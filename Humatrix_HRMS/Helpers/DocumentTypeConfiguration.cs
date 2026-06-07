using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humatrix_HRMS.Configuration.Documents
{
    public class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
    {
        public void Configure(EntityTypeBuilder<DocumentType> builder)
        {
            builder.HasKey(x => x.DocumentTypeId);

            builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
            builder.Property(x => x.AllowedFileTypes).HasMaxLength(200);

            builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            builder.HasIndex(x => new { x.OrganizationId, x.IsActive });

            builder.HasOne(x => x.Organization)
                   .WithMany()
                   .HasForeignKey(x => x.OrganizationId)
                   .OnDelete(DeleteBehavior.Restrict);

            // In DocumentTypeConfiguration.cs, add:
            builder.Property(x => x.IsOrganizationGenerated).HasDefaultValue(false);

            // In EmployeeDocumentConfiguration.cs, add:
            //builder.Property(x => x.Description).HasMaxLength(500);
        }
    }

    public class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
    {
        public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
        {
            builder.HasKey(x => x.DocumentId);

            builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
            builder.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            builder.Property(x => x.FilePath).HasMaxLength(1000).IsRequired();
            builder.Property(x => x.MimeType).HasMaxLength(200).IsRequired();


            builder.Property(x => x.Description).HasMaxLength(500); // Add this line
            builder.Property(x => x.EffectiveDate);

            builder.HasQueryFilter(x => !x.IsDeleted);

            //builder.HasOne(x => x.Employee)
            //       .WithMany()
            //       .HasForeignKey(x => x.EmployeeId)
            //       .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Employee)
       .WithMany(e => e.Documents)
       .HasForeignKey(x => x.EmployeeId)
       .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.DocumentType)
                   .WithMany(dt => dt.Documents)
                   .HasForeignKey(x => x.DocumentTypeId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Organization)
                   .WithMany()
                   .HasForeignKey(x => x.OrganizationId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.PreviousDocument)
                   .WithMany()
                   .HasForeignKey(x => x.PreviousDocumentId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);

            // Filtered indexes
            builder.HasIndex(x => new
            {
                x.EmployeeId,
                x.DocumentTypeId,
                x.IsLatestVersion
            })
  .HasFilter("[IsDeleted] = 0");
            builder.HasIndex(x => new { x.OrganizationId, x.Status })
                   .HasFilter("[IsDeleted] = 0");

            builder.HasIndex(x => new { x.OrganizationId, x.ExpiryDate })
                   .HasFilter("[ExpiryDate] IS NOT NULL AND [IsDeleted] = 0 AND [Status] = 'Verified'");
        }
    }

    public class DocumentHistoryConfiguration : IEntityTypeConfiguration<DocumentHistory>
    {
        public void Configure(EntityTypeBuilder<DocumentHistory> builder)
        {
            builder.HasKey(x => x.HistoryId);

            builder.HasOne(x => x.Document)
        .WithMany(d => d.History)
        .HasForeignKey(x => x.DocumentId)
        .IsRequired(false)
        .OnDelete(DeleteBehavior.NoAction);

            builder.HasIndex(x => x.DocumentId);
            builder.HasIndex(x => x.EmployeeId);
            builder.HasIndex(x => new { x.OrganizationId, x.OccurredAt });
        }
    }
}