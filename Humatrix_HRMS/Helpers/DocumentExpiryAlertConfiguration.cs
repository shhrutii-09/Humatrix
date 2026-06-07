// ============================================================
// FILE: Configuration/Documents/DocumentExpiryAlertConfiguration.cs
// ============================================================

using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humatrix_HRMS.Configuration.Documents
{
    public class DocumentExpiryAlertConfiguration : IEntityTypeConfiguration<DocumentExpiryAlert>
    {
        public void Configure(EntityTypeBuilder<DocumentExpiryAlert> builder)
        {
            builder.HasKey(x => x.AlertId);

            builder.Property(x => x.AlertType)
                   .HasMaxLength(50)
                   .IsRequired();

            // Core FK — cascade so alerts are cleaned up when a document is hard-deleted
            builder.HasOne(x => x.Document)
       .WithMany()
       .HasForeignKey(x => x.DocumentId)
       .IsRequired(false)
       .OnDelete(DeleteBehavior.NoAction);

            // Employee FK — restrict; employee records should not be deleted while alerts exist
            builder.HasOne(x => x.Employee)
                   .WithMany()
                   .HasForeignKey(x => x.EmployeeId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Prevents the job from sending the same threshold alert twice
            // for the same document (e.g. 30-day warning sent only once).
            builder.HasIndex(x => new { x.DocumentId, x.DaysBeforeExpiry })
                   .IsUnique();

            // Supports querying all alerts for a given employee or org
            builder.HasIndex(x => x.EmployeeId);
            builder.HasIndex(x => new { x.OrganizationId, x.AlertSentAt });
        }
    }
}