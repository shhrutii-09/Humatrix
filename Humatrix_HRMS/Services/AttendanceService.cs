using Humatrix_HRMS.Data;
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
    }
}
