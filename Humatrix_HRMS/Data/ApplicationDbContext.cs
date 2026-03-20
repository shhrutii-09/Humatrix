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
}

    }
    
