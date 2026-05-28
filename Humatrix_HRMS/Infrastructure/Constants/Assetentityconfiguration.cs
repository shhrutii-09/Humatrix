using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Humatrix_HRMS.Configuration
{
    // =========================================================================
    // AssetConfiguration
    // =========================================================================

    public class AssetConfiguration : IEntityTypeConfiguration<Asset>
    {
        public void Configure(EntityTypeBuilder<Asset> b)
        {
            b.HasKey(x => x.AssetId);

            // AssetCode is unique per organisation
            b.HasIndex(x => new { x.OrganizationId, x.AssetCode })
             .IsUnique();

            // Fast lookups by status within an org
            b.HasIndex(x => new { x.OrganizationId, x.Status });

            // Fast lookup for "which assets are assigned to this employee"
            b.HasIndex(x => new { x.OrganizationId, x.CurrentEmployeeId });

            b.Property(x => x.PurchasePrice)
             .HasPrecision(18, 2);

            b.HasOne(x => x.CurrentEmployee)
             .WithMany()
             .HasForeignKey(x => x.CurrentEmployeeId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Organization)
             .WithMany()
             .HasForeignKey(x => x.OrganizationId)
             .OnDelete(DeleteBehavior.Restrict);
        }
    }

    // =========================================================================
    // AssetAssignmentConfiguration
    // =========================================================================

    public class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
    {
        public void Configure(EntityTypeBuilder<AssetAssignment> b)
        {
            b.HasKey(x => x.AssetAssignmentId);

            // Enforce only one ACTIVE assignment per asset at a time.
            // A filtered unique index on (AssetId) WHERE ReturnedAt IS NULL.
            b.HasIndex(x => x.AssetId)
             .HasFilter("[ReturnedAt] IS NULL")
             .IsUnique()
             .HasDatabaseName("UX_AssetAssignment_OneActivePerAsset");

            // History lookups
            b.HasIndex(x => new { x.EmployeeId, x.ReturnedAt });
            b.HasIndex(x => new { x.AssetId, x.AssignedAt });

            b.HasOne(x => x.Asset)
             .WithMany(a => a.Assignments)
             .HasForeignKey(x => x.AssetId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Employee)
             .WithMany()
             .HasForeignKey(x => x.EmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
        }
    }

    // =========================================================================
    // AssetRequestConfiguration
    // =========================================================================

    public class AssetRequestConfiguration : IEntityTypeConfiguration<AssetRequest>
    {
        public void Configure(EntityTypeBuilder<AssetRequest> b)
        {
            b.HasKey(x => x.AssetRequestId);

            b.HasIndex(x => new { x.OrganizationId, x.Status });
            b.HasIndex(x => new { x.AssetId, x.Status });
            b.HasIndex(x => new { x.RequestedByEmployeeId, x.Status });

            // Prevent duplicate active requests of the same type for the same asset
            b.HasIndex(x => new { x.AssetId, x.RequestType })
             .HasFilter("[Status] = 'Pending'")
             .IsUnique()
             .HasDatabaseName("UX_AssetRequest_OnePendingPerTypePerAsset");

            b.HasOne(x => x.Asset)
             .WithMany(a => a.Requests)
             .HasForeignKey(x => x.AssetId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.RequestedByEmployee)
             .WithMany()
             .HasForeignKey(x => x.RequestedByEmployeeId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.ReviewedByEmployee)
             .WithMany()
             .HasForeignKey(x => x.ReviewedByEmployeeId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.ReplacementAsset)
             .WithMany()
             .HasForeignKey(x => x.ReplacementAssetId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
        }
    }

    // =========================================================================
    // ProcurementRequestConfiguration
    // =========================================================================

    public class ProcurementRequestConfiguration : IEntityTypeConfiguration<ProcurementRequest>
    {
        public void Configure(EntityTypeBuilder<ProcurementRequest> b)
        {
            b.HasKey(x => x.ProcurementRequestId);

            b.HasIndex(x => new { x.OrganizationId, x.Status });
            b.HasIndex(x => new { x.DepartmentId, x.Status });

            b.HasOne(x => x.Organization)
             .WithMany()
             .HasForeignKey(x => x.OrganizationId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Department)
             .WithMany()
             .HasForeignKey(x => x.DepartmentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.RequestedByEmployee)
             .WithMany()
             .HasForeignKey(x => x.RequestedByEmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
        }
    }
}