using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CorrectionTypes = Humatrix_HRMS.Helpers.CorrectionTypes;

namespace Humatrix_HRMS.Services.AI;

public class AiAssistantService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly CurrentUserService _currentUser;
    private readonly ApplicationDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiAssistantService> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HRPolicyValidationService _policy;

    // Intents that mutate data and therefore ALWAYS require an explicit
    // user confirmation before they are executed.
    private static readonly HashSet<string> WriteIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "APPLY_LEAVE",
        "REQUEST_WFH",
        "REQUEST_OVERTIME",
        "REPORT_MISSING_PUNCH",
        "RESIGNATION",
        "CREATE_TICKET"
    };

    private static readonly JsonSerializerOptions AiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiAssistantService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        CurrentUserService currentUser,
        ApplicationDbContext db,
        IServiceProvider serviceProvider,
        ILogger<AiAssistantService> logger,
        UserManager<ApplicationUser> userManager,
        HRPolicyValidationService policy)
    {
        _httpClient = httpFactory.CreateClient();
        _config = config;
        _currentUser = currentUser;
        _db = db;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _userManager = userManager;
        _policy = policy;
    }

    // ============================================================
    // MAIN ENTRY POINT
    // ============================================================
    public async Task<AiResponse> ProcessQueryAsync(string userQuery)
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null)
            return AiResponse.Error("User not authenticated");

        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.UserId == user.Id);

        var roles = await _userManager.GetRolesAsync(user);
        var userRole = roles.Contains("OrgAdmin") ? "OrgAdmin"
            : roles.Contains("HR") ? "HR"
            : "Employee";

        // ------------------------------------------------------
        // STEP 1: Is there a pending confirmation for this user?
        // ------------------------------------------------------
        var pending = await GetPendingActionAsync(user.Id);

        if (pending != null)
        {
            if (IsAffirmative(userQuery))
            {
                var pendingAction = JsonSerializer.Deserialize<AiAction>(pending.ActionJson, AiJsonOptions)
                    ?? new AiAction { Intent = "ANSWER", ResponseMessage = "Sorry, I lost track of that request." };

                await ClearPendingActionAsync(user.Id);

                var execResult = await ExecuteActionAsync(pendingAction, user, employee, userRole);
                await SaveConversationAsync(user.Id, userQuery, execResult.ResponseText, pendingAction.Intent);
                return execResult;
            }

            if (IsNegative(userQuery))
            {
                await ClearPendingActionAsync(user.Id);
                var cancelled = AiResponse.Answer("👍 Okay, I've cancelled that. Is there anything else I can help with?");
                await SaveConversationAsync(user.Id, userQuery, cancelled.ResponseText, "CANCELLED");
                return cancelled;
            }

            // Anything else => treat as a brand-new request, drop the stale pending action.
            await ClearPendingActionAsync(user.Id);
        }

        // ------------------------------------------------------
        // STEP 2: Build context, ask the LLM what to do
        // ------------------------------------------------------
        var context = await GetUserContextAsync(user, employee, userRole);
        var systemPrompt = BuildSystemPrompt(context, userRole);
        var llmResponse = await CallGroqAsync(systemPrompt, userQuery);
        var action = ParseLlmResponse(llmResponse);

        // ------------------------------------------------------
        // STEP 3: Write actions always go through a confirmation step
        // ------------------------------------------------------
        if (WriteIntents.Contains(action.Intent))
        {
            await SavePendingActionAsync(user.Id, action);

            var confirmText = BuildConfirmationMessage(action);
            var confirmResponse = AiResponse.Confirmation(confirmText);
            await SaveConversationAsync(user.Id, userQuery, confirmResponse.ResponseText, action.Intent);
            return confirmResponse;
        }

        // ------------------------------------------------------
        // STEP 4: Read-only / informational intents execute immediately
        // ------------------------------------------------------
        var result = await ExecuteActionAsync(action, user, employee, userRole);
        await SaveConversationAsync(user.Id, userQuery, result.ResponseText, action.Intent);

        return result;
    }

    // ============================================================
    // CONFIRMATION MESSAGE BUILDER
    // ============================================================
    private string BuildConfirmationMessage(AiAction action)
    {
        var summary = action.ResponseMessage;

        return action.Intent switch
        {
            "CREATE_TICKET" =>
                $"{summary}\n\nShall I raise a support ticket for HR? (Yes / No)",
            "RESIGNATION" =>
                $"{summary}\n\nShall I submit this resignation request? (Yes / No)",
            _ => $"{summary}\n\nShall I go ahead and submit this? (Yes / No)"
        };
    }

    // ============================================================
    // CONTEXT BUILDING
    // ============================================================
    private async Task<UserContext> GetUserContextAsync(ApplicationUser user, Employee? employee, string role)
    {
        var orgId = user.OrganizationId ?? Guid.Empty;

        var context = new UserContext
        {
            UserId = user.Id,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Role = role,
            OrganizationId = orgId
        };

        // Org-local "today" — critical for correct relative date parsing
        if (orgId != Guid.Empty)
        {
            try
            {
                context.TodayDate = (await _policy.GetOrgTodayAsync(orgId)).ToString("yyyy-MM-dd");
            }
            catch
            {
                context.TodayDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
            }

            // Active leave types — required so the LLM only ever uses valid names
            context.AvailableLeaveTypes = await _db.LeaveTypes
                .Where(lt => lt.OrganizationId == orgId && lt.IsActive)
                .Select(lt => lt.Name)
                .ToListAsync();
        }
        else
        {
            context.TodayDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        }

        if (employee != null)
        {
            context.EmployeeId = employee.EmployeeId;
            context.DepartmentId = employee.DepartmentId;
            context.EmployeeCode = employee.EmployeeCode;

            // Leave balances
            var balances = await _db.LeaveBalances
                .Include(b => b.LeaveType)
                .Where(b => b.EmployeeId == employee.EmployeeId && b.Year == DateTime.UtcNow.Year)
                .Select(b => new { b.LeaveType.Name, b.Remaining, b.Pending, b.Used, b.Allocated, b.CarriedForward })
                .ToListAsync();

            foreach (var b in balances)
            {
                context.LeaveBalances[b.Name] = new BalanceInfo
                {
                    Remaining = b.Remaining + b.CarriedForward,
                    Pending = b.Pending,
                    Used = b.Used,
                    Allocated = b.Allocated
                };
            }

            // Today's attendance
            var today = DateTime.UtcNow.Date;
            var todayAttendance = await _db.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employee.EmployeeId && a.WorkDate == today);

            if (todayAttendance != null)
            {
                context.TodayCheckIn = todayAttendance.CheckIn;
                context.TodayCheckOut = todayAttendance.CheckOut;
                context.TodayStatus = todayAttendance.Status;
            }

            // Pending overtime approval flag — helps the model know whether
            // "I worked extra hours" can be actioned right now.
            context.HasPendingOvertimeApproval = await _db.Attendances
                .AnyAsync(a => a.EmployeeId == employee.EmployeeId && a.NeedsOvertimeApproval);
        }

        return context;
    }

    // ============================================================
    // SYSTEM PROMPT
    // ============================================================
    private string BuildSystemPrompt(UserContext context, string role)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are \"Humatrix AI\", the in-app HR Assistant for {context.FullName} (Role: {role}).");
        sb.AppendLine($"Today's date (organization timezone) is {context.TodayDate}. Use this as the reference for words like 'today', 'tomorrow', 'yesterday', 'next Monday', etc.");
        sb.AppendLine();
        sb.AppendLine("## CURRENT CONTEXT (JSON)");
        sb.AppendLine(JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = false }));
        sb.AppendLine();
        sb.AppendLine("## AVAILABLE INTENTS FOR THIS USER");
        sb.AppendLine("- ANSWER: general questions, greetings, policy/FAQ-style answers you can confidently give.");
        sb.AppendLine("- CHECK_LEAVE_BALANCE: show remaining leave balances. parameters: { leaveType? }");
        sb.AppendLine("- APPLY_LEAVE: create a leave request. parameters: { leaveType, fromDate, toDate, reason, isHalfDay }");
        sb.AppendLine("- REQUEST_WFH: apply for work from home. parameters: { date, reason }");
        sb.AppendLine("- REQUEST_OVERTIME: submit an overtime request for a day the employee already checked out late and overtime approval is pending. parameters: { reason }");
        sb.AppendLine("- REPORT_MISSING_PUNCH: create an attendance correction for a forgotten check-in/check-out. parameters: { date, type ('checkin' or 'checkout'), actualTime? ('HH:mm', 24-hour), reason }");
        sb.AppendLine("- RESIGNATION: submit a resignation. parameters: { lastWorkingDay, reason }");
        sb.AppendLine("- SHOW_MY_TASKS: list the employee's pending tasks.");
        sb.AppendLine("- SHOW_TODAY_STATUS: show today's attendance (check-in/check-out/hours).");
        sb.AppendLine("- SHOW_MISSING_DOCUMENTS: show the employee's missing/pending mandatory documents.");
        sb.AppendLine("- CREATE_TICKET: escalate to HR when you cannot safely answer or perform the action (e.g. payroll/salary disputes, policy exceptions, technical issues, anything outside the above). parameters: { category, description }");

        if (role == "HR" || role == "OrgAdmin")
        {
            sb.AppendLine();
            sb.AppendLine("## ADDITIONAL HR/ORGADMIN INTENTS");
            sb.AppendLine("- SHOW_PENDING_APPROVALS: show counts and a short list of pending leave / WFH / overtime / attendance-correction requests awaiting review.");
            sb.AppendLine("- SHOW_MISSING_DOCUMENTS_TEAM: list employees (in scope) who have missing mandatory documents.");
            sb.AppendLine("- SHOW_OPEN_TICKETS: list open AI-escalated support tickets.");
        }

        if (role == "OrgAdmin")
        {
            sb.AppendLine("- SHOW_ATTENDANCE_SUMMARY: show this month's attendance percentage, department-wise, for the organization.");
        }

        sb.AppendLine();
        sb.AppendLine("## RULES");
        sb.AppendLine("1. Always respond with ONLY a single valid JSON object — no markdown, no extra commentary.");
        sb.AppendLine("2. Dates must be in YYYY-MM-DD format, resolved relative to today's date given above.");
        sb.AppendLine("3. leaveType MUST be one of the AvailableLeaveTypes listed in the context (case-insensitive match), unless none fit — then use ANSWER or CREATE_TICKET.");

        sb.AppendLine("3. Never invent or guess a leave type.");
        sb.AppendLine("4. If the user's request does not clearly match an available leave type, use ANSWER and explain which leave types are available.");
        sb.AppendLine("5. Only use APPLY_LEAVE when the leave type is explicitly stated by the user or can be confidently inferred.");

        sb.AppendLine("6. If the user asks something you are not listed as able to do, or it concerns payroll, salary, contracts, legal, or anything you are not certain is safe to answer — use CREATE_TICKET with a clear category and description, and set needs_confirmation = true.");
        sb.AppendLine("7. For any intent that performs an action (everything except ANSWER and the SHOW_* / CHECK_* intents), set needs_confirmation = true and write a clear, human-readable summary of what you are about to do in response_message (it will be shown to the user before they confirm).");
        sb.AppendLine("8. For ANSWER and SHOW_*/CHECK_* intents, set needs_confirmation = false and put the final, complete answer directly in response_message (it is shown to the user as-is).");
        sb.AppendLine("9. Be concise, friendly, and use the user's first name sparingly. Use simple markdown (bold with **, bullet points with •) where helpful.");
        sb.AppendLine();
        sb.AppendLine("## RESPONSE JSON SCHEMA");
        sb.AppendLine(@"{
  ""intent"": ""ACTION_NAME"",
  ""confidence"": 0.0-1.0,
  ""parameters"": { },
  ""response_message"": ""text shown to the user"",
  ""needs_confirmation"": true/false
}");

        return sb.ToString();
    }

    // ============================================================
    // GROQ CALL
    // ============================================================
    private async Task<string> CallGroqAsync(string systemPrompt, string userQuery)
    {
        var apiKey = _config["Groq:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Groq API key missing");
            return FallbackTicketJson("I'm having trouble connecting to the AI service right now.");
        }

        var apiUrl = "https://api.groq.com/openai/v1/chat/completions";

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userQuery }
            },
            temperature = 0.1,
            max_tokens = 1000,
            //response_format = new { type = "json_object" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("================================");
                Console.WriteLine($"STATUS: {response.StatusCode}");
                Console.WriteLine(responseContent);
                Console.WriteLine("================================");

                _logger.LogError("Groq API error ({Status}): {Body}",
                    response.StatusCode,
                    responseContent); return FallbackTicketJson("I'm having trouble processing that request right now.");
            }

            var jsonResponse = JsonSerializer.Deserialize<GroqResponse>(responseContent);
            return jsonResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "{}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Groq API");
            return FallbackTicketJson("The AI service is temporarily unavailable.");
        }
    }

    private static string FallbackTicketJson(string reason)
    {
        var msg = $"{reason} I can raise a support ticket so HR can help instead.";
        var obj = new
        {
            intent = "CREATE_TICKET",
            confidence = 0.3,
            parameters = new { category = "General", description = msg },
            response_message = msg,
            needs_confirmation = true
        };
        return JsonSerializer.Serialize(obj);
    }

    // ============================================================
    // LLM RESPONSE PARSING
    // ============================================================
    private AiAction ParseLlmResponse(string llmResponse)
    {
        try
        {
            // Some models occasionally wrap JSON in ```json fences despite instructions
            var cleaned = llmResponse.Trim();
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Trim('`');
                var firstBrace = cleaned.IndexOf('{');
                var lastBrace = cleaned.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            var parsed = JsonSerializer.Deserialize<AiAction>(cleaned, AiJsonOptions);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Intent))
            {
                return new AiAction
                {
                    Intent = "ANSWER",
                    Parameters = new Dictionary<string, object>(),
                    ResponseMessage = "I'm not sure I understood that — could you rephrase?",
                    NeedsConfirmation = false
                };
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response: {Response}", llmResponse);
            return new AiAction
            {
                Intent = "ANSWER",
                Parameters = new Dictionary<string, object>(),
                ResponseMessage = "Sorry, I had trouble understanding that. Could you try rephrasing?",
                NeedsConfirmation = false
            };
        }
    }

    // ============================================================
    // ACTION DISPATCH
    // ============================================================
    private async Task<AiResponse> ExecuteActionAsync(AiAction action, ApplicationUser user, Employee? employee, string role)
    {
        try
        {
            return action.Intent.ToUpperInvariant() switch
            {
                "CHECK_LEAVE_BALANCE" => await HandleCheckLeaveBalance(action, employee),
                "APPLY_LEAVE" => await HandleApplyLeave(action, employee),
                "REQUEST_WFH" => await HandleRequestWfh(action, employee),
                "REQUEST_OVERTIME" => await HandleRequestOvertime(action, employee),
                "REPORT_MISSING_PUNCH" => await HandleMissingPunch(action, employee),
                "RESIGNATION" => await HandleResignation(action, employee),
                "SHOW_MY_TASKS" => await HandleShowTasks(employee),
                "SHOW_TODAY_STATUS" => await HandleShowTodayStatus(employee),
                "SHOW_MISSING_DOCUMENTS" => await HandleShowMissingDocuments(employee, user),
                "SHOW_PENDING_APPROVALS" => await HandleShowPendingApprovals(user, employee, role),
                "SHOW_MISSING_DOCUMENTS_TEAM" => await HandleShowMissingDocumentsTeam(user, employee, role),
                "SHOW_OPEN_TICKETS" => await HandleShowOpenTickets(user, role),
                "SHOW_ATTENDANCE_SUMMARY" => await HandleShowAttendanceSummary(user, role),
                "CREATE_TICKET" => await HandleCreateTicket(action, user, employee),
                _ => AiResponse.Answer(string.IsNullOrWhiteSpace(action.ResponseMessage)
                        ? "I'm here to help with leave, WFH, overtime, attendance and more — what would you like to do?"
                        : action.ResponseMessage)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AI action {Intent}", action.Intent);
            return AiResponse.Error($"Sorry, something went wrong while doing that: {ex.Message}");
        }
    }

    #region Employee Handlers

    private async Task<AiResponse> HandleCheckLeaveBalance(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var leaveTypeName = GetStr(action.Parameters, "leaveType");

        var balances = await _db.LeaveBalances
            .Include(b => b.LeaveType)
            .Where(b => b.EmployeeId == employee.EmployeeId && b.Year == DateTime.UtcNow.Year)
            .ToListAsync();

        if (!string.IsNullOrEmpty(leaveTypeName))
        {
            var bal = balances.FirstOrDefault(b => b.LeaveType.Name.Contains(leaveTypeName, StringComparison.OrdinalIgnoreCase));
            if (bal != null)
            {
                return AiResponse.Answer(
                    $"**{bal.LeaveType.Name} Balance**\n" +
                    $"• Remaining: {bal.Remaining + bal.CarriedForward} day(s)\n" +
                    $"• Used: {bal.Used} day(s)\n" +
                    $"• Pending approval: {bal.Pending} day(s)");
            }
            return AiResponse.Answer($"I couldn't find a leave type matching '{leaveTypeName}'.");
        }

        if (!balances.Any())
            return AiResponse.Answer("No leave balances have been set up for you yet. Please contact HR.");

        var summary = string.Join("\n", balances.Select(b =>
            $"• **{b.LeaveType.Name}:** {b.Remaining + b.CarriedForward} day(s) remaining"));

        return AiResponse.Answer($"**Your Leave Balances**\n{summary}");
    }

    private async Task<AiResponse> HandleApplyLeave(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var leaveTypeName = GetStr(action.Parameters, "leaveType") ?? "";
        var fromDateStr = GetStr(action.Parameters, "fromDate");
        var toDateStr = GetStr(action.Parameters, "toDate");
        var reason = GetStr(action.Parameters, "reason") ?? "Applied via AI Assistant";
        var isHalfDay = GetBool(action.Parameters, "isHalfDay");

        if (string.IsNullOrEmpty(fromDateStr))
            return AiResponse.Error("Please tell me the start date for your leave.");

        if (!DateTime.TryParse(fromDateStr, out var fromDate))
            return AiResponse.Error($"I couldn't understand the date '{fromDateStr}'.");

        var toDate = fromDate;
        if (!string.IsNullOrEmpty(toDateStr) && !DateTime.TryParse(toDateStr, out toDate))
            return AiResponse.Error($"I couldn't understand the date '{toDateStr}'.");

        var leaveType = await _db.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.OrganizationId == employee.OrganizationId
                && lt.IsActive
                && lt.Name.Contains(leaveTypeName, StringComparison.OrdinalIgnoreCase));

        if (leaveType == null)
            return AiResponse.Error($"I couldn't find an active leave type called '{leaveTypeName}'. Please check available leave types.");

        var leaveRequest = new LeaveRequest
        {
            LeaveRequestId = Guid.NewGuid(),
            LeaveTypeId = leaveType.LeaveTypeId,
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            Reason = reason,
            IsHalfDay = isHalfDay
        };

        using var scope = _serviceProvider.CreateScope();
        var leaveService = scope.ServiceProvider.GetRequiredService<LeaveService>();

        await leaveService.ApplyLeaveAsync(leaveRequest);

        return AiResponse.ActionSuccess(
            $"✅ **Leave Request Submitted!**\n\n" +
            $"• Type: {leaveType.Name}\n" +
            $"• Dates: {fromDate:dd MMM yyyy} - {toDate:dd MMM yyyy}\n" +
            $"• Days: {leaveRequest.TotalDays}\n" +
            $"• Status: Pending approval\n\n" +
            $"You'll be notified once HR reviews your request.",
            leaveRequest.LeaveRequestId);
    }

    private async Task<AiResponse> HandleRequestWfh(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var dateStr = GetStr(action.Parameters, "date");
        var reason = GetStr(action.Parameters, "reason") ?? "Applied via AI Assistant";

        if (string.IsNullOrEmpty(dateStr) || !DateTime.TryParse(dateStr, out var date))
            return AiResponse.Error("Please tell me the date for your work-from-home request.");

        var wfhRequest = new WorkFromHomeRequest
        {
            Date = date.Date,
            Reason = reason
        };

        using var scope = _serviceProvider.CreateScope();
        var wfhService = scope.ServiceProvider.GetRequiredService<WorkFromHomeService>();

        await wfhService.ApplyAsync(wfhRequest);

        return AiResponse.ActionSuccess(
            $"✅ **Work From Home Request Submitted!**\n\n" +
            $"• Date: {date:dd MMM yyyy}\n" +
            $"• Reason: {reason}\n" +
            $"• Status: Pending approval",
            wfhRequest.Id);
    }

    private async Task<AiResponse> HandleRequestOvertime(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var reason = GetStr(action.Parameters, "reason") ?? "Submitted via AI Assistant";

        // Find the most recent attendance record awaiting overtime approval
        var attendance = await _db.Attendances
            .Where(a => a.EmployeeId == employee.EmployeeId && a.NeedsOvertimeApproval)
            .OrderByDescending(a => a.WorkDate)
            .FirstOrDefaultAsync();

        if (attendance == null)
            return AiResponse.Error("I couldn't find any attendance record awaiting overtime approval. Overtime requests can only be raised after you check out later than your shift end time.");

        if (attendance.ActualCheckOut == null)
            return AiResponse.Error("Your checkout time for that day isn't recorded yet, so I can't calculate overtime. Please contact HR.");

        var dto = new CreateOvertimeRequestDto
        {
            AttendanceId = attendance.AttendanceId,
            ActualCheckOut = DateTime.SpecifyKind(attendance.ActualCheckOut.Value, DateTimeKind.Utc),
            Reason = reason
        };

        using var scope = _serviceProvider.CreateScope();
        var overtimeService = scope.ServiceProvider.GetRequiredService<OvertimeService>();

        await overtimeService.RaiseRequestAsync(dto);

        var otHours = attendance.OvertimeHours ?? 0;

        return AiResponse.ActionSuccess(
            $"✅ **Overtime Request Submitted!**\n\n" +
            $"• Date: {attendance.WorkDate:dd MMM yyyy}\n" +
            $"• Overtime: {otHours:0.##} hour(s)\n" +
            $"• Reason: {reason}\n" +
            $"• Status: Pending approval");
    }

    private async Task<AiResponse> HandleMissingPunch(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var dateStr = GetStr(action.Parameters, "date");
        var punchType = (GetStr(action.Parameters, "type") ?? "checkout").Trim().ToLowerInvariant();
        var actualTimeStr = GetStr(action.Parameters, "actualTime");
        var reason = GetStr(action.Parameters, "reason") ?? $"Missing {punchType} reported via AI Assistant";

        if (string.IsNullOrEmpty(dateStr) || !DateTime.TryParse(dateStr, out var date))
            return AiResponse.Error("Please tell me which date you'd like to correct.");

        var correctionType = punchType.Contains("in") ? CorrectionTypes.ForgotCheckIn : CorrectionTypes.ForgotCheckOut;

        DateTime? requestedCheckIn = null;
        DateTime? requestedCheckOut = null;

        if (!string.IsNullOrEmpty(actualTimeStr) && TimeSpan.TryParse(actualTimeStr, out var timeOfDay))
        {
            var combined = DateTime.SpecifyKind(date.Date.Add(timeOfDay), DateTimeKind.Unspecified);
            if (correctionType == CorrectionTypes.ForgotCheckIn)
                requestedCheckIn = combined;
            else
                requestedCheckOut = combined;
        }

        var dto = new SubmitCorrectionRequestDto
        {
            CorrectionType = correctionType,
            WorkDate = date.Date,
            RequestedCheckIn = requestedCheckIn,
            RequestedCheckOut = requestedCheckOut,
            Reason = reason
        };

        using var scope = _serviceProvider.CreateScope();
        var correctionService = scope.ServiceProvider.GetRequiredService<AttendanceCorrectionService>();

        await correctionService.SubmitAsync(dto);

        var requestedTime = requestedCheckIn ?? requestedCheckOut;

        return AiResponse.ActionSuccess(
            $"✅ **Attendance Correction Request Created!**\n\n" +
            $"• Date: {date:dd MMM yyyy}\n" +
            $"• Type: Missing {(correctionType == CorrectionTypes.ForgotCheckIn ? "check-in" : "check-out")}\n" +
            (requestedTime is not null
                ? $"• Requested time: {requestedTime.Value:hh:mm tt}\n"
                : "") +
            $"• Status: Pending HR review");
    }

    private async Task<AiResponse> HandleResignation(AiAction action, Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var lastWorkingDayStr = GetStr(action.Parameters, "lastWorkingDay");
        var reason = GetStr(action.Parameters, "reason") ?? "Not specified";

        if (string.IsNullOrEmpty(lastWorkingDayStr) || !DateTime.TryParse(lastWorkingDayStr, out var lastWorkingDay))
            return AiResponse.Error("Please tell me your intended last working day.");

        using var scope = _serviceProvider.CreateScope();
        var exitService = scope.ServiceProvider.GetRequiredService<EmployeeExitService>();

        var exit = await exitService.SubmitResignationAsync(lastWorkingDay.Date, reason, "Submitted via AI Assistant");

        return AiResponse.ActionSuccess(
            $"✅ **Resignation Submitted**\n\n" +
            $"• Last Working Day: {lastWorkingDay:dd MMM yyyy}\n" +
            $"• Reason: {reason}\n" +
            $"• Status: Pending approval\n\n" +
            $"HR will review your request and reach out to you.",
            exit.ExitId);
    }

    private async Task<AiResponse> HandleShowTasks(Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var tasks = await _db.Tasks
            .Where(t => t.AssignedTo == employee.EmployeeId && t.Status != "Completed")
            .OrderBy(t => t.DueDate)
            .ToListAsync();

        if (!tasks.Any())
            return AiResponse.Answer("🎉 **You have no pending tasks!** Great job!");

        var taskList = string.Join("\n", tasks.Select(t =>
            $"• **{t.Title}** - Due: {(t.DueDate?.ToString("dd MMM yyyy") ?? "No due date")} - Progress: {t.Progress}%"));

        return AiResponse.Answer($"📋 **Your Pending Tasks ({tasks.Count})**\n\n{taskList}");
    }

    private async Task<AiResponse> HandleShowTodayStatus(Employee? employee)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        var today = DateTime.UtcNow.Date;
        var attendance = await _db.Attendances
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.EmployeeId && a.WorkDate == today);

        if (attendance == null)
            return AiResponse.Answer("📅 **No attendance record for today yet.** Have you checked in?");

        var checkInStr = attendance.CheckIn.HasValue ? attendance.CheckIn.Value.ToLocalTime().ToString("hh:mm tt") : "Not checked in";
        var checkOutStr = attendance.CheckOut.HasValue ? attendance.CheckOut.Value.ToLocalTime().ToString("hh:mm tt") : "Not checked out";

        return AiResponse.Answer(
            $"📊 **Today's Attendance**\n\n" +
            $"• Status: {attendance.Status ?? "Present"}\n" +
            $"• Check In: {checkInStr}\n" +
            $"• Check Out: {checkOutStr}\n" +
            $"• Total Hours: {attendance.TotalHours?.ToString("N1") ?? "0"} hours");
    }

    private async Task<AiResponse> HandleShowMissingDocuments(Employee? employee, ApplicationUser user)
    {
        if (employee == null)
            return AiResponse.Error("Employee profile not found.");

        using var scope = _serviceProvider.CreateScope();
        var docService = scope.ServiceProvider.GetRequiredService<IEmployeeDocumentService>();

        var dashboard = await docService.GetEmployeeDocumentDashboardAsync(employee.EmployeeId, employee.OrganizationId);

        if (!dashboard.MissingMandatoryDocuments.Any())
            return AiResponse.Answer($"✅ **All set!** You have no missing mandatory documents." +
                (dashboard.ProfileCompletionPercentage.HasValue
                    ? $" Profile completion: {dashboard.ProfileCompletionPercentage}%."
                    : ""));

        var list = string.Join("\n", dashboard.MissingMandatoryDocuments.Select(d => $"• {d.Name}"));

        return AiResponse.Answer(
            $"📄 **Missing Documents ({dashboard.MissingMandatoryDocuments.Count})**\n\n{list}" +
            (dashboard.ProfileCompletionPercentage.HasValue
                ? $"\n\nProfile completion: {dashboard.ProfileCompletionPercentage}%"
                : "") +
            "\n\nYou can upload these from the Documents page.");
    }

    #endregion

    #region HR / OrgAdmin Handlers

    private async Task<AiResponse> HandleShowPendingApprovals(ApplicationUser user, Employee? employee, string role)
    {
        var orgId = user.OrganizationId ?? Guid.Empty;
        if (orgId == Guid.Empty)
            return AiResponse.Error("Organization context not found.");

        IQueryable<LeaveRequest> leaveQuery = _db.LeaveRequests
            .Include(l => l.Employee)
            .Where(l => l.Employee.OrganizationId == orgId && l.Status == "Pending");

        IQueryable<WorkFromHomeRequest> wfhQuery = _db.WorkFromHomeRequests
            .Include(w => w.Employee)
            .Where(w => w.Employee.OrganizationId == orgId && w.Status == "Pending");

        IQueryable<OvertimeRequest> otQuery = _db.OvertimeRequests
            .Include(o => o.Employee)
            .Where(o => o.Employee.OrganizationId == orgId && o.Status == "Pending");

        IQueryable<AttendanceCorrectionRequest> corrQuery = _db.AttendanceCorrectionRequests
            .Include(c => c.Employee)
            .Where(c => c.OrganizationId == orgId && c.Status == "Pending");

        // HR (non-OrgAdmin) is scoped to their own department, employee-submitted requests only
        if (role == "HR" && employee != null)
        {
            leaveQuery = leaveQuery.Where(l => l.Employee.DepartmentId == employee.DepartmentId);
            wfhQuery = wfhQuery.Where(w => w.Employee.DepartmentId == employee.DepartmentId);
            otQuery = otQuery.Where(o => o.Employee.DepartmentId == employee.DepartmentId);
            corrQuery = corrQuery.Where(c => c.Employee.DepartmentId == employee.DepartmentId);
        }

        var leaveCount = await leaveQuery.CountAsync();
        var wfhCount = await wfhQuery.CountAsync();
        var otCount = await otQuery.CountAsync();
        var corrCount = await corrQuery.CountAsync();

        var total = leaveCount + wfhCount + otCount + corrCount;

        if (total == 0)
            return AiResponse.Answer("✅ **No pending approvals!** You're all caught up.");

        var sb = new StringBuilder();
        sb.AppendLine($"📋 **Pending Approvals ({total})**\n");
        if (leaveCount > 0) sb.AppendLine($"• Leave Requests: **{leaveCount}**");
        if (wfhCount > 0) sb.AppendLine($"• Work From Home Requests: **{wfhCount}**");
        if (otCount > 0) sb.AppendLine($"• Overtime Requests: **{otCount}**");
        if (corrCount > 0) sb.AppendLine($"• Attendance Corrections: **{corrCount}**");

        return AiResponse.Answer(sb.ToString().TrimEnd());
    }

    private async Task<AiResponse> HandleShowMissingDocumentsTeam(ApplicationUser user, Employee? employee, string role)
    {
        var orgId = user.OrganizationId ?? Guid.Empty;
        if (orgId == Guid.Empty)
            return AiResponse.Error("Organization context not found.");

        var employeesQuery = _db.Employees.Where(e => e.OrganizationId == orgId && e.Status == "Active");

        if (role == "HR" && employee != null)
            employeesQuery = employeesQuery.Where(e => e.DepartmentId == employee.DepartmentId);

        var employees = await employeesQuery
            .Select(e => new { e.EmployeeId, e.FirstName, e.LastName })
            .ToListAsync();

        if (!employees.Any())
            return AiResponse.Answer("No employees found in your scope.");

        var mandatoryTypes = await _db.DocumentTypes
            .Where(dt => dt.OrganizationId == orgId && dt.IsActive && dt.IsMandatory)
            .ToListAsync();

        if (!mandatoryTypes.Any())
            return AiResponse.Answer("No mandatory document types have been configured for your organization.");

        var employeeIds = employees.Select(e => e.EmployeeId).ToList();

        var uploaded = await _db.EmployeeDocuments
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.IsLatestVersion && !d.IsDeleted)
            .Select(d => new { d.EmployeeId, d.DocumentTypeId })
            .ToListAsync();

        var uploadedLookup = uploaded
            .GroupBy(d => d.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DocumentTypeId).ToHashSet());

        var results = new List<string>();

        foreach (var emp in employees)
        {
            var uploadedTypeIds = uploadedLookup.TryGetValue(emp.EmployeeId, out var set) ? set : new HashSet<Guid>();
            var missing = mandatoryTypes
                .Where(mt => !uploadedTypeIds.Contains(mt.DocumentTypeId))
                .Select(mt => mt.Name)
                .ToList();

            if (missing.Any())
                results.Add($"• **{emp.FirstName} {emp.LastName}** — {string.Join(", ", missing)}");
        }

        if (!results.Any())
            return AiResponse.Answer("✅ **All employees in your scope have submitted their mandatory documents.**");

        const int maxShown = 15;
        var shown = results.Take(maxShown).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"📄 **{results.Count} employee(s) with missing documents**\n");
        sb.AppendLine(string.Join("\n", shown));

        if (results.Count > maxShown)
            sb.AppendLine($"\n…and {results.Count - maxShown} more. View the full list on the Documents Compliance page.");

        return AiResponse.Answer(sb.ToString().TrimEnd());
    }

    private async Task<AiResponse> HandleShowOpenTickets(ApplicationUser user, string role)
    {
        using var scope = _serviceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<TicketService>();

        var tickets = await ticketService.GetTicketsForHRAsync("Open");

        if (!tickets.Any())
            return AiResponse.Answer("✅ **No open tickets.** Inbox zero!");

        const int maxShown = 10;
        var list = string.Join("\n", tickets.Take(maxShown).Select(t =>
            $"• **#{t.TicketNumber}** [{t.Priority}] {t.Category} — {(t.Employee != null ? $"{t.Employee.FirstName} {t.Employee.LastName}" : "Unknown")}"));

        var sb = new StringBuilder();
        sb.AppendLine($"🎫 **Open Tickets ({tickets.Count})**\n");
        sb.AppendLine(list);

        if (tickets.Count > maxShown)
            sb.AppendLine($"\n…and {tickets.Count - maxShown} more. View all on the Tickets page.");

        return AiResponse.Answer(sb.ToString().TrimEnd());
    }

    private async Task<AiResponse> HandleShowAttendanceSummary(ApplicationUser user, string role)
    {
        var orgId = user.OrganizationId ?? Guid.Empty;
        if (orgId == Guid.Empty)
            return AiResponse.Error("Organization context not found.");

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var data = await (
            from a in _db.Attendances
            join e in _db.Employees on a.EmployeeId equals e.EmployeeId
            join d in _db.Departments on e.DepartmentId equals d.DepartmentId
            where e.OrganizationId == orgId
                  && a.WorkDate >= monthStart
                  && a.WorkDate < monthEnd
            group a by d.Name into g
            select new
            {
                Department = g.Key,
                Total = g.Count(),
                Present = g.Count(x => x.IsPresent)
            }).ToListAsync();

        if (!data.Any())
            return AiResponse.Answer("No attendance data is available for this month yet.");

        var sb = new StringBuilder();
        sb.AppendLine($"📊 **Attendance Summary — {monthStart:MMMM yyyy}**\n");

        foreach (var d in data.OrderByDescending(x => x.Total == 0 ? 0 : (double)x.Present / x.Total))
        {
            var pct = d.Total == 0 ? 0 : Math.Round((double)d.Present / d.Total * 100, 1);
            sb.AppendLine($"• **{d.Department}**: {pct}%");
        }

        return AiResponse.Answer(sb.ToString().TrimEnd());
    }

    #endregion

    #region Ticket Handler

    private async Task<AiResponse> HandleCreateTicket(AiAction action, ApplicationUser user, Employee? employee)
    {
        var category = GetStr(action.Parameters, "category") ?? "General";
        var description = GetStr(action.Parameters, "description") ?? action.ResponseMessage;

        var lastTicket = await _db.SupportTickets
            .OrderByDescending(t => t.TicketNumber)
            .FirstOrDefaultAsync();

        var ticketNumber = (lastTicket?.TicketNumber ?? 1000) + 1;
        var priority = DeterminePriority(category, description);

        var ticket = new SupportTicket
        {
            TicketId = Guid.NewGuid(),
            TicketNumber = ticketNumber,
            EmployeeId = employee?.EmployeeId,
            UserId = user.Id,
            OrganizationId = user.OrganizationId ?? Guid.Empty,
            Category = category,
            Description = description,
            Priority = priority,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync();

        await NotifyNewTicket(ticket, description);

        return AiResponse.ActionSuccess(
            $"🎫 **Support Ticket Created!**\n\n" +
            $"• Ticket #{ticketNumber}\n" +
            $"• Category: {category}\n" +
            $"• Priority: {priority}\n" +
            $"• Status: Open\n\n" +
            $"HR will review your issue and respond shortly.",
            ticket.TicketId);
    }

    private static string DeterminePriority(string category, string description)
    {
        var urgentKeywords = new[] { "urgent", "emergency", "critical", "blocked", "not working", "can't login", "cannot login" };
        var highKeywords = new[] { "salary", "payment", "payroll", "access", "permission" };

        var lowerDesc = description.ToLowerInvariant();
        var lowerCat = category.ToLowerInvariant();

        if (urgentKeywords.Any(k => lowerDesc.Contains(k)))
            return "Urgent";
        if (highKeywords.Any(k => lowerDesc.Contains(k) || lowerCat.Contains(k)))
            return "High";
        return "Medium";
    }

    private async Task NotifyNewTicket(SupportTicket ticket, string description)
    {
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();

        await notificationService.CreateOrgAdminNotificationsAsync(
            ticket.OrganizationId,
            $"🎫 New Support Ticket: #{ticket.TicketNumber}",
            $"Category: {ticket.Category}\nPriority: {ticket.Priority}\n\n{description}",
            "/admin/tickets");
    }

    #endregion

    #region Pending Confirmation Storage

    private async Task<PendingAiAction?> GetPendingActionAsync(string userId)
    {
        var now = DateTime.UtcNow;

        var pending = await _db.PendingAiActions
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (pending == null)
            return null;

        if (pending.ExpiresAt < now)
        {
            _db.PendingAiActions.Remove(pending);
            await _db.SaveChangesAsync();
            return null;
        }

        return pending;
    }

    private async Task SavePendingActionAsync(string userId, AiAction action)
    {
        // Remove any previous pending actions for this user first
        var existing = await _db.PendingAiActions.Where(p => p.UserId == userId).ToListAsync();
        if (existing.Any())
            _db.PendingAiActions.RemoveRange(existing);

        _db.PendingAiActions.Add(new PendingAiAction
        {
            PendingActionId = Guid.NewGuid(),
            UserId = userId,
            ActionJson = JsonSerializer.Serialize(action, AiJsonOptions),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        await _db.SaveChangesAsync();
    }

    private async Task ClearPendingActionAsync(string userId)
    {
        var existing = await _db.PendingAiActions.Where(p => p.UserId == userId).ToListAsync();
        if (existing.Any())
        {
            _db.PendingAiActions.RemoveRange(existing);
            await _db.SaveChangesAsync();
        }
    }

    private static readonly string[] AffirmativeWords =
    {
        "yes", "y", "yeah", "yep", "yup", "confirm", "confirmed", "ok", "okay",
        "sure", "go ahead", "go for it", "please do", "do it", "submit", "proceed",
        "correct", "right", "affirmative"
    };

    private static readonly string[] NegativeWords =
    {
        "no", "n", "nope", "nah", "cancel", "don't", "do not", "stop", "never mind",
        "nevermind", "negative", "skip"
    };

    private static bool IsAffirmative(string text)
    {
        var t = text.Trim().Trim('.', '!', '?').ToLowerInvariant();
        return AffirmativeWords.Any(w => t == w || t.StartsWith(w + " "));
    }

    private static bool IsNegative(string text)
    {
        var t = text.Trim().Trim('.', '!', '?').ToLowerInvariant();
        return NegativeWords.Any(w => t == w || t.StartsWith(w + " "));
    }

    #endregion

    #region Parameter Helpers

    private static string? GetStr(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var val) || val == null)
            return null;

        if (val is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.ToString()
            };
        }

        return val.ToString();
    }

    private static bool GetBool(Dictionary<string, object> parameters, string key)
    {
        var s = GetStr(parameters, key);
        return !string.IsNullOrEmpty(s) && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
    }

    #endregion

    private async Task SaveConversationAsync(string userId, string query, string response, string intent)
    {
        var conversation = new AiConversation
        {
            ConversationId = Guid.NewGuid(),
            UserId = userId,
            Query = query.Length > 2000 ? query.Substring(0, 2000) : query,
            Response = response.Length > 4000 ? response.Substring(0, 4000) : response,
            Intent = intent,
            CreatedAt = DateTime.UtcNow
        };

        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
    }
}

