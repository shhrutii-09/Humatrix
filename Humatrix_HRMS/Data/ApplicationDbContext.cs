using Humatrix_HRMS.Configuration;
using Humatrix_HRMS.Configuration.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
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
        public DbSet<OrgGeneratedDocument> OrgGeneratedDocuments { get; set; }
        public DbSet<OrgDocumentHistory> OrgDocumentHistories { get; set; }
        public DbSet<UserInvite> UserInvites { get; set; }

        public DbSet<Designation> Designations { get; set; }

        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<EmployeeRehire> EmployeeRehires { get; set; }
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

        public DbSet<EmployeeExit> EmployeeExits { get; set; }

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

        // In DbSet declarations
        public DbSet<DocumentType> DocumentTypes { get; set; }
        public DbSet<EmployeeDocument> EmployeeDocuments { get; set; }
        public DbSet<DocumentHistory> DocumentHistories { get; set; }
        public DbSet<DocumentExpiryAlert> DocumentExpiryAlerts { get; set; }
        public DbSet<OrgDocumentTemplate> OrgDocumentTemplates { get; set; }



        public DbSet<AiConversation> AiConversations { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<TicketReply> TicketReplies { get; set; }
        public DbSet<PendingAiAction> PendingAiActions { get; set; }

        //public DbSet<OrgGeneratedDocument> OrgGeneratedDocuments => Set<OrgGeneratedDocument>();
        // In OnModelCreating

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

            modelBuilder.HasSequence<long>("EmployeeCodeSequence")
      .StartsAt(1)
      .IncrementsBy(1);

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

            // Add to OnModelCreating in ApplicationDbContext.cs
            // AI Conversations
            modelBuilder.Entity<AiConversation>(entity =>
            {
                entity.HasKey(e => e.ConversationId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CreatedAt);
            });

            // Support Tickets
            modelBuilder.Entity<SupportTicket>(entity =>
            {
                entity.HasKey(e => e.TicketId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Priority).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);

                entity.HasOne(e => e.Employee)
                      .WithMany()
                      .HasForeignKey(e => e.EmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Ticket Replies
            modelBuilder.Entity<TicketReply>(entity =>
            {
                entity.HasKey(e => e.ReplyId);
                entity.HasIndex(e => e.TicketId);
                entity.HasOne(e => e.Ticket)
                      .WithMany()
                      .HasForeignKey(e => e.TicketId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Add sequence for ticket numbers
            modelBuilder.HasSequence<int>("TicketNumberSequence")
                .StartsAt(1001)
                .IncrementsBy(1);

            // Pending AI Actions - ADD THIS SECTION
            modelBuilder.Entity<PendingAiAction>(entity =>
            {
                entity.HasKey(e => e.PendingActionId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.ExpiresAt);
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
                entity.Property(e => e.ActionJson).IsRequired();
            });

            modelBuilder.ApplyConfiguration(new SupportTicketConfiguration());
            modelBuilder.ApplyConfiguration(new TicketReplyConfiguration());

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

            modelBuilder.Entity<AssetAssignment>()
    .HasIndex(a => a.AssetId)
    .HasFilter("[ReturnedAt] IS NULL")
    .IsUnique();

            modelBuilder.ApplyConfiguration(new AssetConfiguration());
                modelBuilder.ApplyConfiguration(new AssetAssignmentConfiguration());
            modelBuilder.ApplyConfiguration(new AssetRequestConfiguration());
            modelBuilder.ApplyConfiguration(new ProcurementRequestConfiguration());


            modelBuilder.Entity<EmployeeExit>(entity =>
            {
                entity.HasKey(e => e.ExitId);

                // Fix decimal precision
                entity.Property(e => e.FullFinalAmount)
                      .HasPrecision(18, 2);

                entity.HasOne(e => e.Employee)
                      .WithMany()
                      .HasForeignKey(e => e.EmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Organization)
                      .WithMany()
                      .HasForeignKey(e => e.OrganizationId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ApprovedBy)
                      .WithMany()
                      .HasForeignKey(e => e.ApprovedByEmployeeId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.OrganizationId, e.Status });
                entity.HasIndex(e => e.EmployeeId);
                entity.HasIndex(e => e.LastWorkingDay);
            });

            //// This automatically injects 'Where(e => e.IsActive)' into every single query across the system
            //modelBuilder.Entity<Employee>().HasQueryFilter(e => e.Status == "Active");
            //// This automatically injects 'Where(d => d.IsActive)' into every single department query across the system
            //modelBuilder.Entity<Department>().HasQueryFilter(d => d.IsActive);

            modelBuilder.Entity<Asset>()
    .HasOne(a => a.Department)
    .WithMany()
    .HasForeignKey(a => a.DepartmentId)
    .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Asset>()
       .HasIndex(a => new
       {
           a.OrganizationId,
           a.SerialNumber
       })
       .HasFilter("[SerialNumber] IS NOT NULL")
       .IsUnique();



            modelBuilder.ApplyConfiguration(new DocumentTypeConfiguration());
            modelBuilder.ApplyConfiguration(new EmployeeDocumentConfiguration());
            modelBuilder.ApplyConfiguration(new DocumentHistoryConfiguration());

            modelBuilder.ApplyConfiguration(new DocumentExpiryAlertConfiguration());

            modelBuilder.ApplyConfiguration(new OrgGeneratedDocumentConfiguration());


        }




    }
}
    
