using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class DepartmentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly CurrentUserService _currentUser;
        private readonly DesignationService _designationService;
        private readonly EmployeeService _employeeService;
        private readonly DepartmentEventService _eventService;

        public DepartmentService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            CurrentUserService currentUser,
            DesignationService designationService,
            EmployeeService employeeService,
            DepartmentEventService eventService)
        {
            _contextFactory = contextFactory;
            _currentUser = currentUser;
            _designationService = designationService;
            _employeeService = employeeService;
            _eventService = eventService;
        }

        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized: Organization context missing.");
            return user;
        }

        public async Task<List<DepartmentDto>> GetAllAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }

        public async Task<List<DepartmentDto>> GetActiveAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId && d.IsActive)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }

        public async Task<DepartmentDto?> GetByIdAsync(Guid id)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.DepartmentId == id && d.OrganizationId == user.OrganizationId)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .FirstOrDefaultAsync();
        }

        public async Task<List<DepartmentDto>> GetDepartmentsByOrganizationAsync(Guid organizationId)
        {
            using var _context = _contextFactory.CreateDbContext();

            return await _context.Departments
                .Where(d => d.OrganizationId == organizationId)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name ?? string.Empty,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }

        public async Task ToggleStatusAsync(Guid id, bool isActive)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id && d.OrganizationId == user.OrganizationId);

            if (department == null)
                throw new Exception("Department records not found.");

            department.IsActive = isActive;
            await _context.SaveChangesAsync();

            if (!isActive)
            {
                await _designationService.BulkInactivateByDepartmentAsync(id, user.OrganizationId.Value);
                await _employeeService.BulkInactivateByDepartmentAsync(id, user.OrganizationId.Value);
            }

            _eventService.NotifyStateChanged();
        }

        public async Task CreateAsync(CreateDepartmentDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Department name cannot be blank.");

            var exists = await _context.Departments
                .AnyAsync(d => d.Name.ToLower() == dto.Name.Trim().ToLower() && d.OrganizationId == user.OrganizationId);

            if (exists)
                throw new Exception("Department already exists.");

            var department = new Department
            {
                Name = dto.Name.Trim(),
                Description = dto.Description.Trim(), // ✅ Description saved
                OrganizationId = user.OrganizationId.Value,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Departments.Add(department);
            await _context.SaveChangesAsync();
            _eventService.NotifyStateChanged();
        }

        public async Task UpdateAsync(Guid id, CreateDepartmentDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();
            await ExecuteUpdateInternalAsync(_context, id, dto.Name, dto.Description, user.OrganizationId.Value);
        }

        public async Task UpdateAsync(Guid id, string name, string? description)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();
            await ExecuteUpdateInternalAsync(_context, id, name, description, user.OrganizationId.Value);
        }

        public async Task UpdateAsync(Guid id, CreateDepartmentDto dto, Guid organizationId)
        {
            using var _context = _contextFactory.CreateDbContext();
            await ExecuteUpdateInternalAsync(_context, id, dto.Name, dto.Description, organizationId);
        }

        public async Task UpdateAsync(Guid id, string name, Guid organizationId)
        {
            using var _context = _contextFactory.CreateDbContext();
            await ExecuteUpdateInternalAsync(_context, id, name, null, organizationId);
        }

        private async Task ExecuteUpdateInternalAsync(ApplicationDbContext context, Guid id, string name, string? description, Guid organizationId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Department name is required.");

            var department = await context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id && d.OrganizationId == organizationId);

            if (department == null)
                throw new Exception("Department not found.");

            var exists = await context.Departments.AnyAsync(d =>
                d.DepartmentId != id &&
                d.Name.ToLower() == name.Trim().ToLower() &&
                d.OrganizationId == organizationId);

            if (exists)
                throw new Exception("Department name already exists.");

            department.Name = name.Trim();
            department.Description = description;
            await context.SaveChangesAsync();
            _eventService.NotifyStateChanged();
        }
    }
}