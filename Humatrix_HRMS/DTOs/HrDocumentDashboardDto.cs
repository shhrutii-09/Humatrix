namespace Humatrix_HRMS.DTOs.Documents;

public class HrDocumentDashboardDto
{
    public int EmployeesMissingDocuments { get; set; }

    public int PendingVerificationCount { get; set; }

    public int ExpiringSoonCount { get; set; }

    public int RejectedDocumentsCount { get; set; }

    public decimal CompliancePercentage { get; set; }
}