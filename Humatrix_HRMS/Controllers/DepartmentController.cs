using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "OrgAdmin")]
public class DepartmentController : ControllerBase
{
    private readonly DepartmentService _service;

    public DepartmentController(DepartmentService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentDto dto)
    {
        // Validate model before proceeding
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await _service.CreateAsync(dto);
            return Ok(new { message = "Department created" });
        }
        catch (Exception ex)
        {
            // Catch organization check failures or other errors
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var data = await _service.GetAllAsync();
        return Ok(data);
    }
}