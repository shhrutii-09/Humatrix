using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class ShiftService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public ShiftService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<List<Shift>> GetAllShiftsAsync()
        {
            // ✅ Get user FIRST (avoid concurrency issues)
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            return await _context.Shifts
                .AsNoTracking() // ✅ prevents tracking conflicts
                .Where(s => s.OrganizationId == user.OrganizationId)
                .ToListAsync();
        }

        public async Task CreateShiftAsync(Shift shift)
        {
            // ✅ Get user FIRST
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            shift.OrganizationId = user.OrganizationId.Value;
            if (string.IsNullOrWhiteSpace(shift.Name))
                throw new Exception("Shift name is required");

            var exists = await _context.Shifts.AnyAsync(s =>
                s.OrganizationId == user.OrganizationId &&
                s.Name.ToLower().Trim() == shift.Name.ToLower().Trim());

            if (exists)
                throw new Exception("Shift already exists");

            //if (shift.StartTime >= shift.EndTime)
            //    throw new Exception("Invalid shift timing");

            // Allow overnight shifts
            if (shift.StartTime == shift.EndTime)
                throw new Exception("Start and End time cannot be same");
            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateShiftAsync(Guid shiftId, Shift updated)
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s =>
                    s.ShiftId == shiftId &&
                    s.OrganizationId == user.OrganizationId)
                ?? throw new Exception("Shift not found");

            // Prevent duplicate name (excluding self)
            var nameExists = await _context.Shifts.AnyAsync(s =>
                s.OrganizationId == user.OrganizationId &&
                s.ShiftId != shiftId &&
                s.Name.ToLower().Trim() == updated.Name.ToLower().Trim());

            if (nameExists)
                throw new Exception("Shift name already in use");

            if (updated.StartTime == updated.EndTime)
                throw new Exception("Start and end time cannot be the same");

            shift.Name = updated.Name;
            shift.StartTime = updated.StartTime;
            shift.EndTime = updated.EndTime;
            shift.LateAllowanceMinutes = updated.LateAllowanceMinutes;
            shift.MinimumHoursForFullDay = updated.MinimumHoursForFullDay;
            shift.MinimumHoursForHalfDay = updated.MinimumHoursForHalfDay;

            await _context.SaveChangesAsync();
        }

    }
}