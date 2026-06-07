// ============================================================
// FILE: Configuration/Documents/OrgGeneratedDocumentConfiguration.cs
// ============================================================

using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humatrix_HRMS.Configuration.Documents
{
    public class OrgGeneratedDocumentConfiguration
        : IEntityTypeConfiguration<OrgGeneratedDocument>
    {
        public void Configure(EntityTypeBuilder<OrgGeneratedDocument> builder)
        {
            builder.HasKey(x => x.OrgDocumentId);

            builder.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            builder.Property(x => x.OriginalFileName).HasMaxLength(500).IsRequired();
            builder.Property(x => x.FilePath).HasMaxLength(1000).IsRequired();
            builder.Property(x => x.MimeType).HasMaxLength(200).IsRequired();
            builder.Property(x => x.IssuedByRole).HasMaxLength(50).IsRequired();
            builder.Property(x => x.DocumentNumber).HasMaxLength(200);
            builder.Property(x => x.Remarks).HasMaxLength(500);
            builder.Property(x => x.RevocationReason).HasMaxLength(500);

            // Default filter: hide revoked docs from normal queries
            // (HR/OrgAdmin explicitly bypass this with IgnoreQueryFilters() when needed)
            builder.HasQueryFilter(x => !x.IsRevoked);

            // FK: Organization — restrict; org shouldn't be deletable while docs exist
            builder.HasOne(x => x.Organization)
                   .WithMany()
                   .HasForeignKey(x => x.OrganizationId)
                   .OnDelete(DeleteBehavior.Restrict);

            // FK: Employee — restrict
            builder.HasOne(x => x.Employee)
                   .WithMany()
                   .HasForeignKey(x => x.EmployeeId)
                   .OnDelete(DeleteBehavior.Restrict);

            // FK: DocumentType — restrict
            builder.HasOne(x => x.DocumentType)
                   .WithMany()
                   .HasForeignKey(x => x.DocumentTypeId)
                   .OnDelete(DeleteBehavior.Restrict);

            // FK: Previous version (self-referencing) — no action to avoid cycles
            builder.HasOne(x => x.PreviousDocument)
                   .WithMany()
                   .HasForeignKey(x => x.PreviousOrgDocumentId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.NoAction);

            // FK: Issuer user
            builder.HasOne(x => x.IssuedByUser)
                   .WithMany()
                   .HasForeignKey(x => x.IssuedByUserId)
                   .OnDelete(DeleteBehavior.Restrict);

            // ── Indexes ───────────────────────────────────────────
            // Per-employee latest version lookup (hot path for employee portal)
            builder.HasIndex(x => new { x.EmployeeId, x.DocumentTypeId, x.IsLatestVersion })
                   .HasFilter("[IsRevoked] = 0");

            // Org-level listing for HR/OrgAdmin
            builder.HasIndex(x => new { x.OrganizationId, x.IssuedAt });
            builder.HasIndex(x => new { x.OrganizationId, x.DocumentTypeId });
        }
    }
}