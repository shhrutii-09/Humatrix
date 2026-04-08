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

        public DesignationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            CurrentUserService currentUser)
        {
            _contextFactory = contextFactory;
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
        // =========================
        // CREATE DESIGNATION
        // =========================
        public async Task CreateAsync(CreateDesignationDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            if (dto.DepartmentId == Guid.Empty)
                throw new Exception("Department is required");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name is required");

            // FIX: Removed !d.IsActive so it checks for duplicates among all records
            var exists = await _context.Designations
                .AnyAsync(d =>
                    d.Name.ToLower() == dto.Name.ToLower() &&
                    d.DepartmentId == dto.DepartmentId &&
                    d.OrganizationId == user.OrganizationId);

            if (exists)
                throw new Exception("Designation already exists in this department");

            var designation = new Designation
            {
                Name = dto.Name.Trim(),
                DepartmentId = dto.DepartmentId,
                OrganizationId = user.OrganizationId.Value,
                IsActive = true
            };

            _context.Designations.Add(designation);
            await _context.SaveChangesAsync();
        }

        // =========================
        // UPDATE DESIGNATION
        // =========================
        public async Task UpdateAsync(Guid id, CreateDesignationDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Designation name is required");

            // FIX: Removed !d.IsActive. It was preventing updates on active records.
            var designation = await _context.Designations
                .FirstOrDefaultAsync(d =>
                    d.DesignationId == id &&
                    d.OrganizationId == user.OrganizationId);

            if (designation == null)
                throw new Exception("Designation not found");

            // FIX: Removed !d.IsActive from duplicate check
            var exists = await _context.Designations.AnyAsync(d =>
                d.DesignationId != id &&
                d.Name.ToLower() == dto.Name.ToLower() &&
                d.DepartmentId == designation.DepartmentId &&
                d.OrganizationId == user.OrganizationId);

            if (exists)
                throw new Exception("Designation name already exists in this department");

            designation.Name = dto.Name.Trim();

            await _context.SaveChangesAsync();
        }

        // =========================
        // GET ALL DESIGNATIONS
        // =========================
        public async Task<List<DesignationDto>> GetAllAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await (
                from d in _context.Designations
                join dep in _context.Departments
                    on d.DepartmentId equals dep.DepartmentId
                where d.OrganizationId == user.OrganizationId
                orderby d.Name
                select new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Department = dep.Name,
                    IsActive = d.IsActive // ✅
                }
            ).ToListAsync();
        }

        // =========================
        // GET BY DEPARTMENT (FOR DROPDOWN)
        // =========================
        // =========================
        // GET BY DEPARTMENT (FOR DROPDOWN)
        // =========================
        public async Task<List<DesignationDto>> GetByDepartmentAsync(Guid departmentId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var user = await GetCurrentUserAsync();

            return await _context.Designations
                .Where(d => d.OrganizationId == user.OrganizationId
                         && d.DepartmentId == departmentId
                         && d.IsActive) // ✅ Changed from !d.IsActive to d.IsActive
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
        //public async Task UpdateAsync(Guid id, CreateDesignationDto dto)
        //{
        //    using var _context = _contextFactory.CreateDbContext();

        //    var user = await GetCurrentUserAsync();

        //    if (string.IsNullOrWhiteSpace(dto.Name))
        //        throw new Exception("Designation name is required");

        //    var designation = await _context.Designations
        //        .FirstOrDefaultAsync(d =>
        //            d.DesignationId == id &&
        //            d.OrganizationId == user.OrganizationId &&
        //            !d.IsActive);

        //    if (designation == null)
        //        throw new Exception("Designation not found");

        //    var exists = await _context.Designations.AnyAsync(d =>
        //        d.DesignationId != id &&
        //        d.Name.ToLower() == dto.Name.ToLower() &&
        //        d.DepartmentId == designation.DepartmentId &&
        //        d.OrganizationId == user.OrganizationId &&
        //        !d.IsActive);

        //    if (exists)
        //        throw new Exception("Designation already exists");

        //    designation.Name = dto.Name.Trim();

        //    await _context.SaveChangesAsync();
        //}


        public async Task ToggleStatusAsync(Guid id, bool isActive)
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d => d.DesignationId == id);

            if (designation == null)
                throw new Exception("Designation not found");

            if (designation.OrganizationId != user.OrganizationId)
                throw new Exception("Access denied");

            designation.IsActive = isActive;

            await _context.SaveChangesAsync();
        }

        public async Task<List<DesignationDto>> GetActiveAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await GetCurrentUserAsync();

            return await (
                from d in _context.Designations
                join dep in _context.Departments
                    on d.DepartmentId equals dep.DepartmentId
                where d.OrganizationId == user.OrganizationId
                      && d.IsActive // ✅ ONLY ACTIVE
                orderby d.Name
                select new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Department = dep.Name,
                    IsActive = d.IsActive
                }
            ).ToListAsync();
        }
    }
}