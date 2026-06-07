using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.DTOs.Documents;

public class EmployeeComplianceDto
{
    public Guid EmployeeId { get; set; }

    public int ProfileCompletionPercentage { get; set; }

    public bool IsFullyCompliant { get; set; }

    public int RequiredDocuments { get; set; }

    public int UploadedDocuments { get; set; }

    public List<DocumentType> MissingDocuments { get; set; }
        = new();
}