using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class DepartmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public DepartmentService(ApplicationDbContext context,
                                 CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // =========================
        // GET CURRENT USER (COMMON METHOD)
        // =========================
        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized access");

            return user;
        }

        // =========================
        // CREATE DEPARTMENT
        // =========================
        public async Task CreateAsync(CreateDepartmentDto dto)
        {
            var user = await GetCurrentUserAsync();

            var dept = new Department
            {
                Name = dto.Name,
                Description = dto.Description,
                OrganizationId = user.OrganizationId.Value
            };

            _context.Departments.Add(dept);
            await _context.SaveChangesAsync();
        }

        // =========================
        // GET ALL DEPARTMENTS (ORG BASED)
        // =========================
        public async Task<List<DepartmentDto>> GetAllAsync()
        {
            var user = await GetCurrentUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId && !d.IsDeleted)
                .OrderBy(d => d.Name) // 🔥 better UI sorting
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Description = d.Description
                })
                .ToListAsync();
        }

        // =========================
        // UPDATE DEPARTMENT
        // =========================
        public async Task UpdateAsync(Guid id, string name, string? description)
        {
            var user = await GetCurrentUserAsync();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id && !d.IsDeleted);

            if (dept == null)
                throw new Exception("Department not found");

            // 🔐 SECURITY CHECK
            if (dept.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied");

            dept.Name = name;
            dept.Description = description;

            await _context.SaveChangesAsync();
        }

        // =========================
        // DELETE (SOFT DELETE)
        // =========================
        public async Task DeleteAsync(Guid id)
        {
            var user = await GetCurrentUserAsync();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentId == id && !d.IsDeleted);

            if (dept == null)
                throw new Exception("Department not found");

            // 🔐 SECURITY CHECK
            if (dept.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied");

            dept.IsDeleted = true;

            await _context.SaveChangesAsync();
        }
    }
}
