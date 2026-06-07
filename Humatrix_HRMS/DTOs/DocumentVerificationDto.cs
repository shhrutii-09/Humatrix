namespace Humatrix_HRMS.DTOs.Documents;

public class DocumentVerificationDto
{
    public Guid DocumentId { get; set; }

    public bool Approve { get; set; }

    public string? Remarks { get; set; }
}