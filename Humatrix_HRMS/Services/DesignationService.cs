using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class DesignationService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public DesignationService(ApplicationDbContext context,
                                  CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // =========================
        // COMMON: GET CURRENT USER
        // =========================
        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            return user;
        }

        // =========================
        // CREATE DESIGNATION
        // =========================
        public async Task CreateAsync(CreateDesignationDto dto)
        {
            var user = await GetCurrentUserAsync();

            if (dto.DepartmentId == Guid.Empty)
                throw new Exception("Department is required");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name is required");

            var exists = await _context.Designations
                .AnyAsync(d =>
                    d.Name.ToLower() == dto.Name.ToLower() &&
                    d.DepartmentId == dto.DepartmentId &&
                    d.OrganizationId == user.OrganizationId &&
                    !d.IsDeleted);

            if (exists)
                throw new Exception("Designation already exists in this department");

            var designation = new Designation
            {
                Name = dto.Name.Trim(),
                DepartmentId = dto.DepartmentId,
                OrganizationId = user.OrganizationId.Value
            };

            _context.Designations.Add(designation);
            await _context.SaveChangesAsync();
        }

        // =========================
        // GET ALL DESIGNATIONS
        // =========================
        public async Task<List<DesignationDto>> GetAllAsync()
        {
            var user = await GetCurrentUserAsync();

            return await (
                from d in _context.Designations
                join dep in _context.Departments
                    on d.DepartmentId equals dep.DepartmentId
                where d.OrganizationId == user.OrganizationId
                      && !d.IsDeleted
                orderby d.Name
                select new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    Name = d.Name,
                    Department = dep.Name
                }
            ).ToListAsync();
        }

        // =========================
        // GET BY DEPARTMENT (FOR DROPDOWN)
        // =========================
        public async Task<List<DesignationDto>> GetByDepartmentAsync(Guid departmentId)
        {
            var user = await GetCurrentUserAsync();

            return await _context.Designations
                .Where(d => d.OrganizationId == user.OrganizationId
                         && d.DepartmentId == departmentId
                         && !d.IsDeleted)
                .OrderBy(d => d.Name)
                .Select(d => new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    Name = d.Name
                })
                .ToListAsync();
        }

        // =========================
        // UPDATE DESIGNATION
        // =========================
        public async Task UpdateAsync(Guid id, CreateDesignationDto dto)
        {
            var user = await GetCurrentUserAsync();

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name is required");

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d =>
                    d.DesignationId == id &&
                    d.OrganizationId == user.OrganizationId &&
                    !d.IsDeleted);

            if (designation == null)
                throw new Exception("Designation not found");

            var exists = await _context.Designations.AnyAsync(d =>
                d.DesignationId != id &&
                d.Name.ToLower() == dto.Name.ToLower() &&
                d.DepartmentId == designation.DepartmentId &&
                d.OrganizationId == user.OrganizationId &&
                !d.IsDeleted);

            if (exists)
                throw new Exception("Designation already exists");

            designation.Name = dto.Name.Trim();

            await _context.SaveChangesAsync();
        }

        // =========================
        // DELETE (SOFT DELETE)
        // =========================
        public async Task DeleteAsync(Guid id)
        {
            var user = await GetCurrentUserAsync();

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d =>
                    d.DesignationId == id &&
                    d.OrganizationId == user.OrganizationId &&
                    !d.IsDeleted);

            if (designation == null)
                throw new Exception("Designation not found");

            designation.IsDeleted = true;

            await _context.SaveChangesAsync();
        }
    }
}
