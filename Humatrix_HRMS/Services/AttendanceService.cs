using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class AttendanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public AttendanceService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task CheckInAsync()
        {
            var user = await _currentUser.GetUserAsync();

            var today = DateTime.UtcNow.Date;

            var exists = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (exists != null && exists.CheckIn != null)
                throw new Exception("Already checked in");

            if (exists == null)
            {
                var attendance = new Attendance
                {
                    UserId = user.Id,
                    Date = today,
                    CheckIn = DateTime.UtcNow,
                    IsPresent = true,
                    OrganizationId = user.OrganizationId.Value
                };

                _context.Attendances.Add(attendance);
            }
            else
            {
                exists.CheckIn = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var today = DateTime.UtcNow.Date;

            return await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);
        }

        public async Task CheckOutAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var today = DateTime.UtcNow.Date;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out");

            attendance.CheckOut = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task<List<Attendance>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null)
                throw new Exception("User not found");

            return await _context.Attendances
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.Date)
                .ToListAsync();
        }

        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(DateTime? date = null)
        {
            var currentUser = await _currentUser.GetUserAsync();

            var query = _context.Attendances
                .Include(a => a.User)
                .ThenInclude(u => u.Department)
                .Where(a => a.OrganizationId == currentUser.OrganizationId);

            if (date.HasValue)
            {
                var selectedDate = date.Value.Date;
                query = query.Where(a => a.Date == selectedDate);
            }

            var data = await query.ToListAsync();

            return data.Select(a => new AttendanceListDto
            {
                EmployeeName = a.User.FirstName + " " + a.User.LastName,
                Email = a.User.Email,
                Department = a.User.Department != null ? a.User.Department.Name : "",

                Date = a.Date,
                CheckIn = a.CheckIn,
                CheckOut = a.CheckOut,

                Status = GetStatus(a)
            }).ToList();
        }

        private string GetStatus(Attendance a)
        {
            if (a.CheckIn == null)
                return "Absent";

            var checkInTime = a.CheckIn.Value.TimeOfDay;

            if (checkInTime > new TimeSpan(10, 30, 0))
                return "Late";

            if (a.CheckOut != null)
            {
                var hours = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;

                if (hours < 4)
                    return "Half Day";
            }

            return "Present";
        }
    }
}
