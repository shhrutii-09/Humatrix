using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "OrgAdmin")]
public class DesignationController : ControllerBase
{
    private readonly DesignationService _service;

    public DesignationController(DesignationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDesignationDto dto)
    {
        await _service.CreateAsync(dto);
        return Ok(new { message = "Designation created" });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var data = await _service.GetAllAsync();
        return Ok(data);
    }

    [HttpGet("by-department/{departmentId}")]
    public async Task<IActionResult> GetByDepartment(Guid departmentId)
    {
        var data = await _service.GetByDepartmentAsync(departmentId);
        return Ok(data);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, CreateDesignationDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return Ok(new { message = "Designation updated" });
    }
}