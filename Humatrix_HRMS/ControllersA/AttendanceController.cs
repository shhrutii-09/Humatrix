using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.ControllersA
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class AttendanceController : ControllerBase
    {
        private readonly AttendanceService _attendanceService;

        public AttendanceController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        // ================================
        // CHECK IN
        // ================================
        [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn(
            [FromBody] CheckLocationDto dto)
        {
            try
            {
                await _attendanceService.CheckInAsync(
                    dto.Latitude,
                    dto.Longitude);

                return Ok(new
                {
                    success = true,
                    message = "Checked in successfully"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // ================================
        // CHECK OUT
        // ================================
        [HttpPost("checkout")]
        public async Task<IActionResult> CheckOut(
            [FromBody] CheckLocationDto dto)
        {
            try
            {
                await _attendanceService.CheckOutAsync(
                    dto.Latitude,
                    dto.Longitude);

                return Ok(new
                {
                    success = true,
                    message = "Checked out successfully"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // ================================
        // TODAY STATUS
        // ================================
        [HttpGet("today")]
        public async Task<IActionResult> TodayStatus()
        {
            try
            {
                var result = await _attendanceService.GetTodayStatusAsync();

                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // ================================
        // MY ATTENDANCE
        // ================================
        [HttpGet("my")]
        public async Task<IActionResult> MyAttendance()
        {
            try
            {
                var result = await _attendanceService.GetMyAttendanceAsync();

                return Ok(new
                {
                    success = true,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }

    public class CheckLocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}