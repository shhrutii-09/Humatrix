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
            var user = await _currentUser.GetUserAsync();
            return await _context.Shifts
                .Where(s => s.OrganizationId == user.OrganizationId)
                .ToListAsync();
        }

        public async Task CreateShiftAsync(Shift shift)
        {
            var user = await _currentUser.GetUserAsync();
            shift.OrganizationId = user.OrganizationId ?? Guid.Empty;
            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
        }
    }
}
