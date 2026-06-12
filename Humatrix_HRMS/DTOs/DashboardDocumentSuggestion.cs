// DTOs/Documents/DashboardDocumentSuggestion.cs
// ADD these new fields to your existing DashboardDocumentSuggestion class

namespace Humatrix_HRMS.DTOs.Documents;

public class DashboardDocumentSuggestion
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string Department { get; set; } = default!;
    public string DocumentType { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public int Priority { get; set; }
    public string Category { get; set; } = "General";           // NEW: auto-sets category on upload form
    public Dictionary<string, string> SuggestedData { get; set; } = new();

    // Exit-related fields
    public Guid? ExitId { get; set; }                           // NEW
    public bool IsExitRelated { get; set; }                     // NEW
    public bool IsRevocationWarning { get; set; }               // NEW: warns about cancellation
}