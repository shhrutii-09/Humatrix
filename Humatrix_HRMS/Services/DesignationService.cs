using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class DesignationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly CurrentUserService _currentUser;
        private readonly EmployeeService _employeeService; // New Injection

        public DesignationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            CurrentUserService currentUser,
            EmployeeService employeeService) // Added to constructor
        {
            _contextFactory = contextFactory;
            _currentUser = currentUser;
            _employeeService = employeeService;
        }

        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized: Organization context missing.");
            return user;
        }

        public async Task BulkInactivateByDepartmentAsync(Guid departmentId, Guid organizationId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var linkedDesignations = await _context.Designations
                .Where(d => d.DepartmentId == departmentId && d.OrganizationId == organizationId && d.IsActive)
                .ToListAsync();

            foreach (var designation in linkedDesignations)
            {
                designation.IsActive = false;
            }

            await _context.SaveChangesAsync();
        }

        public async Task CreateAsync(CreateDesignationDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            if (dto.DepartmentId == Guid.Empty)
                throw new Exception("Department selection is required.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name cannot be blank.");

            var dept = await _context.Departments
                .FirstOrDefaultAsync(x => x.DepartmentId == dto.DepartmentId && x.OrganizationId == user.OrganizationId);

            if (dept == null)
                throw new Exception("Selected department does not exist.");

            if (!dept.IsActive)
                throw new Exception("Cannot add designations to an Inactive Department.");

            var exists = await _context.Designations
                .AnyAsync(d =>
                    d.Name.ToLower() == dto.Name.Trim().ToLower() &&
                    d.DepartmentId == dto.DepartmentId &&
                    d.OrganizationId == user.OrganizationId);

            if (exists)
                throw new Exception("Designation already exists within this department.");

            var designation = new Designation
            {
                Name = dto.Name.Trim(),
                DepartmentId = dto.DepartmentId,
                OrganizationId = user.OrganizationId.Value,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Designations.Add(designation);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Guid id, CreateDesignationDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name is required.");

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d => d.DesignationId == id && d.OrganizationId == user.OrganizationId);

            if (designation == null)
                throw new Exception("Designation records not found.");

            var exists = await _context.Designations.AnyAsync(d =>
                d.DesignationId != id &&
                d.Name.ToLower() == dto.Name.Trim().ToLower() &&
                d.DepartmentId == designation.DepartmentId &&
                d.OrganizationId == user.OrganizationId);

            if (exists)
                throw new Exception("Designation name already exists in this department.");

            designation.Name = dto.Name.Trim();
            await _context.SaveChangesAsync();
        }

        public async Task<List<DesignationDto>> GetAllAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await (
                from d in _context.Designations.OrderByDescending(x => x.CreatedAt)
                join dep in _context.Departments on d.DepartmentId equals dep.DepartmentId
                where d.OrganizationId == user.OrganizationId
                select new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    Department = dep.Name,
                    IsActive = d.IsActive
                }
            ).ToListAsync();
        }

        public async Task<List<DesignationDto>> GetByDepartmentAsync(Guid departmentId)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Designations
                .Where(d => d.OrganizationId == user.OrganizationId
                          && d.DepartmentId == departmentId
                          && d.IsActive)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }

        public async Task ToggleStatusAsync(Guid id, bool isActive)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d => d.DesignationId == id);

            if (designation == null)
                throw new Exception("Designation reference missing.");

            if (designation.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied.");

            if (isActive)
            {
                var dept = await _context.Departments.FirstOrDefaultAsync(x => x.DepartmentId == designation.DepartmentId);
                if (dept != null && !dept.IsActive)
                {
                    throw new Exception("Cannot activate designation because its parent Department is Inactive.");
                }
            }

            designation.IsActive = isActive;
            await _context.SaveChangesAsync();

            // Cascade Deactivation to Employees
            if (!isActive)
            {
                await _employeeService.BulkInactivateByDesignationAsync(id, user.OrganizationId.Value);
            }
        }
    }
}