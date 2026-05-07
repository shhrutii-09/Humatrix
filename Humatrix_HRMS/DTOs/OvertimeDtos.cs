namespace Humatrix_HRMS.DTOs
{
    public class CreateOvertimeRequestDto
    {
        /// <summary>The attendance record this OT claim is for.</summary>
        public Guid AttendanceId { get; set; }

        /// <summary>
        /// Local (org-timezone) datetime the employee actually finished work.
        /// Must be after the scheduled shift end stored in Attendance.SystemCheckOut.
        /// </summary>
        public DateTime ActualCheckOut { get; set; }

        /// <summary>Reason for working overtime — required.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewOvertimeDto
    {
        public Guid OvertimeRequestId { get; set; }
        public bool Approve { get; set; }
    }
}
