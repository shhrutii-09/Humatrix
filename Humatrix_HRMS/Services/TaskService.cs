using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class TaskService
    {
        private readonly ApplicationDbContext _context;

        private readonly CurrentUserService _currentUser;

        private readonly NotificationService _notificationService;
        private readonly UserManager<ApplicationUser> _userManager;

        public TaskService(
         ApplicationDbContext context,
         CurrentUserService currentUser,
         NotificationService notificationService,
         UserManager<ApplicationUser> userManager)
        {
            _context = context;

            _currentUser = currentUser;

            _notificationService = notificationService;

            _userManager = userManager;
        }

        // =========================
        // ASSIGN TASK (HR/Admin)
        // =========================
        public async Task AssignTaskAsync(CreateTaskDto dto)
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null)
                throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == dto.AssignedTo);

            if (employee == null)
                throw new Exception("Employee not found");

            var task = new TaskItem
            {
                TaskId = Guid.NewGuid(),

                Title = dto.Title ?? string.Empty,

                Description = dto.Description ?? string.Empty,

                Priority = dto.Priority ?? "Medium",

                DueDate = dto.DueDate,

                AssignedTo = (Guid)dto.AssignedTo,

                AssignedBy = Guid.TryParse(user.Id, out var uid)
                    ? uid
                    : Guid.Empty,

                OrganizationId = employee.OrganizationId,

                Status = "Pending",

                Progress = 0,

                CreatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);

            await _context.SaveChangesAsync();

            // =========================
            // CREATE NOTIFICATION
            // =========================

            if (!string.IsNullOrEmpty(employee.UserId))
            {
                await _notificationService.CreateNotificationAsync(
                    employee.UserId,

                    "New Task Assigned",

                    $"Task '{task.Title}' assigned to you",

                    "/employee/my-tasks"
                );
            }
        }

        // =========================
        // TASK REMINDER NOTIFICATIONS
        // =========================
        public async Task CreateTaskReminderNotificationsAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null)
                return;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                return;

            var today = DateTime.Today.Date;

            var tomorrow = today.AddDays(1).Date;

            var tasks = await _context.Tasks
                .Where(t =>
                    t.AssignedTo == employee.EmployeeId &&
                    t.Status != "Completed")
                .ToListAsync();

            foreach (var task in tasks)
            {
                string? title = null;

                string? message = null;

                // =========================
                // DUE TODAY
                // =========================
                if (task.DueDate.HasValue &&
                         task.DueDate.Value.Date == today)
                {
                    title = "Task Due Today";

                    message = $"Task '{task.Title}' is due today";
                }

                // =========================
                // DUE TOMORROW
                // =========================
                else if (task.DueDate.HasValue &&
                       task.DueDate.Value.Date == tomorrow)
                {
                    title = "Task Due Tomorrow";

                    message = $"Task '{task.Title}' is due tomorrow";
                }

                // =========================
                // OVERDUE
                // =========================
                else if (task.DueDate.HasValue &&
                     task.DueDate.Value.Date < today)
                {
                    title = "Task Overdue";

                    message = $"Task '{task.Title}' is overdue";
                }

                if (title != null)
                {
                    // =========================
                    // AVOID DUPLICATE NOTIFICATIONS
                    // =========================
                    bool exists = await _context.Notifications.AnyAsync(n =>
                        n.UserId == user.Id &&
                        n.Title == title &&
                        n.Message == message &&
                        n.CreatedAt.Date == today);

                    if (!exists)
                    {
                        await _notificationService.CreateNotificationAsync(
                            user.Id,

                            title,

                            message,

                            "/employee/my-tasks"
                        );
                    }
                }
            }
        }

        // =========================
        // MY TASKS (Employee)
        // =========================
        public async Task<List<TaskDto>> GetMyTasksAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null)
                throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                throw new Exception("Employee not found");

            return await _context.Tasks

                .Where(t => t.AssignedTo == employee.EmployeeId)

                .OrderByDescending(t => t.CreatedAt)

                .Select(t => new TaskDto
                {
                    TaskId = t.TaskId,

                    Title = t.Title ?? "",

                    Description = t.Description ?? "",

                    Priority = t.Priority ?? "Medium",

                    Status = t.Status ?? "Pending",

                    Progress = t.Progress,

                    DueDate = t.DueDate,

                    CreatedAt = t.CreatedAt,

                    AssignedToName = ""
                })

                .ToListAsync();
        }

        // =========================
        // ALL TASKS (HR/Admin)
        // =========================
        public async Task<List<TaskDto>> GetAllTasksAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null)
                throw new Exception("Unauthorized");

            return await _context.Tasks

                .Include(t => t.AssignedToEmployee)

                .Where(t => t.OrganizationId == user.OrganizationId)

                .OrderByDescending(t => t.CreatedAt)

                .Select(t => new TaskDto
                {
                    TaskId = t.TaskId,

                    Title = t.Title ?? "",

                    Description = t.Description ?? "",

                    Priority = t.Priority ?? "Medium",

                    Status = t.Status ?? "Pending",

                    Progress = t.Progress,

                    DueDate = t.DueDate,

                    CreatedAt = t.CreatedAt,

                    AssignedToName = t.AssignedToEmployee != null
                        ? t.AssignedToEmployee.FirstName + " "
                          + t.AssignedToEmployee.LastName
                        : ""
                })

                .ToListAsync();
        }

        // =========================
        // UPDATE PROGRESS (Employee)
        // =========================
        public async Task UpdateTaskAsync(UpdateTaskDto dto)
        {
            var task = await _context.Tasks
                .Include(t => t.AssignedToEmployee)
                .FirstOrDefaultAsync(t => t.TaskId == dto.TaskId);

            if (task == null)
                throw new Exception("Task not found");

            task.Progress = dto.Progress;

            if (dto.Progress >= 100)
                task.Status = "Completed";

            else if (dto.Progress > 0)
                task.Status = "In Progress";

            else
                task.Status = "Pending";

            await _context.SaveChangesAsync();

            // =========================
            // NOTIFY HR WHEN COMPLETED
            // =========================

            if (task.Progress >= 100)
            {
                var allUsers = await _context.Users
    .Where(u => u.OrganizationId == task.OrganizationId)
    .ToListAsync();

                var hrUsers = new List<ApplicationUser>();

                foreach (var user in allUsers)
                {
                    if (await _userManager.IsInRoleAsync(user, "HR") ||
                        await _userManager.IsInRoleAsync(user, "OrgAdmin"))
                    {
                        hrUsers.Add(user);
                    }
                }

                foreach (var hr in hrUsers)
                {
                    await _notificationService.CreateNotificationAsync(
                        hr.Id,

                        "Task Completed",

                        $"{task.AssignedToEmployee?.FirstName} completed task '{task.Title}'",

                        "/hr/tasks"
                    );
                }
            }
        }

        // =========================
        // MARK COMPLETE
        // =========================
        public async Task MarkCompleteAsync(Guid taskId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (task == null)
                throw new Exception("Task not found");

            task.Status = "Completed";

            task.Progress = 100;

            await _context.SaveChangesAsync();
        }
    }
}