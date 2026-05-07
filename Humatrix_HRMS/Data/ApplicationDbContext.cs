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
        public DbSet<OfficeLocation> OfficeLocations { get; set; }

        public DbSet<LeaveBalance> LeaveBalances { get; set; }
        public DbSet<LeaveType> LeaveTypes { get; set; }

        public DbSet<LeaveRequest> LeaveRequests { get; set; }

        public DbSet<WorkWeek> WorkWeeks { get; set; }

        public DbSet<WorkFromHomeRequest> WorkFromHomeRequests { get; set; }

        public DbSet<YearlyJobLog> YearlyJobLogs { get; set; }

        public DbSet<OvertimeRequest> OvertimeRequests { get; set; }

        public DbSet<AttendanceCorrectionRequest> AttendanceCorrectionRequests { get; set; }


        //public DbSet<IdentityUserRole<string>> UserRoles { get; set; }
        //public DbSet<IdentityRole> Roles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LeaveBalance>()
                .Property(l => l.Pending)
                .HasPrecision(5, 2);

            modelBuilder.Entity<LeaveBalance>()
                .Property(l => l.Used)
                .HasPrecision(5, 2);

            modelBuilder.Entity<LeaveBalance>()
                .Property(l => l.CarriedForward)
                .HasPrecision(5, 2);

            modelBuilder.Entity<LeaveRequest>()
                .Property(l => l.TotalDays)
                .HasPrecision(5, 2);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.EmployeeId, a.WorkDate })
                .IsUnique();

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Employee)
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict); 
            modelBuilder.Entity<OvertimeRequest>()
                .HasOne(o => o.Attendance)
                .WithMany()
                .HasForeignKey(o => o.AttendanceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OvertimeRequest>()
                .HasOne(o => o.Employee)
                .WithMany()
                .HasForeignKey(o => o.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<Attendance>()
    .HasOne(a => a.User)
    .WithMany()
    .HasForeignKey(a => a.UserId)
    .OnDelete(DeleteBehavior.Restrict);
        }

    }



    }
    
