using System;

namespace Humatrix_HRMS.DTOs
{
    public class DepartmentDto
    {
        public Guid DepartmentId { get; set; }
        public string Name { get; set; } = string.Empty; // ✅ Handled compilation warning
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}