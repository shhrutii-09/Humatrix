using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Configuration.Documents;

public class OrgGeneratedDocumentConfiguration : IEntityTypeConfiguration<OrgGeneratedDocument>
{
    public void Configure(EntityTypeBuilder<OrgGeneratedDocument> builder)
    {
        builder.HasKey(x => x.DocumentId);

        builder.HasIndex(x => new { x.OrganizationId, x.EmployeeId });
        builder.HasIndex(x => x.DocumentNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.GeneratedAt);

        builder.Property(x => x.FileSize).HasPrecision(18, 2);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}