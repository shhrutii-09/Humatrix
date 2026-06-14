// Configuration/SupportTicketConfiguration.cs
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humatrix_HRMS.Configuration
{
    public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
    {
        public void Configure(EntityTypeBuilder<SupportTicket> entity)
        {
            entity.HasKey(t => t.TicketId);

            // Auto-increment ticket number via DB sequence
            entity.Property(t => t.TicketNumber)
                  .ValueGeneratedOnAdd();

            entity.HasIndex(t => t.TicketNumber)
                  .IsUnique();

            entity.HasIndex(t => new { t.OrganizationId, t.Status });
            entity.HasIndex(t => new { t.OrganizationId, t.CreatedAt });
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.AssignedToUserId);

            // Raiser employee – restrict delete to protect ticket history
            entity.HasOne(t => t.Employee)
                  .WithMany()
                  .HasForeignKey(t => t.EmployeeId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.Restrict);

            // Assigned HR employee
            entity.HasOne(t => t.AssignedTo)
                  .WithMany()
                  .HasForeignKey(t => t.AssignedToEmployeeId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.Restrict);

            // Replies (cascade – deleting a ticket removes its replies)
            entity.HasMany(t => t.Replies)
                  .WithOne(r => r.Ticket)
                  .HasForeignKey(r => r.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(t => t.Category).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Description).HasMaxLength(4000).IsRequired();
            entity.Property(t => t.Priority).HasMaxLength(50).HasDefaultValue("Medium");
            entity.Property(t => t.Status).HasMaxLength(50).HasDefaultValue("Open");
            entity.Property(t => t.Source).HasMaxLength(20).HasDefaultValue("Manual");
            entity.Property(t => t.Resolution).HasMaxLength(4000);
            entity.Property(t => t.InternalNote).HasMaxLength(4000);
        }
    }

    public class TicketReplyConfiguration : IEntityTypeConfiguration<TicketReply>
    {
        public void Configure(EntityTypeBuilder<TicketReply> entity)
        {
            entity.HasKey(r => r.ReplyId);

            entity.HasIndex(r => r.TicketId);
            entity.HasIndex(r => r.CreatedAt);

            entity.Property(r => r.Message).HasMaxLength(4000).IsRequired();
            entity.Property(r => r.IsInternalNote).HasDefaultValue(false);
        }
    }
}