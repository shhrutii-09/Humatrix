    using System.ComponentModel.DataAnnotations;

    namespace Humatrix_HRMS.DTOs
    {
        public class CreateDesignationDto
        {
           
            public string Name { get; set; }

            [Required]
            public Guid DepartmentId { get; set; }
        }
    }