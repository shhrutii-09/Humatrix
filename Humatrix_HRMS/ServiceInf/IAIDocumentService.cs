using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Services.AI;

public interface IAIDocumentService
{
    /// <summary>
    /// Generate personalized document content using AI
    /// </summary>
    Task<string> GenerateDocumentContentAsync(
        string templateName,
        string category,
        Employee employee,
        Dictionary<string, string>? customData,
        string generatedBy);

    /// <summary>
    /// Suggest document type based on employee events
    /// </summary>
    Task<List<AIDocumentSuggestion>> SuggestDocumentsAsync(Employee employee);

    /// <summary>
    /// Generate smart notification message
    /// </summary>
    Task<string> GenerateNotificationMessageAsync(
        string documentName,
        Employee employee,
        string documentNumber);
}

public class AIDocumentSuggestion
{
    public string DocumentType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Priority { get; set; }
    public Dictionary<string, string> SuggestedData { get; set; } = new();
}