// ============================================================
// DTOs and Helper Classes
// ============================================================

public class UserContext
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? EmployeeCode { get; set; }
    public string TodayDate { get; set; } = string.Empty;
    public DateTime? TodayCheckIn { get; set; }
    public DateTime? TodayCheckOut { get; set; }
    public string? TodayStatus { get; set; }
    public bool HasPendingOvertimeApproval { get; set; }
    public Dictionary<string, BalanceInfo> LeaveBalances { get; set; } = new();
    public List<string> AvailableLeaveTypes { get; set; } = new();
}

public class BalanceInfo
{
    public decimal Remaining { get; set; }
    public decimal Pending { get; set; }
    public decimal Used { get; set; }
    public decimal Allocated { get; set; }
}

public class AiAction
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();

    [JsonPropertyName("response_message")]
    public string ResponseMessage { get; set; } = string.Empty;

    [JsonPropertyName("needs_confirmation")]
    public bool NeedsConfirmation { get; set; }
}

public class AiResponse
{
    public bool IsSuccess { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public bool RequiresConfirmation { get; set; }

    public static AiResponse Answer(string text) => new()
    {
        IsSuccess = true,
        ResponseText = text,
        RequiresConfirmation = false
    };

    public static AiResponse Confirmation(string text) => new()
    {
        IsSuccess = true,
        ResponseText = text,
        RequiresConfirmation = true
    };

    public static AiResponse ActionSuccess(string text, Guid? referenceId = null) => new()
    {
        IsSuccess = true,
        ResponseText = text,
        ReferenceId = referenceId,
        RequiresConfirmation = false
    };

    public static AiResponse Error(string text) => new()
    {
        IsSuccess = false,
        ResponseText = text,
        RequiresConfirmation = false
    };
}

// Groq API response models
internal class GroqResponse
{
    [JsonPropertyName("choices")]
    public List<GroqChoice>? Choices { get; set; }
}

internal class GroqChoice
{
    [JsonPropertyName("message")]
    public GroqMessage? Message { get; set; }
}

internal class GroqMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}   