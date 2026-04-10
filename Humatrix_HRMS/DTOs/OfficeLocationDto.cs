namespace Humatrix_HRMS.DTOs
{
    public class OfficeLocationDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Main Office";

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public double RadiusInMeters { get; set; } = 100;
    }
}