using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOsA;
using Humatrix_HRMS.DTOsA.Auth;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Humatrix_HRMS.Services
{
    public class EmployeeService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly IConfiguration _config;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly EmailService _emailService;

        public EmployeeService(
     UserManager<ApplicationUser> userManager,
     CurrentUserService currentUser,
     IDbContextFactory<ApplicationDbContext> contextFactory,
     IConfiguration config,
     EmailService emailService)
        {
            _userManager = userManager;
            _currentUser = currentUser;
            _contextFactory = contextFactory;
            _config = config;
            _emailService = emailService;
        }

        #region Dashboard Data

        public async Task<EmployeeDashboardDto?> GetEmployeeDashboardDataAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) return null;

            var userId = currentUser.Id;

            var dashboardData = await (
                from e in context.Employees
                where e.UserId == userId

                join d in context.Departments on e.DepartmentId equals d.DepartmentId into dept
                from d in dept.DefaultIfEmpty()

                join des in context.Designations on e.DesignationId equals des.DesignationId into desg
                from des in desg.DefaultIfEmpty()

                join s in context.Shifts on e.ShiftId equals s.ShiftId into sh
                from s in sh.DefaultIfEmpty()

                select new EmployeeDashboardDto
                {
                    FullName = currentUser.FirstName + " " + currentUser.LastName,
                    Email = currentUser.Email ?? "",
                    EmployeeCode = e.EmployeeCode ?? "PENDING",
                    JoiningDate = e.JoiningDate,
                    Status = e.Status ?? "Active",
                    DepartmentName = d != null ? d.Name : "N/A",
                    DesignationName = des != null ? des.Name : "N/A",
                    ShiftName = s != null ? s.Name : "No Shift Assigned"
                }
            ).AsNoTracking().FirstOrDefaultAsync();

            return dashboardData;
        }

        #endregion

        #region Employee Management

        public async Task<EmployeeListDto?> GetEmployeeByEmailAsync(string email)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return null;

            var currentUser = await _currentUser.GetUserAsync();

            var profile = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var roles = await _userManager.GetRolesAsync(user);

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d =>
                    d.DepartmentId == user.DepartmentId &&
                    d.OrganizationId == currentUser.OrganizationId);

            var desig = await _context.Designations
                .FirstOrDefaultAsync(d =>
                    d.DesignationId == user.DesignationId &&
                    d.OrganizationId == currentUser.OrganizationId);

            var shiftId = profile?.ShiftId;

            var shift = shiftId == null
                ? null
                : await _context.Shifts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ShiftId == shiftId);

            return new EmployeeListDto
            {
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Name = $"{user.FirstName} {user.LastName}",
                Role = roles.FirstOrDefault() ?? "Employee",
                Department = dept?.Name ?? "N/A",
                Designation = desig?.Name ?? "N/A",
                DepartmentId = user.DepartmentId,
                DesignationId = user.DesignationId,
                ShiftId = profile?.ShiftId,
                ShiftName = shift?.Name ?? "No Shift",
                IsActive = user.IsActive
            };
        }

        public async Task<EmployeeProfileDto?> GetMyProfileAsync()
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null)
                return null;

            var employee = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Designation)
                .Include(e => e.Shift)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            if (employee == null)
                return null;

            return new EmployeeProfileDto
            {
                EmployeeId = employee.EmployeeId,
                FullName = $"{employee.FirstName} {employee.LastName}",
                Email = currentUser.Email ?? "",
                EmployeeCode = employee.EmployeeCode,
                Department = employee.Department?.Name ?? "N/A",
                Designation = employee.Designation?.Name ?? "N/A",
                Shift = employee.Shift?.Name ?? "No Shift",
                Status = employee.Status,
                JoiningDate = employee.JoiningDate,
                Phone = employee.Phone,
                Address = employee.Address
            };
        }

        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            if (dto.DepartmentId == null)
                throw new Exception("Department required");

            if (dto.DesignationId == null)
                throw new Exception("Designation required");

            var currentUser = await _currentUser.GetUserAsync();

            var validCombo = await _context.Designations.AnyAsync(d =>
                d.DesignationId == dto.DesignationId &&
                d.DepartmentId == dto.DepartmentId &&
                d.OrganizationId == currentUser.OrganizationId);

            if (!validCombo)
                throw new Exception("Designation does not belong to selected department");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId;

            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (profile != null)
            {
                profile.FirstName = dto.FirstName;
                profile.LastName = dto.LastName;
                profile.DepartmentId = dto.DepartmentId ?? Guid.Empty;
                profile.DesignationId = dto.DesignationId ?? Guid.Empty;
                profile.ShiftId = dto.ShiftId;

                await _context.SaveChangesAsync();
            }
        }

        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (profile != null)
            {
                profile.Status = isActive ? "Active" : "Inactive";
                await _context.SaveChangesAsync();
            }
        }

        #endregion

        #region List Retrieval

        public async Task<(List<EmployeeListDto> Items, int TotalCount)> GetEmployeesForListAsync(
            string? search = null,
            Guid? departmentId = null,
            int pageNumber = 1,
            int pageSize = 10)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser?.OrganizationId == null) return (new List<EmployeeListDto>(), 0);

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            bool isOrgAdmin = currentUserRoles.Contains("OrgAdmin");
            bool isHR = currentUserRoles.Contains("HR");

            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId && u.Id != currentUser.Id);

            if (isHR && !isOrgAdmin)
            {
                usersQuery = usersQuery.Where(u => u.DepartmentId == currentUser.DepartmentId);
            }

            var users = await usersQuery.ToListAsync();

            var depts = await _context.Departments
                .Where(d => d.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var desigs = await _context.Designations
                .Where(d => d.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var shifts = await _context.Shifts
                .Where(s => s.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var profiles = await _context.Employees
                .Where(e => e.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var list = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
                var role = userRoles.FirstOrDefault() ?? "Employee";

                if (isHR && !isOrgAdmin && (role == "HR" || role == "OrgAdmin"))
                    continue;

                var profile = profiles.FirstOrDefault(p => p.UserId == u.Id);

                list.Add(new EmployeeListDto
                {
                    EmployeeId = profile?.EmployeeId ?? Guid.Empty,
                    Email = u.Email ?? string.Empty,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Name = $"{u.FirstName} {u.LastName}",
                    Role = role,
                    Department = depts.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "N/A",
                    Designation = desigs.FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "N/A",
                    DepartmentId = u.DepartmentId,
                    DesignationId = u.DesignationId,
                    ShiftId = profile?.ShiftId,
                    ShiftName = shifts.FirstOrDefault(s => s.ShiftId == profile?.ShiftId)?.Name ?? "No Shift Assigned",
                    IsActive = u.IsActive
                });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                list = list.Where(x => x.Name.ToLower().Contains(search) || x.Email.ToLower().Contains(search)).ToList();
            }

            if (departmentId.HasValue)
            {
                list = list.Where(x => x.DepartmentId == departmentId).ToList();
            }

            int totalCount = list.Count;
            var items = list
                .OrderBy(x => x.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return (items, totalCount);
        }

        /// <summary>
        /// Fetches all active or inactive employee summaries assigned to a specific target organization ID.
        /// </summary>
        public async Task<List<EmployeeListDto>> GetEmployeesByOrganizationIdAsync(Guid organizationId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            var users = await _userManager.Users
                .Where(u => u.OrganizationId == organizationId)
                .AsNoTracking()
                .ToListAsync();

            var depts = await context.Departments
                .Where(d => d.OrganizationId == organizationId)
                .AsNoTracking().ToListAsync();

            var desigs = await context.Designations
                .Where(d => d.OrganizationId == organizationId)
                .AsNoTracking().ToListAsync();

            var shifts = await context.Shifts
                .Where(s => s.OrganizationId == organizationId)
                .AsNoTracking().ToListAsync();

            var profiles = await context.Employees
                .Where(e => e.OrganizationId == organizationId)
                .AsNoTracking().ToListAsync();

            var resultList = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
                var role = userRoles.FirstOrDefault() ?? "Employee";
                var profile = profiles.FirstOrDefault(p => p.UserId == u.Id);

                resultList.Add(new EmployeeListDto
                {
                    EmployeeId = profile?.EmployeeId ?? Guid.Empty,
                    Email = u.Email ?? string.Empty,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Name = $"{u.FirstName} {u.LastName}",
                    Role = role,
                    Department = depts.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "N/A",
                    Designation = desigs.FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "N/A",
                    DepartmentId = u.DepartmentId,
                    DesignationId = u.DesignationId,
                    ShiftId = profile?.ShiftId,
                    ShiftName = shifts.FirstOrDefault(s => s.ShiftId == profile?.ShiftId)?.Name ?? "No Shift Assigned",
                    IsActive = u.IsActive
                });
            }

            return resultList.OrderBy(x => x.Name).ToList();
        }

        #endregion

        #region Creation

        public async Task<string> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync();
            var inviterName =
    $"{currentUser.FirstName} {currentUser.LastName}";
            if (currentUser?.OrganizationId == null)
                throw new Exception("Unauthorized");

            using var context = await _contextFactory.CreateDbContextAsync();
            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                if (existingUser != null)
                    throw new Exception("User already exists");

                if (string.IsNullOrWhiteSpace(dto.Email))
                    throw new Exception("Email is required");

                var deptExists = await context.Departments.AnyAsync(d =>
                                    d.DepartmentId == dto.DepartmentId &&
                                    d.OrganizationId == currentUser.OrganizationId);

                if (!deptExists)
                    throw new Exception("Invalid department");

                var desigExists = await context.Designations.AnyAsync(d =>
                    d.DesignationId == dto.DesignationId &&
                    d.OrganizationId == currentUser.OrganizationId);

                if (!desigExists)
                    throw new Exception("Invalid designation");

                var designation = await context.Designations
                    .FirstOrDefaultAsync(d =>
                       d.DesignationId == dto.DesignationId &&
                       d.OrganizationId == currentUser.OrganizationId &&
                       d.IsActive);

                if (designation == null)
                    throw new Exception("Selected designation is inactive or invalid");

                var user = new ApplicationUser
                {
                    UserName = dto.Email,
                    Email = dto.Email,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    OrganizationId = currentUser.OrganizationId,
                    DepartmentId = dto.DepartmentId,
                    DesignationId = dto.DesignationId,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                    throw new Exception(string.Join(", ", result.Errors.Select(x => x.Description)));

                await _userManager.AddToRoleAsync(user, dto.Role ?? "Employee");

                var employee = new Employee
                {
                    UserId = user.Id,
                    OrganizationId = currentUser.OrganizationId.Value,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    DepartmentId = dto.DepartmentId ?? Guid.Empty,
                    DesignationId = dto.DesignationId ?? Guid.Empty,
                    ShiftId = dto.ShiftId,
                    EmployeeCode = await GenerateEmployeeCodeAsync(context),
                    JoiningDate = DateTime.UtcNow,
                    Status = "Active"
                };

                context.Employees.Add(employee);
                await context.SaveChangesAsync();

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                var invite = new UserInvite
                {
                    Email = dto.Email,
                    UserId = user.Id,
                    Token = token,
                    Role = dto.Role ?? "Employee",
                    OrganizationId = currentUser.OrganizationId,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                };

                context.UserInvites.Add(invite);
                await context.SaveChangesAsync();

                //await transaction.CommitAsync();

                //var baseUrl = _config["AppBaseUrl"] ?? "https://localhost:7057";
                //return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

                await transaction.CommitAsync();

                var baseUrl = _config["AppBaseUrl"] ?? "https://localhost:7057";

                var setupLink =
                    $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

                await _emailService.SendEmployeeInviteAsync(
                    dto.Email,
                    $"{dto.FirstName} {dto.LastName}",
                    dto.Role ?? "Employee",
                    setupLink,
                    inviterName);

                return setupLink;

            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<string> GenerateEmployeeCodeAsync(ApplicationDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            using var command = connection.CreateCommand();

            var currentTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
            if (currentTransaction != null)
            {
                command.Transaction = currentTransaction;
            }

            command.CommandText = "SELECT NEXT VALUE FOR EmployeeCodeSequence";

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await context.Database.OpenConnectionAsync();
            }

            var nextValue = await command.ExecuteScalarAsync();
            long value = (nextValue != DBNull.Value) ? Convert.ToInt64(nextValue) : 0;

            return $"EMP{value.ToString("D4")}";
        }
        #endregion

        public async Task<EmployeeDetailsDto?> GetEmployeeDetailsByIdAsync(Guid employeeId)
        {
            using var context = _contextFactory.CreateDbContext();

            var employee = await context.Employees
                .Include(e => e.Department)
                .Include(e => e.Designation)
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

            if (employee == null) return null;

            var user = await _userManager.FindByIdAsync(employee.UserId);
            var roles = await _userManager.GetRolesAsync(user!);
            var primaryRole = roles.FirstOrDefault() ?? "Employee";

            var recentAttendance = await context.Attendances
                .Where(a => a.EmployeeId == employeeId)
                .OrderByDescending(a => a.WorkDate)
                .Take(7)
                .Select(a => new AttendanceRecordDto
                {
                    Date = a.WorkDate,
                    CheckIn = a.CheckIn,
                    CheckOut = a.CheckOut,
                    Status = a.Status
                })
                .ToListAsync();

            var recentLeaves = await context.LeaveRequests
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employeeId && l.Status == "Approved")
                .OrderByDescending(l => l.FromDate)
                .Take(5)
                .Select(l => new LeaveRecordDto
                {
                    Type = l.LeaveType.Name,
                    Date = l.FromDate
                })
                .ToListAsync();

            var totalTrackedDays = await context.Attendances.CountAsync(a => a.EmployeeId == employeeId);
            var presentDays = await context.Attendances.CountAsync(a => a.EmployeeId == employeeId && a.IsPresent);

            return new EmployeeDetailsDto
            {
                EmployeeId = employee.EmployeeId,
                FullName = $"{employee.FirstName} {employee.LastName}",
                Email = user.Email ?? "",
                EmployeeCode = employee.EmployeeCode,
                DesignationName = employee.Designation?.Name ?? "N/A",
                DepartmentName = employee.Department?.Name ?? "N/A",
                ShiftName = employee.Shift?.Name ?? "General Shift",
                JoiningDate = employee.JoiningDate,
                Status = employee.Status,
                Role = primaryRole,
                PresentDays = presentDays,
                TotalDays = totalTrackedDays,
                RecentAttendance = recentAttendance,
                Leaves = recentLeaves
            };
        }

        public async Task<bool> UpdateMyProfileAsync(UpdateEmployeeProfileDto dto)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) return false;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            if (employee == null) return false;

            employee.Phone = dto.Phone;
            employee.Address = dto.Address;
            employee.Gender = dto.Gender;
            employee.DateOfBirth = dto.DateOfBirth;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(bool Success, string Message)> ChangePasswordAsync(ChangePasswordDto dto)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) return (false, "User not found");

            var user = await _userManager.FindByIdAsync(currentUser.Id);
            if (user == null) return (false, "User not found");

            var result = await _userManager.ChangePasswordAsync(
                user,
                dto.CurrentPassword,
                dto.NewPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(x => x.Description));
                return (false, errors);
            }

            return (true, "Password changed successfully");
        }
    }
}