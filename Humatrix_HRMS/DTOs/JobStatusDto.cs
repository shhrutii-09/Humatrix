namespace Humatrix_HRMS.DTOs
{
    public class JobStatusDto
    {
        public string JobName { get; set; } = "";
        public DateTime? LastRun { get; set; }
        public string Status { get; set; } = "Not Run";
    }
}
