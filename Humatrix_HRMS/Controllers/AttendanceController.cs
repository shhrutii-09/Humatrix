//using Microsoft.AspNetCore.Mvc;
//using Humatrix_HRMS.Services;
//using Humatrix_HRMS.DTOs;

//namespace Humatrix_HRMS.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class AttendanceController : ControllerBase
//    {
//        private readonly AttendanceService _attendanceService;

//        public AttendanceController(AttendanceService attendanceService)
//        {
//            _attendanceService = attendanceService;
//        }

//        // =========================
//        // CHECK IN
//        // =========================
//        [HttpPost("check-in")]
//        public async Task<IActionResult> CheckIn([FromBody] LocationDto dto)
//        {
//            try
//            {
//                await _attendanceService.CheckInAsync(dto.Latitude, dto.Longitude);
//                return Ok(new { message = "Checked in successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        // =========================
//        // CHECK OUT
//        // =========================
//        [HttpPost("check-out")]
//        public async Task<IActionResult> CheckOut([FromBody] LocationDto dto)
//        {
//            try
//            {
//                await _attendanceService.CheckOutAsync(dto.Latitude, dto.Longitude);
//                return Ok(new { message = "Checked out successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        // =========================
//        // TODAY STATUS
//        // =========================
//        [HttpGet("today")]
//        public async Task<IActionResult> GetTodayStatus()
//        {
//            try
//            {
//                var result = await _attendanceService.GetTodayStatusAsync();
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        // =========================
//        // MY ATTENDANCE
//        // =========================
//        [HttpGet("my")]
//        public async Task<IActionResult> GetMyAttendance()
//        {
//            try
//            {
//                var result = await _attendanceService.GetMyAttendanceAsync();
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        // =========================
//        // ADMIN - ALL ATTENDANCE
//        // =========================
//        //[HttpGet("all")]
//        //public async Task<IActionResult> GetAllAttendance(
//        //    [FromQuery] DateTime? date,
//        //    [FromQuery] Guid? departmentId,
//        //    [FromQuery] string? role)
//        //{
//        //    try
//        //    {
//        //        var result = await _attendanceService.GetAllAttendanceAsync(date, departmentId, role);
//        //        return Ok(result);
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return BadRequest(new { error = ex.Message });
//        //    }
//        //}
//    }
//}