namespace Humatrix_HRMS.DTOs.Documents;

public class DocumentDashboardDto
{
    public int ProfileCompletionPercentage { get; set; }

    public int MandatoryDocuments { get; set; }

    public int UploadedMandatoryDocuments { get; set; }

    public int MissingDocuments { get; set; }

    public int PendingVerification { get; set; }

    public int RejectedDocuments { get; set; }

    public int ExpiringSoonDocuments { get; set; }
}