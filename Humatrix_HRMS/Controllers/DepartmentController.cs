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
    public async Task<IActionResult> Create(CreateDepartmentDto dto)
    {
        await _service.CreateAsync(dto);
        return Ok(new { message = "Department created" });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var data = await _service.GetAllAsync();
        return Ok(data);
    }
}