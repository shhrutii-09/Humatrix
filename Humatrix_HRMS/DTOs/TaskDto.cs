namespace Humatrix_HRMS.DTOs
{
    public class TaskDto
    {
        public Guid TaskId { get; set; }

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";

        public string Priority { get; set; } = "Medium";
        public string Status { get; set; } = "Pending";

        public DateTime? DueDate { get; set; }
        public int Progress { get; set; }
        public string AssignedToName { get; set; } = "";
        public string AssignedByName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string DepartmentName { get; set; } = "";
    }
}