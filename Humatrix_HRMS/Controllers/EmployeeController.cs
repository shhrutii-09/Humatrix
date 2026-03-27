using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ControllerBase
    {
        private readonly EmployeeService _service;

        public EmployeeController(EmployeeService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateEmployeeDto dto)
        {
            var link = await _service.CreateEmployeeAsync(dto);

            return Ok(new
            {
                message = "Employee created",
                inviteLink = link
            });
        }
    }
}
