using Humatrix_HRMS.DTOsA;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.ControllersA
{
    [ApiController]
    [Route("api/employee")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class EmployeeController : ControllerBase
    {
        private readonly EmployeeService _employeeService;

        public EmployeeController(EmployeeService employeeService)
        {
            _employeeService = employeeService;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var profile = await _employeeService.GetMyProfileAsync();

            if (profile == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Employee profile not found"
                });
            }

            return Ok(new
            {
                success = true,
                data = profile
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile(UpdateEmployeeProfileDto dto)
        {
            var updated = await _employeeService.UpdateMyProfileAsync(dto);

            if (!updated)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Unable to update profile"
                });
            }

            return Ok(new
            {
                success = true,
                message = "Profile updated successfully"
            });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var result = await _employeeService.ChangePasswordAsync(dto);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = true,
                message = result.Message
            });
        }
    }


}