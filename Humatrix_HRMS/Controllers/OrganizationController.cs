using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class OrganizationController : ControllerBase
{
    private readonly OrganizationService _service;

    public OrganizationController(OrganizationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganizationDto dto)
    {
        var link = await _service.CreateOrganizationAsync(dto);

        return Ok(new
        {
            message = "Organization created",
            inviteLink = link
        });
    }
}