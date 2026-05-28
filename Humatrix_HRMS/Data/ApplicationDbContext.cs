using Humatrix_HRMS.Configuration;
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
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        public DbSet<WorkFromHomeRequest> WorkFromHomeRequests { get; set; }

        public DbSet<YearlyJobLog> YearlyJobLogs { get; set; }

        public DbSet<OvertimeRequest> OvertimeRequests { get; set; }

        public DbSet<AttendanceCorrectionRequest> AttendanceCorrectionRequests { get; set; }

        //public DbSet<CorrectionAuditLog> AttendanceAuditLogs { get; set; }

        public DbSet<CorrectionAuditLog> CorrectionAuditLogs { get; set; }
        public DbSet<NotificationPreferences> NotificationPreferences { get; set; }
        public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
        public DbSet<ApprovalHistory> ApprovalHistories { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }


        public DbSet<Asset> Assets { get; set; }
           public DbSet<AssetAssignment> AssetAssignments { get; set; }
        public DbSet<AssetRequest> AssetRequests { get; set; }
        public DbSet<ProcurementRequest> ProcurementRequests { get; set; }

        //public DbSet<IdentityUserRole<string>> UserRoles { get; set; }
        //public DbSet<IdentityRole> Roles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================================================================
            // LEAVE CONFIG
            // =========================================================================

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

            // =========================================================================
            // ATTENDANCE
            // =========================================================================

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.EmployeeId, a.WorkDate })
                .IsUnique();

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Employee)
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================================================================
            // OVERTIME REQUEST
            // =========================================================================

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

            // =========================================================================
            // ATTENDANCE CORRECTION REQUEST
            // =========================================================================

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.Attendance)
                .WithMany(a => a.CorrectionRequests)
                .HasForeignKey(x => x.AttendanceId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.ReviewedByEmployee)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByEmployeeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.InitiatedByHrEmployee)
                .WithMany()
                .HasForeignKey(x => x.InitiatedByHrEmployeeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // NEW: Assigned reviewer support
            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(x => x.AssignedReviewerEmployee)
                .WithMany()
                .HasForeignKey(x => x.AssignedReviewerEmployeeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================================================================
            // ATTENDANCE CORRECTION INDEXES
            // =========================================================================

            //modelBuilder.Entity<AttendanceCorrectionRequest>()
            //    .HasIndex(x => new
            //    {
            //        x.EmployeeId,
            //        x.WorkDate,
            //        x.Status
            //    });

            //modelBuilder.Entity<AttendanceCorrectionRequest>()
            //    .HasIndex(x => x.OrganizationId);

            //modelBuilder.Entity<AttendanceCorrectionRequest>()
            //    .HasIndex(x => x.AssignedReviewerEmployeeId);

            //modelBuilder.Entity<AttendanceCorrectionRequest>()
            //    .HasIndex(x => x.SubmittedAt);

            //modelBuilder.Entity<AttendanceCorrectionRequest>()
            //    .HasIndex(x => new
            //    {
            //        x.OrganizationId,
            //        x.Status,
            //        x.ReviewLevel
            //    });
            // =========================================================================
            // ATTENDANCE CORRECTION INDEXES
            // =========================================================================

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasIndex(x => new
                {
                    x.EmployeeId,
                    x.WorkDate,
                    x.CorrectionType,
                    x.Status
                });

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasIndex(x => new
                {
                    x.OrganizationId,
                    x.Status,
                    x.ReviewLevel
                });

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasIndex(x => new
                {
                    x.OrganizationId,
                    x.SubmittedAt
                });

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasIndex(x => x.AssignedReviewerEmployeeId);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasIndex(x => new
                {
                    x.EmployeeId,
                    x.WorkDate,
                    x.CorrectionType
                })
                .HasFilter("[Status] = 'Pending'")
                .IsUnique();
            // =========================================================================
            // CORRECTION AUDIT LOG
            // =========================================================================

            modelBuilder.Entity<CorrectionAuditLog>()
                .HasOne(x => x.AttendanceCorrectionRequest)
                .WithMany(x => x.AuditLogs)
                .HasForeignKey(x => x.AttendanceCorrectionRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CorrectionAuditLog>()
                .HasOne(x => x.ActorEmployee)
                .WithMany()
                .HasForeignKey(x => x.ActorEmployeeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CorrectionAuditLog>()
                .HasIndex(x => x.AttendanceCorrectionRequestId);

            modelBuilder.Entity<CorrectionAuditLog>()
                .HasIndex(x => x.OccurredAt);


            // =========================================================================
            // NOTIFICATION
            // =========================================================================

            modelBuilder.Entity<Notification>()
                .HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });

            modelBuilder.Entity<Notification>()
                .HasIndex(x => new { x.OrganizationId, x.NotificationType });

            modelBuilder.Entity<NotificationPreferences>()
                .HasIndex(x => x.UserId)
                .IsUnique();


            // =========================================================================
            // APPROVAL REQUEST
            // =========================================================================

            modelBuilder.Entity<ApprovalRequest>()
                .HasOne(x => x.RequestedByEmployee)
                .WithMany()
                .HasForeignKey(x => x.RequestedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ApprovalRequest>()
                .HasOne(x => x.CurrentApprover)
                .WithMany()
                .HasForeignKey(x => x.CurrentApproverEmployeeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ApprovalRequest>()
                .HasIndex(x => new
                {
                    x.OrganizationId,
                    x.RequestType,
                    x.Status
                });

            modelBuilder.Entity<ApprovalRequest>()
                .HasIndex(x => new
                {
                    x.RequestType,
                    x.RequestId
                })
                .IsUnique();


            // =========================================================================
            // APPROVAL HISTORY
            // =========================================================================

            modelBuilder.Entity<ApprovalHistory>()
                .HasOne(x => x.ApprovalRequest)
                .WithMany(x => x.History)
                .HasForeignKey(x => x.ApprovalRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ApprovalHistory>()
                .HasOne(x => x.PerformedByEmployee)
                .WithMany()
                .HasForeignKey(x => x.PerformedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ApprovalHistory>()
                .HasIndex(x => x.ApprovalRequestId);


            // =========================================================================
            // ACTIVITY LOG
            // =========================================================================

            modelBuilder.Entity<ActivityLog>()
                .HasIndex(x => new
                {
                    x.OrganizationId,
                    x.Module,
                    x.OccurredAt
                });

            modelBuilder.Entity<ActivityLog>()
                .HasIndex(x => new
                {
                    x.EntityType,
                    x.EntityId
                });

            modelBuilder.Entity<ActivityLog>()
                .HasIndex(x => new
                {
                    x.PerformedByUserId,
                    x.OrganizationId
                });


            modelBuilder.ApplyConfiguration(new AssetConfiguration());
                modelBuilder.ApplyConfiguration(new AssetAssignmentConfiguration());
            modelBuilder.ApplyConfiguration(new AssetRequestConfiguration());
            modelBuilder.ApplyConfiguration(new ProcurementRequestConfiguration());

        }



    }
}
    
