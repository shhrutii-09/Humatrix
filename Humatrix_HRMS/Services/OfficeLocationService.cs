using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OfficeLocationService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public OfficeLocationService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // 🔹 Get current org location
        public async Task<OfficeLocation?> GetAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            return await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == user.OrganizationId);
        }

        // 🔹 Create or Update (single record per org)
        public async Task SaveAsync(OfficeLocationDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            //var orgId = user.OrganizationId
            //    ?? throw new Exception("No organization");

            var orgId = user.OrganizationId;

            if (orgId == Guid.Empty)
                throw new Exception("No organization");
            if (dto.Latitude < -90 || dto.Latitude > 90)
                throw new Exception("Invalid Latitude");

            if (dto.Longitude < -180 || dto.Longitude > 180)
                throw new Exception("Invalid Longitude");

            if (dto.RadiusInMeters <= 0)
                throw new Exception("Radius must be greater than 0");

            var existing = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId);

            if (existing == null)
            {
                // CREATE
                var location = new OfficeLocation
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    Latitude = dto.Latitude,
                    Longitude = dto.Longitude,
                    RadiusInMeters = dto.RadiusInMeters,
                    OrganizationId = orgId.Value
                };

                _context.OfficeLocations.Add(location);
            }
            else
            {
                // UPDATE
                existing.Name = dto.Name;
                existing.Latitude = dto.Latitude;
                existing.Longitude = dto.Longitude;
                existing.RadiusInMeters = dto.RadiusInMeters;
            }

            await _context.SaveChangesAsync();
        }
    }
}