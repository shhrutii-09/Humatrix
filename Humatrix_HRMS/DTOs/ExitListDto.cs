namespace Humatrix_HRMS.DTOs.Exit;

public class ExitListDto
{
    public Guid ExitId { get; set; }

    public Guid EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = "";

    public string EmployeeName { get; set; } = "";

    public string Department { get; set; } = "";

    public string Designation { get; set; } = "";

    public DateTime ResignationDate { get; set; }

    public DateTime LastWorkingDay { get; set; }

    public string Reason { get; set; } = "";

    public string Status { get; set; } = "";

    public bool HasAssetsPending { get; set; }

    public bool HasPendingTasks { get; set; }
}