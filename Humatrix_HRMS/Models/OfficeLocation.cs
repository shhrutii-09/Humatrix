namespace Humatrix_HRMS.Models
{
    public class OfficeLocation
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = "Main Office";

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        // Allowed radius (in meters)
        public double RadiusInMeters { get; set; } = 100;

        public Guid OrganizationId { get; set; }
    }
}
