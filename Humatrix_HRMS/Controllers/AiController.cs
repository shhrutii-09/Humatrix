using Humatrix_HRMS.Services.AI;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly AiAssistantService _aiService;

    public AiController(AiAssistantService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var response = await _aiService.ProcessQueryAsync(request.Message);
        return Ok(new { response = response.ResponseText });
    }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}