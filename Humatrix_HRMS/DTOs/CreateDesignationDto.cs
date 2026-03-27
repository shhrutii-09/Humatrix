    using System.ComponentModel.DataAnnotations;

    namespace Humatrix_HRMS.DTOs
    {
    public class CreateDesignationDto
    {
        [Required(ErrorMessage = "Designation name is required")]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        public Guid DepartmentId { get; set; }
    }
}