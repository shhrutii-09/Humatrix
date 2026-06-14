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

        
        private string GetAssignedByName(Guid assignedBy)
        {
            if (assignedBy == Guid.Empty) return "System Admin";

            string idString = assignedBy.ToString();

            var employee = _context.Employees.FirstOrDefault(e => e.UserId == idString);
            if (employee != null)
            {
                return !string.IsNullOrWhiteSpace(employee.FirstName)
                    ? $"{employee.FirstName} {employee.LastName}"
                    : "HR User";
            }

            var user = _userManager.Users.FirstOrDefault(u => u.Id == idString);

            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(user.FirstName))
                    return $"{user.FirstName} {user.LastName}";
                else if (!string.IsNullOrWhiteSpace(user.UserName))
                    return user.UserName;
                else
                    return "Org Admin";
            }

            return "Admin";
        }
        public async Task AssignTaskAsync(CreateTaskDto dto)
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.EmployeeId == dto.AssignedTo && e.Status == "Active");

            if (employee == null) throw new Exception("Active employee not found");

            Guid assignedByGuid = Guid.TryParse(user.Id, out var uid) ? uid : Guid.Empty;

            var task = new TaskItem
            {
                TaskId = Guid.NewGuid(),
                Title = dto.Title ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                Priority = dto.Priority ?? "Medium",
                DueDate = dto.DueDate,
                AssignedTo = (Guid)dto.AssignedTo,
                AssignedBy = assignedByGuid,
                OrganizationId = employee.OrganizationId,
                Status = "Pending",
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

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

        public async Task<List<TaskDto>> GetMyTasksAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) throw new Exception("Unauthorized");

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (employee == null) throw new Exception("Employee not found");

            var tasks = await _context.Tasks
                .Where(t => t.AssignedTo == employee.EmployeeId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return tasks.Select(t => new TaskDto
            {
                TaskId = t.TaskId,
                Title = t.Title ?? "",
                Description = t.Description ?? "",
                Priority = t.Priority ?? "Medium",
                Status = t.Status ?? "Pending",
                Progress = t.Progress,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                AssignedToName = "",
                AssignedByName = GetAssignedByName(t.AssignedBy)
            }).ToList();
        }

        public async Task<List<TaskDto>> GetAllTasksAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) throw new Exception("Unauthorized");

            var tasks = await _context.Tasks
                .Include(t => t.AssignedToEmployee)
                .Where(t => t.OrganizationId == user.OrganizationId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return tasks.Select(t => new TaskDto
            {
                TaskId = t.TaskId,
                Title = t.Title ?? "",
                Description = t.Description ?? "",
                Priority = t.Priority ?? "Medium",
                Status = t.Status ?? "Pending",
                Progress = t.Progress,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                AssignedToName = t.AssignedToEmployee != null ? $"{t.AssignedToEmployee.FirstName} {t.AssignedToEmployee.LastName}" : "",
                AssignedByName = GetAssignedByName(t.AssignedBy)
            }).ToList();
        }

        public async Task<List<TaskDto>> GetTasksAssignedByMeAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) throw new Exception("Unauthorized");

            var isHr = await _userManager.IsInRoleAsync(user, "HR");
            var isOrgAdmin = await _userManager.IsInRoleAsync(user, "OrgAdmin");

            var query = _context.Tasks
                .Include(t => t.AssignedToEmployee)
                .ThenInclude(e => e.Department)
                .AsQueryable();

            if (isOrgAdmin)
            {
                // OrgAdmin: Sirf HR role wale users ko assign kiye gaye tasks dikhein
                query = query.Where(t => _context.UserRoles
                    .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .Where(x => x.r.Name == "HR" && x.ur.UserId == t.AssignedToEmployee.UserId)
                    .Any());
            }
            else if (isHr)
            {
                // HR: Sirf Employees (Jo HR nahi hain) aur apne department ke
                var hrProfile = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
                if (hrProfile != null)
                {
                    query = query.Where(t => t.AssignedToEmployee.DepartmentId == hrProfile.DepartmentId
                                          && t.AssignedToEmployee.Status == "Active"
                                          && !_context.UserRoles
                                              .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                                              .Where(x => x.r.Name == "HR" && x.ur.UserId == t.AssignedToEmployee.UserId)
                                              .Any());
                }
            }

            var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

            return tasks.Select(t => new TaskDto
            {
                // ... (Baaki ki mapping waisi hi rahegi)
                TaskId = t.TaskId,
                Title = t.Title ?? "",
                Description = t.Description ?? "",
                Priority = t.Priority ?? "Medium",
                Status = t.Status ?? "Pending",
                Progress = t.Progress,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                AssignedToName = t.AssignedToEmployee != null ? $"{t.AssignedToEmployee.FirstName} {t.AssignedToEmployee.LastName}" : "",
                DepartmentName = t.AssignedToEmployee?.Department?.Name ?? "N/A",
                AssignedByName = GetAssignedByName(t.AssignedBy)
            }).ToList();
        }

        public async Task UpdateTaskAsync(UpdateTaskDto dto)
        {
            var task = await _context.Tasks.Include(t => t.AssignedToEmployee).FirstOrDefaultAsync(t => t.TaskId == dto.TaskId);
            if (task == null) throw new Exception("Task not found");

            task.Progress = dto.Progress;
            task.Status = dto.Progress >= 100 ? "Completed" : dto.Progress > 0 ? "In Progress" : "Pending";
            await _context.SaveChangesAsync();

            if (task.Progress >= 100)
            {
                var managers = await _context.Users.Where(u => u.OrganizationId == task.OrganizationId).ToListAsync();
                foreach (var m in managers)
                {
                    if (await _userManager.IsInRoleAsync(m, "HR") || await _userManager.IsInRoleAsync(m, "OrgAdmin"))
                    {
                        await _notificationService.CreateNotificationAsync(m.Id, "Task Completed", $"{task.AssignedToEmployee?.FirstName} completed task '{task.Title}'", "/hr/tasks");
                    }
                }
            }
        }

        public async Task MarkCompleteAsync(Guid taskId)
        {
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.TaskId == taskId);
            if (task == null) throw new Exception("Task not found");
            task.Status = "Completed";
            task.Progress = 100;
            await _context.SaveChangesAsync();
        }

        public async Task CreateTaskReminderNotificationsAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) return;

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (employee == null) return;

            var today = DateTime.Today.Date;
            var tomorrow = today.AddDays(1).Date;

            var tasks = await _context.Tasks
                .Where(t => t.AssignedTo == employee.EmployeeId && t.Status != "Completed")
                .ToListAsync();

            foreach (var task in tasks)
            {
                string? title = null;
                string? message = null;

                if (task.DueDate.HasValue && task.DueDate.Value.Date == today)
                {
                    title = "Task Due Today";
                    message = $"Task '{task.Title}' is due today";
                }
                else if (task.DueDate.HasValue && task.DueDate.Value.Date == tomorrow)
                {
                    title = "Task Due Tomorrow";
                    message = $"Task '{task.Title}' is due tomorrow";
                }
                else if (task.DueDate.HasValue && task.DueDate.Value.Date < today)
                {
                    title = "Task Overdue";
                    message = $"Task '{task.Title}' is overdue";
                }

                if (title != null)
                {
                    bool exists = await _context.Notifications.AnyAsync(n =>
                        n.UserId == user.Id && n.Title == title && n.CreatedAt.Date == today);

                    if (!exists)
                    {
                        await _notificationService.CreateNotificationAsync(user.Id, title, message, "/employee/my-tasks");
                    }
                }
            }
        }
    }
}