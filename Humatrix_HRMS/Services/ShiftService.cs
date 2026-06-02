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

        // ========================================================
        // GET ALL SHIFTS (STRICT CHRONOLOGICAL TIMELINE ORDER)
        // ========================================================
        public async Task<List<Shift>> GetAllShiftsAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized: Organization context missing.");

            // OrderBy(s => s.StartTime) ensures that early shifts display at the top
            return await _context.Shifts
                .AsNoTracking()
                .Where(s => s.OrganizationId == user.OrganizationId)
                .OrderBy(s => s.StartTime)
                .ToListAsync();
        }

        // ========================================================
        // CREATE SHIFT CONFIGURATION (WITH DUPLICATE VALIDATION)
        // ========================================================
        public async Task CreateShiftAsync(Shift shift)
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized: User validation check failed.");

            shift.OrganizationId = user.OrganizationId.Value;

            if (string.IsNullOrWhiteSpace(shift.Name))
                throw new Exception("Shift metric identifier name is required.");

            // Duplicate validation logic
            var isDuplicate = await _context.Shifts.AnyAsync(s =>
                s.OrganizationId == user.OrganizationId &&
                s.Name.ToLower().Trim() == shift.Name.ToLower().Trim());

            if (isDuplicate)
            {
                throw new Exception($"A corporate shift profile with the name '{shift.Name}' already exists in your registry.");
            }

            if (shift.StartTime == shift.EndTime)
                throw new Exception("Operational boundaries overlap: Start and End times cannot match.");

            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
        }

        // ========================================================
        // UPDATE SHIFT METRICS
        // ========================================================
        public async Task UpdateShiftAsync(Guid shiftId, Shift updated)
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized: Scope verification refused.");

            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.ShiftId == shiftId && s.OrganizationId == user.OrganizationId)
                ?? throw new Exception("Target shift configuration profiles not found.");

            var nameExists = await _context.Shifts.AnyAsync(s =>
                s.OrganizationId == user.OrganizationId &&
                s.ShiftId != shiftId &&
                s.Name.ToLower().Trim() == updated.Name.ToLower().Trim());

            if (nameExists)
                throw new Exception("Shift baseline profile name is already in use by another registry item.");

            if (updated.StartTime == updated.EndTime)
                throw new Exception("Operational boundaries overlap: Start and End times cannot match.");

            shift.Name = updated.Name.Trim();
            shift.StartTime = updated.StartTime;
            shift.EndTime = updated.EndTime;
            shift.LateAllowanceMinutes = updated.LateAllowanceMinutes;
            shift.MinimumHoursForFullDay = updated.MinimumHoursForFullDay;
            shift.MinimumHoursForHalfDay = updated.MinimumHoursForHalfDay;

            await _context.SaveChangesAsync();
        }
    }
}