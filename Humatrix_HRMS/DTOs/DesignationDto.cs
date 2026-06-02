using System;

namespace Humatrix_HRMS.DTOs
{
    public class DesignationDto
    {
        public Guid DesignationId { get; set; }
        public Guid DepartmentId { get; set; }
        public string Name { get; set; } = string.Empty; // ✅ Fixed warning and ensured non-null
        public string? Department { get; set; }
        public bool IsActive { get; set; } = true;
    }
}