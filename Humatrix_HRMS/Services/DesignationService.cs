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

        // CREATE
        public async Task CreateAsync(CreateDesignationDto dto)
        {
            var user = await _currentUser.GetUserAsync();
           
            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");
            var exists = await _context.Designations
           .AnyAsync(d =>
               d.Name.ToLower() == dto.Name.ToLower() &&
               d.DepartmentId == dto.DepartmentId &&
               d.OrganizationId == user.OrganizationId &&
               !d.IsDeleted);

            if (exists)
            {
                throw new Exception("Designation already exists in this department");
            }
            var designation = new Designation
            {
                Name = dto.Name,
                DepartmentId = dto.DepartmentId,
                OrganizationId = user.OrganizationId.Value
            };

            _context.Designations.Add(designation);
            await _context.SaveChangesAsync();
        }

        // GET ALL
        public async Task<List<DesignationDto>> GetAllAsync()
        {
            var user = await _currentUser.GetUserAsync();

            //return await _context.Designations
            //    .Where(d => d.OrganizationId == user.OrganizationId && !d.IsDeleted)
            //    .Select(d => new DesignationDto
            //    {
            //        DesignationId = d.DesignationId,
            //        Name = d.Name,
            //        Department = _context.Departments
            //            .Where(dep => dep.DepartmentId == d.DepartmentId)
            //            .Select(dep => dep.Name)
            //            .FirstOrDefault()
            //    })
            //    .ToListAsync();
            return await (
                from d in _context.Designations
                join dep in _context.Departments
                    on d.DepartmentId equals dep.DepartmentId
                where d.OrganizationId == user.OrganizationId
                      && !d.IsDeleted
                select new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    Name = d.Name,
                    Department = dep.Name
                }
            ).ToListAsync();
        }

        // GET BY DEPARTMENT (VERY IMPORTANT FOR DROPDOWN)
        public async Task<List<DesignationDto>> GetByDepartmentAsync(Guid departmentId)
        {
            var user = await _currentUser.GetUserAsync();

            return await _context.Designations
                .Where(d => d.OrganizationId == user.OrganizationId
                         && d.DepartmentId == departmentId
                         && !d.IsDeleted)
                .Select(d => new DesignationDto
                {
                    DesignationId = d.DesignationId,
                    Name = d.Name
                })
                .ToListAsync();
        }

        public async Task UpdateAsync(Guid id, CreateDesignationDto dto)
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            var designation = await _context.Designations
                .FirstOrDefaultAsync(d => d.DesignationId == id
                                      && d.OrganizationId == user.OrganizationId);

            if (designation == null)
                throw new Exception("Designation not found");

            var exists = await _context.Designations.AnyAsync(d =>
                d.DesignationId != id &&
                d.Name.ToLower() == dto.Name.ToLower() &&
                d.DepartmentId == dto.DepartmentId &&
                d.OrganizationId == user.OrganizationId &&
                !d.IsDeleted);

            if (exists)
                throw new Exception("Designation already exists");

            designation.Name = dto.Name;
            designation.DepartmentId = dto.DepartmentId;

            await _context.SaveChangesAsync();
        }
    }
}