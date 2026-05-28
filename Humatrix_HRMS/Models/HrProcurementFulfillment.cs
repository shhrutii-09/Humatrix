// Models/HrProcurementFulfillment.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Records each batch of assets created by OrgAdmin to fulfill an HrProcurementRequest.
    /// Supports partial fulfillment across multiple actions.
    /// </summary>
    public class HrProcurementFulfillment
    {
        [Key]
        public Guid FulfillmentId { get; set; } = Guid.NewGuid();

        public Guid ProcurementRequestId { get; set; }

        public Guid FulfilledByEmployeeId { get; set; }

        public int QuantityFulfilled { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime FulfilledAt { get; set; } = DateTime.UtcNow;

        // ── Navigation ──────────────────────────────────────────────────────

        [ForeignKey(nameof(ProcurementRequestId))]
        public HrProcurementRequest? ProcurementRequest { get; set; }

        [ForeignKey(nameof(FulfilledByEmployeeId))]
        public Employee? FulfilledByEmployee { get; set; }

        /// <summary>Assets created during this fulfillment batch.</summary>
        public ICollection<Asset> CreatedAssets { get; set; } = new List<Asset>();
    }
}
