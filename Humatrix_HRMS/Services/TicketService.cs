// Services/AI/TicketService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.AI;

public class TicketService
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notificationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public TicketService(
        ApplicationDbContext db,
        CurrentUserService currentUser,
        NotificationService notificationService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _notificationService = notificationService;
        _userManager = userManager;
    }

    public async Task<List<SupportTicket>> GetTicketsForHRAsync(string? status = null)
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null) return new List<SupportTicket>();

        var roles = await _userManager.GetRolesAsync(user);
        var isOrgAdmin = roles.Contains("OrgAdmin");
        var isHR = roles.Contains("HR");

        var query = _db.SupportTickets
            .Include(t => t.Employee)
            .Where(t => t.OrganizationId == user.OrganizationId);

        if (isHR && !isOrgAdmin)
        {
            // HR sees only tickets from their department
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee != null)
            {
                query = query.Where(t => t.Employee != null && t.Employee.DepartmentId == employee.DepartmentId);
            }
        }

        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }

    public async Task<List<SupportTicket>> GetMyTicketsAsync()
    {
        var user = await _currentUser.GetUserAsync();
        if (user == null) return new List<SupportTicket>();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);

        return await _db.SupportTickets
            .Where(t => t.UserId == user.Id || (employee != null && t.EmployeeId == employee.EmployeeId))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<SupportTicket?> GetTicketByIdAsync(Guid ticketId)
    {
        return await _db.SupportTickets
            .Include(t => t.Employee)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);
    }

    public async Task<List<TicketReplyViewDto>> GetRepliesAsync(Guid ticketId)
    {
        var replies = await _db.TicketReplies
            .Where(r => r.TicketId == ticketId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var result = new List<TicketReplyViewDto>();

        foreach (var r in replies)
        {
            var user = await _userManager.FindByIdAsync(r.UserId);
            var roles = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();

            result.Add(new TicketReplyViewDto
            {
                ReplyId = r.ReplyId,
                UserId = r.UserId,
                AuthorName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown",
                IsStaff = roles.Contains("HR") || roles.Contains("OrgAdmin"),
                Message = r.Message,
                CreatedAt = r.CreatedAt
            });
        }

        return result;
    }

    public async Task<SupportTicket> AddReplyAsync(Guid ticketId, string message, string userId)
    {
        var ticket = await _db.SupportTickets.FindAsync(ticketId);
        if (ticket == null)
            throw new Exception("Ticket not found");

        var reply = new TicketReply
        {
            ReplyId = Guid.NewGuid(),
            TicketId = ticketId,
            UserId = userId,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        _db.TicketReplies.Add(reply);

        // Update ticket status if it was resolved and now being reopened
        if (ticket.Status == "Resolved")
            ticket.Status = "Open";

        await _db.SaveChangesAsync();

        // Notify the other party
        var isHRorAdmin = await IsUserHRorAdmin(userId, ticket.OrganizationId);

        if (isHRorAdmin && ticket.UserId != null)
        {
            await _notificationService.CreateNotificationAsync(
                ticket.UserId,
                $"New reply on Ticket #{ticket.TicketNumber}",
                message.Length > 100 ? message.Substring(0, 100) + "..." : message,
                $"/employee/tickets/{ticketId}");
        }
        else if (ticket.UserId != userId)
        {
            // Notify HR/Admin
            await _notificationService.CreateOrgAdminNotificationsAsync(
                ticket.OrganizationId,
                $"New reply on Ticket #{ticket.TicketNumber}",
                message.Length > 100 ? message.Substring(0, 100) + "..." : message,
                $"/admin/tickets/{ticketId}");
        }

        return ticket;
    }

    public async Task<SupportTicket> ResolveTicketAsync(Guid ticketId, string resolution)
    {
        var ticket = await _db.SupportTickets.FindAsync(ticketId);
        if (ticket == null)
            throw new Exception("Ticket not found");

        ticket.Status = "Resolved";
        ticket.ResolvedAt = DateTime.UtcNow;
        ticket.Resolution = resolution;

        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(ticket.UserId))
        {
            await _notificationService.CreateNotificationAsync(
                ticket.UserId,
                $"Ticket #{ticket.TicketNumber} Resolved",
                $"Your ticket has been resolved.\n\nResolution: {resolution}",
                $"/employee/tickets/{ticketId}");
        }

        return ticket;
    }

    public async Task<SupportTicket> ReopenTicketAsync(Guid ticketId, string reason)
    {
        var ticket = await _db.SupportTickets.FindAsync(ticketId);
        if (ticket == null)
            throw new Exception("Ticket not found");

        ticket.Status = "Open";
        ticket.ResolvedAt = null;
        ticket.Resolution = null;

        await _db.SaveChangesAsync();

        await _notificationService.CreateOrgAdminNotificationsAsync(
            ticket.OrganizationId,
            $"Ticket #{ticket.TicketNumber} Reopened",
            reason,
            $"/admin/tickets/{ticketId}");

        return ticket;
    }

    public async Task<DashboardStatsDto> GetTicketDashboardStatsAsync(Guid organizationId)
    {
        var query = _db.SupportTickets.Where(t => t.OrganizationId == organizationId);

        return new DashboardStatsDto
        {
            OpenTickets = await query.CountAsync(t => t.Status == "Open"),
            InProgressTickets = await query.CountAsync(t => t.Status == "InProgress"),
            ResolvedTickets = await query.CountAsync(t => t.Status == "Resolved"),
            TotalTicketsThisMonth = await query.CountAsync(t => t.CreatedAt.Month == DateTime.UtcNow.Month),
            AverageResolutionHours = await CalculateAverageResolutionHoursAsync(organizationId)
        };
    }

    private async Task<double> CalculateAverageResolutionHoursAsync(Guid organizationId)
    {
        var resolvedTickets = await _db.SupportTickets
            .Where(t => t.OrganizationId == organizationId && t.Status == "Resolved" && t.ResolvedAt.HasValue)
            .ToListAsync();

        if (!resolvedTickets.Any()) return 0;

        var totalHours = resolvedTickets.Sum(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours);
        return Math.Round(totalHours / resolvedTickets.Count, 1);
    }

    private async Task<bool> IsUserHRorAdmin(string userId, Guid organizationId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var roles = await _userManager.GetRolesAsync(user);
        return roles.Contains("HR") || roles.Contains("OrgAdmin");
    }
}

public class DashboardStatsDto
{
    public int OpenTickets { get; set; }
    public int InProgressTickets { get; set; }
    public int ResolvedTickets { get; set; }
    public int TotalTicketsThisMonth { get; set; }
    public double AverageResolutionHours { get; set; }
}

public class TicketReplyViewDto
{
    public Guid ReplyId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public bool IsStaff { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}