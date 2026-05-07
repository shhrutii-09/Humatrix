using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models
{
    public class AttendanceCorrectionRequest
    {
        public Guid AttendanceCorrectionRequestId { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; }

        public Guid? AttendanceId { get; set; }

        [ForeignKey(nameof(AttendanceId))]
        public Attendance? Attendance { get; set; }

        // Requested date
        public DateTime WorkDate { get; set; }

        // Request Type
        public string RequestType { get; set; } = null!;
        // MissingCheckIn
        // MissingCheckOut
        // WrongCheckIn
        // WrongCheckOut
        // FullCorrection

        // Existing values
        public DateTime? ExistingCheckIn { get; set; }
        public DateTime? ExistingCheckOut { get; set; }

        // Requested values
        public DateTime? RequestedCheckIn { get; set; }
        public DateTime? RequestedCheckOut { get; set; }

        // Employee reason
        public string Reason { get; set; } = null!;

        // HR action
        public string Status { get; set; } = "Pending";
        // Pending / Approved / Rejected

        public string? HRRemarks { get; set; }

        public Guid? ReviewedByEmployeeId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        // System flags
        public bool IsApplied { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}