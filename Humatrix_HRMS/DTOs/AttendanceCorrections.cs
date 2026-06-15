namespace Humatrix_HRMS.DTOs
{
    public class AttendanceListDto
    {
        public Guid? AttendanceId { get; set; }
        public string? EmployeeName { get; set; }
        public string? Email { get; set; }
        public string? Department { get; set; }
        public Guid? DepartmentId { get; set; }

        public string? Role { get; set; }
        public DateTime Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public DateTime? SystemCheckOut { get; set; } // The "Shift End" target
        public double OvertimeHours { get; set; }     // Raw OT calculated
        public double ApprovedOvertimeHours { get; set; } // OT after HR approval
        public string? Status { get; set; }
        public double? TotalHours { get; set; }
        public bool IsManual { get; set; }
        public bool? NeedsOvertimeApproval { get; set; }
        public string? OvertimeStatus { get; set; }
    }

      public class CreateOvertimeRequestDto
    {
        public Guid AttendanceId { get; set; }
        public DateTime ActualCheckOut { get; set; }
        public string? Reason { get; set; }
    }

    public class ReviewOvertimeDto
    {
        public Guid OvertimeRequestId { get; set; }
        public bool Approve { get; set; }
        public string? RejectionReason { get; set; }
    }

   
    public class CreateCorrectionRequestDto
    {
        public DateTime WorkDate { get; set; }
        public DateTime? RequestedCheckIn { get; set; }    
        public DateTime? RequestedCheckOut { get; set; }
        public string? Reason { get; set; }
    }

    public class LocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}