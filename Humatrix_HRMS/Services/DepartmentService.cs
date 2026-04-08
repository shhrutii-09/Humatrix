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

        public DepartmentService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            CurrentUserService currentUser)
        {
            _contextFactory = contextFactory;
            _currentUser = currentUser;
        }

        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized access");

            return user;
        }

        // ✅ CREATE
        public async Task CreateAsync(CreateDepartmentDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var exists = await _context.Departments.AnyAsync(d =>
                d.OrganizationId == user.OrganizationId &&
                d.Name.ToLower().Trim() == dto.Name.ToLower().Trim());

            if (exists)
                throw new Exception("Department already exists");

            var dept = new Department
            {
                Name = dto.Name.Trim(),
                Description = dto.Description,
                OrganizationId = user.OrganizationId.Value,
                IsActive = true
            };

            _context.Departments.Add(dept);
            await _context.SaveChangesAsync();
        }

        // ✅ GET ALL (IMPORTANT: show ALL, not only active)
        public async Task<List<DepartmentDto>> GetAllAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId)
                .OrderBy(d => d.Name)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }

        // ✅ UPDATE (IMPORTANT FIX)
        public async Task UpdateAsync(Guid id, string name, string? description)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id);

            if (dept == null)
                throw new Exception("Department not found");

            if (dept.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied");

            dept.Name = name;
            dept.Description = description;

            await _context.SaveChangesAsync();
        }

        // ✅ TOGGLE (MAIN FIX)
        public async Task ToggleStatusAsync(Guid id, bool isActive)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id);

            if (dept == null)
                throw new Exception("Department not found");

            if (dept.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied");

            dept.IsActive = isActive; // ✅ CORRECT

            await _context.SaveChangesAsync();
        }

        public async Task<DepartmentDto?> GetByIdAsync(Guid id)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d =>
                    d.DepartmentId == id &&
                    d.OrganizationId == user.OrganizationId);

            if (dept == null)
                return null;

            return new DepartmentDto
            {
                DepartmentId = dept.DepartmentId,
                Name = dept.Name,
                Description = dept.Description,
                IsActive = dept.IsActive
            };
        }

        public async Task<List<DepartmentDto>> GetActiveAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId && d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .ToListAsync();
        }
    }
}