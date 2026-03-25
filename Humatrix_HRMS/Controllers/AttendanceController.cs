using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "HR,OrgAdmin")]
    public class AttendanceController : ControllerBase
    {
        private readonly AttendanceService _attendanceService;

        public AttendanceController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        // 🔥 GET ALL ATTENDANCE
        [HttpGet]
        public async Task<ActionResult<List<AttendanceListDto>>> GetAll(DateTime? date)
        {
            var data = await _attendanceService.GetAllAttendanceAsync(date);
            return Ok(data);
        }
    }
}