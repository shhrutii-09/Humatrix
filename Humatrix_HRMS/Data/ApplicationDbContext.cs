using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        { }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<Department> Departments { get; set; }

        public DbSet<UserInvite> UserInvites { get; set; }

        public DbSet<Designation> Designations { get; set; }

        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<Employee> Employees { get; set; }

        public DbSet<Shift> Shifts { get; set; }
        public DbSet<Holiday> Holidays { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<OfficeLocation> OfficeLocations { get; set; }

        //public DbSet<IdentityUserRole<string>> UserRoles { get; set; }
        //public DbSet<IdentityRole> Roles { get; set; }
    }

    }
    
