namespace Humatrix_HRMS.Models
{
    public class Holiday
    {
        public Guid HolidayId { get; set; }
        public Guid OrganizationId { get; set; }

        public DateTime Date { get; set; }
        //public string Name { get; set; } = "";

        public string Name { get; set; } = string.Empty;
        public bool IsOptional { get; set; } = false; // optional holidays

        
    }
}
