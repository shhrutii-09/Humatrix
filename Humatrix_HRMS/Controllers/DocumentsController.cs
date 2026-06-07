using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IEmployeeDocumentService _documentService;

    public DocumentsController(IEmployeeDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet("download/{documentId}")]
    public async Task<IActionResult> DownloadDocument(Guid documentId)
    {
        var document = await _documentService.GetDocumentAsync(documentId);

        if (document == null || document.IsDeleted)
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", document.FilePath.TrimStart('/'));

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, document.MimeType, document.OriginalFileName);
    }
}