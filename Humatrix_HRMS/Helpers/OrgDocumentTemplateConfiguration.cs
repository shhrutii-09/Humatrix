using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Configuration.Documents;

public class OrgDocumentTemplateConfiguration : IEntityTypeConfiguration<OrgDocumentTemplate>
{
    public void Configure(EntityTypeBuilder<OrgDocumentTemplate> builder)
    {
        builder.HasKey(x => x.TemplateId);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Category });
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}