// Services/AI/TicketService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.AI;

/// <summary>
/// Centralised helpdesk service.
///
/// Scope rules (enforced at the service layer — never trust the UI):
///   OrgAdmin  → sees / manages ALL tickets within their organisation.
///   HR        → sees / manages ONLY tickets raised by employees in the
///               same department as the HR's own employee record.
///               An HR user cannot see tickets they raised themselves
///               (those appear in their employee "My Tickets" view).
///   Employee  → sees / replies to ONLY tickets they personally raised.
/// </summary>
public class TicketService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notificationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TicketService> _logger;

    public TicketService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        CurrentUserService currentUser,
        NotificationService notificationService,
        UserManager<ApplicationUser> userManager,
        ILogger<TicketService> logger)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
        _notificationService = notificationService;
        _userManager = userManager;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INTERNAL HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the caller's role context.  Never throws — returns null on failure.
    /// </summary>
    private async Task<CallerContext?> GetCallerContextAsync()
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);

        await using var db = await _dbFactory.CreateDbContextAsync();

        var employee = await db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == user.Id);

        return new CallerContext
        {
            UserId = user.Id,
            OrganizationId = user.OrganizationId ?? Guid.Empty,
            IsOrgAdmin = roles.Contains("OrgAdmin"),
            IsHR = roles.Contains("HR"),
            Employee = employee
        };
    }

    /// <summary>
    /// Builds an IQueryable scoped to exactly what the current caller is
    /// authorised to see. This is the single source of truth for all list
    /// and existence checks — every public method uses it.
    /// </summary>
    private IQueryable<SupportTicket> ScopedTicketQuery(
        ApplicationDbContext db,
        CallerContext ctx)
    {
        var query = db.SupportTickets
            .Where(t => t.OrganizationId == ctx.OrganizationId);

        if (ctx.IsOrgAdmin)
        {
            // OrgAdmin sees ALL tickets in their org.
            return query;
        }

        if (ctx.IsHR)
        {
            if (ctx.Employee == null)
            {
                // HR with no employee record — return nothing (safety net).
                return query.Where(_ => false);
            }

            var deptId = ctx.Employee.DepartmentId;

            // HR sees tickets from their department employees only.
            // They do NOT see tickets they raised themselves (those are in "My Tickets").
            return query.Where(t =>
                t.Employee != null &&
                t.Employee.DepartmentId == deptId &&
                t.UserId != ctx.UserId);
        }

        // Employee — should not call management methods, but guard anyway.
        return query.Where(t => t.UserId == ctx.UserId);
    }

    /// <summary>
    /// Verifies that the given ticket is within the caller's scope.
    /// Throws UnauthorizedAccessException on scope violation.
    /// </summary>
    private async Task AssertTicketInScopeAsync(
        ApplicationDbContext db,
        CallerContext ctx,
        Guid ticketId)
    {
        var exists = await ScopedTicketQuery(db, ctx)
            .AnyAsync(t => t.TicketId == ticketId);

        if (!exists)
        {
            _logger.LogWarning(
                "Scope violation: user {UserId} attempted to access ticket {TicketId}",
                ctx.UserId, ticketId);

            throw new UnauthorizedAccessException(
                "You do not have access to this ticket.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // READ — FOR HR / ORGADMIN MANAGEMENT VIEW
    // ═══════════════════════════════════════════════════════════════════════

    //public async Task<List<SupportTicket>> GetTicketsForHRAsync(
    //    string? status = null,
    //    string? priority = null,
    //    string? category = null,
    //    string? assignedToUserId = null,
    //    string? search = null)
    //{
    //    var ctx = await GetCallerContextAsync();
    //    if (ctx == null || ctx.OrganizationId == Guid.Empty)
    //        return new List<SupportTicket>();

    //    await using var db = await _dbFactory.CreateDbContextAsync();

    //    var query = ScopedTicketQuery(db, ctx)
    //        .Include(t => t.Employee)
    //            .ThenInclude(e => e.Department)  // Add this to get department name
    //        .Include(t => t.AssignedTo)
    //        .Include(t => t.ResolvedBy)
    //        .Include(t => t.ClosedBy)
    //        .AsNoTracking();

    //    if (!string.IsNullOrWhiteSpace(status))
    //        query = query.Where(t => t.Status == status);

    //    if (!string.IsNullOrWhiteSpace(priority))
    //        query = query.Where(t => t.Priority == priority);

    //    if (!string.IsNullOrWhiteSpace(category))
    //        query = query.Where(t => t.Category == category);

    //    if (!string.IsNullOrWhiteSpace(assignedToUserId))
    //        query = query.Where(t => t.AssignedToUserId == assignedToUserId);

    //    if (!string.IsNullOrWhiteSpace(search))
    //    {
    //        var s = search.ToLower();
    //        query = query.Where(t =>
    //            t.Description.ToLower().Contains(s) ||
    //            t.Category.ToLower().Contains(s) ||
    //            (t.Employee != null && (t.Employee.FirstName + " " + t.Employee.LastName).ToLower().Contains(s)));
    //    }

    //    return await query
    //        .OrderByDescending(t => t.CreatedAt)
    //        .ToListAsync();
    //}
    public async Task<List<SupportTicket>> GetTicketsForHRAsync(
     string? status = null,
     string? priority = null,
     string? category = null,
     string? assignedToUserId = null,
     string? search = null)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || ctx.OrganizationId == Guid.Empty)
            return new List<SupportTicket>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = ScopedTicketQuery(db, ctx)
            .Include(t => t.Employee)
                .ThenInclude(e => e.Department)
            .Include(t => t.AssignedTo)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(t => t.Priority == priority);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(t => t.Category == category);

        if (!string.IsNullOrWhiteSpace(assignedToUserId))
            query = query.Where(t => t.AssignedToUserId == assignedToUserId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(t =>
                t.Description.ToLower().Contains(s) ||
                t.Category.ToLower().Contains(s) ||
                (t.Employee != null && (t.Employee.FirstName + " " + t.Employee.LastName).ToLower().Contains(s)));
        }

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Load ResolvedBy and ClosedBy users and their roles
        foreach (var ticket in tickets)
        {
            if (!string.IsNullOrEmpty(ticket.ResolvedByUserId))
            {
                var user = await _userManager.FindByIdAsync(ticket.ResolvedByUserId);
                if (user != null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("OrgAdmin"))
                    {
                        // Create a placeholder user for OrgAdmin
                        ticket.ResolvedBy = new ApplicationUser
                        {
                            Id = user.Id,
                            FirstName = "OrgAdmin",
                            LastName = "",
                            UserName = user.UserName
                        };
                    }
                    else if (roles.Contains("HR"))
                    {
                        ticket.ResolvedBy = user;
                    }
                }
            }

            if (!string.IsNullOrEmpty(ticket.ClosedByUserId))
            {
                var user = await _userManager.FindByIdAsync(ticket.ClosedByUserId);
                if (user != null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("OrgAdmin"))
                    {
                        ticket.ClosedBy = new ApplicationUser
                        {
                            Id = user.Id,
                            FirstName = "OrgAdmin",
                            LastName = "",
                            UserName = user.UserName
                        };
                    }
                    else if (roles.Contains("HR"))
                    {
                        ticket.ClosedBy = user;
                    }
                }
            }
        }

        return tickets;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // READ — FOR EMPLOYEE "MY TICKETS"
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<SupportTicket>> GetMyTicketsAsync()
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null) return new List<SupportTicket>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Only the user's own tickets — no OR-conditions that could leak
        // another employee's records.
        return await db.SupportTickets
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // READ — SINGLE TICKET (scope-guarded)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches a single ticket.
    /// For HR/OrgAdmin callers, enforces department/org scope.
    /// For Employee callers, enforces ownership.
    /// Returns null if not found; throws UnauthorizedAccessException on scope violation.
    /// </summary>
    //public async Task<SupportTicket?> GetTicketByIdAsync(Guid ticketId, bool isManagementView = false)
    //{
    //    var user = await _currentUser.GetUserAsync();
    //    if (user == null) return null;

    //    await using var db = await _dbFactory.CreateDbContextAsync();

    //    if (isManagementView)
    //    {
    //        var ctx = await GetCallerContextAsync();
    //        if (ctx == null) return null;

    //        await AssertTicketInScopeAsync(db, ctx, ticketId);

    //        return await db.SupportTickets
    //            .Include(t => t.Employee)
    //                .ThenInclude(e => e.Department)
    //            .Include(t => t.AssignedTo)
    //            .Include(t => t.ResolvedBy)   // Make sure this is here
    //            .Include(t => t.ClosedBy)     // Make sure this is here
    //            .AsNoTracking()
    //            .FirstOrDefaultAsync(t => t.TicketId == ticketId);
    //    }
    //    else
    //    {
    //        return await db.SupportTickets
    //            .Include(t => t.Employee)
    //                .ThenInclude(e => e.Department)
    //            .Include(t => t.ResolvedBy)   // Make sure this is here
    //            .Include(t => t.ClosedBy)     // Make sure this is here
    //            .AsNoTracking()
    //            .FirstOrDefaultAsync(t => t.TicketId == ticketId && t.UserId == user.Id);
    //    }
    //}
    public async Task<SupportTicket?> GetTicketByIdAsync(Guid ticketId, bool isManagementView = false)
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();

        SupportTicket? ticket = null;

        if (isManagementView)
        {
            var ctx = await GetCallerContextAsync();
            if (ctx == null) return null;

            await AssertTicketInScopeAsync(db, ctx, ticketId);

            ticket = await db.SupportTickets
                .Include(t => t.Employee)
                    .ThenInclude(e => e.Department)
                .Include(t => t.AssignedTo)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TicketId == ticketId);
        }
        else
        {
            ticket = await db.SupportTickets
                .Include(t => t.Employee)
                    .ThenInclude(e => e.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TicketId == ticketId && t.UserId == user.Id);
        }

        if (ticket != null)
        {
            // Manually load ResolvedBy user
            if (!string.IsNullOrEmpty(ticket.ResolvedByUserId))
            {
                var resolvedByUser = await _userManager.FindByIdAsync(ticket.ResolvedByUserId);
                if (resolvedByUser != null)
                {
                    ticket.ResolvedBy = resolvedByUser;
                }
            }

            // Manually load ClosedBy user
            if (!string.IsNullOrEmpty(ticket.ClosedByUserId))
            {
                var closedByUser = await _userManager.FindByIdAsync(ticket.ClosedByUserId);
                if (closedByUser != null)
                {
                    ticket.ClosedBy = closedByUser;
                }
            }
        }

        return ticket;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // READ — REPLIES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns replies for a ticket.
    /// Pass <paramref name="includeInternalNotes"/> = true for HR/OrgAdmin views only.
    /// Employee views must never see internal notes.
    /// </summary>
    public async Task<List<TicketReplyViewDto>> GetRepliesAsync(
     Guid ticketId,
     bool includeInternalNotes = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.TicketReplies
            .Where(r => r.TicketId == ticketId);

        if (!includeInternalNotes)
            query = query.Where(r => !r.IsInternalNote);

        var replies = await query
            .OrderBy(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        var result = new List<TicketReplyViewDto>();

        foreach (var r in replies)
        {
            var replyUser = await _userManager.FindByIdAsync(r.UserId);
            var roles = replyUser != null
                ? await _userManager.GetRolesAsync(replyUser)
                : new List<string>();

            var isStaff = roles.Contains("HR") || roles.Contains("OrgAdmin");
            var isOrgAdmin = roles.Contains("OrgAdmin");
            var isHR = roles.Contains("HR");

            result.Add(new TicketReplyViewDto
            {
                ReplyId = r.ReplyId,
                UserId = r.UserId,
                AuthorName = replyUser != null
                    ? $"{replyUser.FirstName} {replyUser.LastName}".Trim()
                    : "Unknown",
                IsStaff = isStaff,
                IsOrgAdmin = isOrgAdmin,  // Add this property
                IsHR = isHR,              // Add this property
                IsInternalNote = r.IsInternalNote,
                Message = r.Message,
                CreatedAt = r.CreatedAt
            });
        }

        return result;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // CREATE — EMPLOYEE RAISES A TICKET
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<SupportTicket> CreateTicketAsync(
        string category,
        string description,
        string priority = "Medium",
        string source = "Manual")
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null)
            throw new InvalidOperationException("User not authenticated.");

        if (user.OrganizationId == null)
            throw new InvalidOperationException("User is not associated with an organisation.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.UserId == user.Id);
        var nextTicketNumber =
    (await db.SupportTickets
        .MaxAsync(t => (int?)t.TicketNumber) ?? 1000) + 1;
        var ticket = new SupportTicket
        {
            TicketId = Guid.NewGuid(),

            TicketNumber = nextTicketNumber,

            OrganizationId = user.OrganizationId.Value,
            UserId = user.Id,
            EmployeeId = employee?.EmployeeId,

            Category = category.Trim(),
            Description = description.Trim(),

            Priority = priority,
            Status = "Open",
            Source = source,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        //db.SupportTickets.Add(ticket);
        //await db.SaveChangesAsync();
        db.SupportTickets.Add(ticket);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Ticket creation failed");
            _logger.LogError("INNER ERROR: {Error}",
                ex.InnerException?.Message);

            throw new Exception(
                ex.InnerException?.Message ??
                ex.Message);
        }

        // Notify the department HR (and always the OrgAdmin)
        await _notificationService.CreateOrgAdminNotificationsAsync(
            user.OrganizationId.Value,
            $"New Ticket #{ticket.TicketNumber}: {category}",
            description.Length > 100 ? description[..100] + "…" : description,
            $"/admin/tickets/{ticket.TicketId}");

        return ticket;
    }

  
    // ═══════════════════════════════════════════════════════════════════════
    // REPLY — SCOPED
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a reply.  HR/OrgAdmin replies go through scope validation.
    /// Employee replies validate ticket ownership.
    /// </summary>
    public async Task<SupportTicket> AddReplyAsync(
        Guid ticketId,
        string message,
        bool isInternalNote = false,
        bool isManagementView = false)
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null)
            throw new UnauthorizedAccessException("Not authenticated.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        SupportTicket? ticket;

        if (isManagementView)
        {
            var ctx = await GetCallerContextAsync();
            if (ctx == null) throw new UnauthorizedAccessException("Not authenticated.");

            // Internal notes only for staff
            if (isInternalNote && !ctx.IsHR && !ctx.IsOrgAdmin)
                throw new UnauthorizedAccessException("Only HR/OrgAdmin can add internal notes.");

            await AssertTicketInScopeAsync(db, ctx, ticketId);
            ticket = await db.SupportTickets.FindAsync(ticketId);
        }
        else
        {
            // Employee — must own the ticket
            ticket = await db.SupportTickets
                .FirstOrDefaultAsync(t => t.TicketId == ticketId && t.UserId == user.Id);

            if (ticket == null)
                throw new UnauthorizedAccessException("You do not have access to this ticket.");

            // Employees cannot add internal notes
            isInternalNote = false;
        }

        if (ticket == null)
            throw new KeyNotFoundException("Ticket not found.");

        var reply = new TicketReply
        {
            ReplyId = Guid.NewGuid(),
            TicketId = ticketId,
            UserId = user.Id,
            Message = message.Trim(),
            IsInternalNote = isInternalNote,
            CreatedAt = DateTime.UtcNow
        };

        db.TicketReplies.Add(reply);

        // Auto-reopen if resolved and employee replied
        if (ticket.Status == "Resolved" && !isManagementView)
            ticket.Status = "Open";

        // Auto-move to InProgress when HR first replies
        if (isManagementView && !isInternalNote && ticket.Status == "Open")
            ticket.Status = "InProgress";

        ticket.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // ── Notifications ─────────────────────────────────────────────────
        if (!isInternalNote)
        {
            if (isManagementView)
            {
                // HR replied → notify employee
                if (!string.IsNullOrEmpty(ticket.UserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        ticket.UserId,
                        $"New reply on your Ticket #{ticket.TicketNumber}",
                        message.Length > 100 ? message[..100] + "…" : message,
                        $"/employee/tickets/{ticketId}");
                }
            }
            else
            {
                // Employee replied → notify the assigned HR (or broadcast to OrgAdmin)
                if (!string.IsNullOrEmpty(ticket.AssignedToUserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        ticket.AssignedToUserId,
                        $"Employee reply on Ticket #{ticket.TicketNumber}",
                        message.Length > 100 ? message[..100] + "…" : message,
                        $"/admin/tickets/{ticketId}");
                }
                else
                {
                    await _notificationService.CreateOrgAdminNotificationsAsync(
                        ticket.OrganizationId,
                        $"Employee reply on Ticket #{ticket.TicketNumber}",
                        message.Length > 100 ? message[..100] + "…" : message,
                        $"/admin/tickets/{ticketId}");
                }
            }
        }

        return ticket;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ASSIGN TICKET (OrgAdmin only)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task AssignTicketAsync(Guid ticketId, string assignToUserId)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null) throw new UnauthorizedAccessException();

        // Only OrgAdmin can assign tickets
        if (!ctx.IsOrgAdmin)
            throw new UnauthorizedAccessException("Only OrgAdmin can assign tickets.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        await AssertTicketInScopeAsync(db, ctx, ticketId);

        var ticket = await db.SupportTickets.FindAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found.");

        // Validate the assignee belongs to the same org and is HR
        var assignee = await _userManager.FindByIdAsync(assignToUserId);
        if (assignee == null || assignee.OrganizationId != ctx.OrganizationId)
            throw new InvalidOperationException("Invalid assignee.");

        var assigneeRoles = await _userManager.GetRolesAsync(assignee);
        if (!assigneeRoles.Contains("HR") && !assigneeRoles.Contains("OrgAdmin"))
            throw new InvalidOperationException("Tickets can only be assigned to HR or OrgAdmin users.");

        var assigneeEmployee = await db.Employees
            .FirstOrDefaultAsync(e => e.UserId == assignToUserId);

        ticket.AssignedToUserId = assignToUserId;
        ticket.AssignedToEmployeeId = assigneeEmployee?.EmployeeId;
        ticket.UpdatedAt = DateTime.UtcNow;

        if (ticket.Status == "Open")
            ticket.Status = "InProgress";

        await db.SaveChangesAsync();

        // Notify the newly assigned HR
        await _notificationService.CreateNotificationAsync(
            assignToUserId,
            $"Ticket #{ticket.TicketNumber} assigned to you",
            ticket.Description.Length > 100 ? ticket.Description[..100] + "…" : ticket.Description,
            $"/admin/tickets/{ticketId}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RESOLVE (scope-guarded)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<SupportTicket> ResolveTicketAsync(Guid ticketId, string resolution)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || (!ctx.IsHR && !ctx.IsOrgAdmin))
            throw new UnauthorizedAccessException("Only HR or OrgAdmin can resolve tickets.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        await AssertTicketInScopeAsync(db, ctx, ticketId);

        var ticket = await db.SupportTickets.FindAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found.");

        if (ticket.Status == "Resolved")
            throw new InvalidOperationException("Ticket is already resolved.");

        ticket.Status = "Resolved";
        ticket.ResolvedAt = DateTime.UtcNow;
        ticket.Resolution = resolution.Trim();
        ticket.UpdatedAt = DateTime.UtcNow;
        ticket.ResolvedByUserId = ctx.UserId;

        await db.SaveChangesAsync();

        // Reload the ticket and manually load the ResolvedBy user
        var updatedTicket = await db.SupportTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);

        if (updatedTicket != null && !string.IsNullOrEmpty(updatedTicket.ResolvedByUserId))
        {
            var resolvedByUser = await _userManager.FindByIdAsync(updatedTicket.ResolvedByUserId);
            if (resolvedByUser != null)
            {
                updatedTicket.ResolvedBy = resolvedByUser;
            }
        }

        if (!string.IsNullOrEmpty(updatedTicket?.UserId))
        {
            var resolverName = await GetUserFullNameAsync(ctx.UserId);
            await _notificationService.CreateNotificationAsync(
                updatedTicket.UserId,
                $"Your Ticket #{updatedTicket.TicketNumber} has been resolved",
                $"Resolved by: {resolverName}\nResolution: {(resolution.Length > 150 ? resolution[..150] + "…" : resolution)}",
                $"/employee/tickets/{ticketId}");
        }

        return updatedTicket!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // REOPEN (scope-guarded)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<SupportTicket> ReopenTicketAsync(Guid ticketId, string reason)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || (!ctx.IsHR && !ctx.IsOrgAdmin))
            throw new UnauthorizedAccessException("Only HR or OrgAdmin can reopen tickets.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        await AssertTicketInScopeAsync(db, ctx, ticketId);

        var ticket = await db.SupportTickets.FindAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found.");

        ticket.Status = "Open";
        ticket.ResolvedAt = null;
        ticket.Resolution = null;
        ticket.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(ticket.UserId))
        {
            await _notificationService.CreateNotificationAsync(
                ticket.UserId,
                $"Your Ticket #{ticket.TicketNumber} has been reopened",
                reason,
                $"/employee/tickets/{ticketId}");
        }

        return ticket;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CLOSE (scope-guarded; different from Resolved — ticket is archived)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<SupportTicket> CloseTicketAsync(Guid ticketId)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || !ctx.IsOrgAdmin)
            throw new UnauthorizedAccessException("Only OrgAdmin can permanently close tickets.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        await AssertTicketInScopeAsync(db, ctx, ticketId);

        var ticket = await db.SupportTickets.FindAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found.");

        ticket.Status = "Closed";
        ticket.ClosedAt = DateTime.UtcNow;
        ticket.UpdatedAt = DateTime.UtcNow;
        ticket.ClosedByUserId = ctx.UserId;

        await db.SaveChangesAsync();

        // Reload the ticket and manually load the ClosedBy user
        var updatedTicket = await db.SupportTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);

        if (updatedTicket != null && !string.IsNullOrEmpty(updatedTicket.ClosedByUserId))
        {
            var closedByUser = await _userManager.FindByIdAsync(updatedTicket.ClosedByUserId);
            if (closedByUser != null)
            {
                updatedTicket.ClosedBy = closedByUser;
            }
        }

        return updatedTicket!;
    }

    private async Task<string> GetUserFullNameAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return "Unknown User";
        return $"{user.FirstName} {user.LastName}".Trim();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DASHBOARD STATS — CORRECTLY SCOPED
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<DashboardStatsDto> GetTicketDashboardStatsAsync()
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || ctx.OrganizationId == Guid.Empty)
            return new DashboardStatsDto();

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Use the same scope query so HR only sees their dept's stats
        var baseQuery = ScopedTicketQuery(db, ctx);
        var now = DateTime.UtcNow;

        var stats = new DashboardStatsDto
        {
            OpenTickets = await baseQuery.CountAsync(t => t.Status == "Open"),
            InProgressTickets = await baseQuery.CountAsync(t => t.Status == "InProgress"),
            ResolvedTickets = await baseQuery.CountAsync(t => t.Status == "Resolved"),
            TotalTicketsThisMonth = await baseQuery.CountAsync(t =>
                t.CreatedAt.Month == now.Month && t.CreatedAt.Year == now.Year),
            UnassignedTickets = await baseQuery.CountAsync(t =>
                t.AssignedToUserId == null && t.Status != "Resolved" && t.Status != "Closed"),
            AverageResolutionHours = await CalculateAverageResolutionHoursAsync(baseQuery)
        };

        // Only calculate HR/Employee breakdown for OrgAdmin


        return stats;
    }

    /// <summary>
    /// Determines if the ticket creator is an HR user
    /// </summary>
    private async Task<bool> IsTicketCreatorHRAsync(SupportTicket ticket)
    {
        if (string.IsNullOrEmpty(ticket.UserId))
            return false;

        var user = await _userManager.FindByIdAsync(ticket.UserId);
        if (user == null)
            return false;

        var roles = await _userManager.GetRolesAsync(user);
        return roles.Contains("HR");
    }

    private static async Task<double> CalculateAverageResolutionHoursAsync(
        IQueryable<SupportTicket> scopedQuery)
    {
        var resolved = await scopedQuery
            .Where(t => t.Status == "Resolved" && t.ResolvedAt.HasValue)
            .Select(t => new { t.CreatedAt, ResolvedAt = t.ResolvedAt!.Value })
            .ToListAsync();

        if (!resolved.Any()) return 0;

        var totalHours = resolved.Sum(t => (t.ResolvedAt - t.CreatedAt).TotalHours);
        return Math.Round(totalHours / resolved.Count, 1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER: list of HR users in scope (for assignment dropdown)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<HrStaffDto>> GetAssignableHRUsersAsync(Guid? departmentId = null)
    {
        var ctx = await GetCallerContextAsync();
        if (ctx == null || !ctx.IsOrgAdmin)
            return new List<HrStaffDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get HR role
        var hrRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "HR");
        if (hrRole == null) return new List<HrStaffDto>();

        // Get all HR user IDs
        var hrUserIds = await db.UserRoles
            .Where(ur => ur.RoleId == hrRole.Id)
            .Select(ur => ur.UserId)
            .ToListAsync();

        // Build query for HR users
        var query = db.Users
            .Where(u => hrUserIds.Contains(u.Id)
                        && u.OrganizationId == ctx.OrganizationId
                        && u.IsActive);  // Only active users

        // If department filter is provided, filter HR by their department
        if (departmentId.HasValue)
        {
            var hrEmployeeIds = await db.Employees
                .Where(e => e.DepartmentId == departmentId.Value
                            && e.Status == "Active"  // Only active employees
                            && e.OrganizationId == ctx.OrganizationId)
                .Select(e => e.UserId)
                .ToListAsync();

            query = query.Where(u => hrEmployeeIds.Contains(u.Id));
        }

        var hrUsers = await query.ToListAsync();

        var result = new List<HrStaffDto>();

        foreach (var u in hrUsers)
        {
            var emp = await db.Employees
                .Include(e => e.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == u.Id && e.Status == "Active");  // Only active employees

            if (emp != null)  // Only add if employee record exists and is active
            {
                result.Add(new HrStaffDto
                {
                    UserId = u.Id,
                    FullName = $"{u.FirstName} {u.LastName}".Trim(),
                    DepartmentName = emp.Department?.Name ?? "—"
                });
            }
        }

        return result;
    }
    // ═══════════════════════════════════════════════════════════════════════════
    // Supporting types
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Internal context object — never leaves the service.</summary>
    internal sealed class CallerContext
    {
        public string UserId { get; init; } = string.Empty;
        public Guid OrganizationId { get; init; }
        public bool IsOrgAdmin { get; init; }
        public bool IsHR { get; init; }
        public Employee? Employee { get; init; }
    }

    public class DashboardStatsDto
    {
        public int OpenTickets { get; set; }
        public int InProgressTickets { get; set; }
        public int ResolvedTickets { get; set; }
        public int TotalTicketsThisMonth { get; set; }
        public int UnassignedTickets { get; set; }
        public double AverageResolutionHours { get; set; }
        public int HRTickets { get; set; }
        public int EmployeeTickets { get; set; }
    }

    public class TicketReplyViewDto
    {
        public Guid ReplyId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public bool IsStaff { get; set; }
        public bool IsOrgAdmin { get; set; }  // ← ADD THIS
        public bool IsHR { get; set; }
        public bool IsInternalNote { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class HrStaffDto
    {   
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
    }
}