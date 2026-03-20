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

        // CREATE
        //public async Task CreateAsync(CreateDepartmentDto dto)
        //{
        //    var user = await _currentUser.GetUserAsync();

        //    var dept = new Department
        //    {
        //        Name = dto.Name,
        //        Description = dto.Description,
        //        OrganizationId = user.OrganizationId.Value
        //    };

        //    _context.Departments.Add(dept);
        //    await _context.SaveChangesAsync();
        //}

        public async Task CreateAsync(CreateDepartmentDto dto)
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("User organization not found");

            var dept = new Department
            {
                Name = dto.Name,
                Description = dto.Description,
                OrganizationId = user.OrganizationId.Value
            };

            _context.Departments.Add(dept);
            await _context.SaveChangesAsync();
        }

        // GET ALL (ONLY SAME ORG)
        public async Task<List<DepartmentDto>> GetAllAsync()
        {
            var user = await _currentUser.GetUserAsync();

            return await _context.Departments
                .Where(d => d.OrganizationId == user.OrganizationId && !d.IsDeleted)
                .Select(d => new DepartmentDto
                {
                    DepartmentId = d.DepartmentId,
                    Name = d.Name,
                    Description = d.Description
                })
                .ToListAsync();
        }
    }
}