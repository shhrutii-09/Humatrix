using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class HolidayService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public HolidayService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<List<Holiday>> GetAllAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            return await _context.Holidays
                .Where(h => h.OrganizationId == user.OrganizationId)
                .OrderBy(h => h.Date)
                .ToListAsync();
        }

        public async Task CreateAsync(Holiday holiday)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            holiday.OrganizationId = user.OrganizationId!.Value;

            var exists = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == holiday.OrganizationId &&
                h.Date.Date == holiday.Date.Date);

            if (exists)
                throw new Exception("Holiday already exists for this date");

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            //var holiday = await _context.Holidays.FindAsync(id);
            var user = await _currentUser.GetUserAsync();

            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => h.HolidayId == id &&
                                          h.OrganizationId == user.OrganizationId);
            if (holiday == null) return;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();
        }
    }
}
