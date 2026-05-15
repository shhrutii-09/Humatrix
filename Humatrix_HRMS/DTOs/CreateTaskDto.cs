using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class CreateTaskDto
    {
        [Required(ErrorMessage = "Employee is required")]
        public Guid? AssignedTo { get; set; }

        [Required(ErrorMessage = "Title is required")]
        public string Title { get; set; } = "";

        [Required(ErrorMessage = "Description is required")]
        public string Description { get; set; } = "";

        [Required(ErrorMessage = "Priority is required")]
        public string Priority { get; set; } = "Medium";

        [Required(ErrorMessage = "Due date is required")]
        public DateTime? DueDate { get; set; }
    }
}