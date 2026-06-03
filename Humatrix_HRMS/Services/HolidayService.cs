using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Humatrix_HRMS.Services
{
    public class HolidayService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly IHttpClientFactory _httpClientFactory;

        // TODO: Apni Calendarific API Key yahan replace karein
        private const string ApiKey = "Je8ikyrl6pkDRmGOdIk3rZS2kMgwaoTE";

        public HolidayService(ApplicationDbContext context, CurrentUserService currentUser, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _currentUser = currentUser;
            _httpClientFactory = httpClientFactory;
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
            var user = await _currentUser.GetUserAsync();

            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => h.HolidayId == id &&
                                          h.OrganizationId == user.OrganizationId);
            if (holiday == null) return;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Holiday holiday)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.HolidayId == holiday.HolidayId &&
                                          h.OrganizationId == user.OrganizationId);

            if (existing == null) throw new Exception("Holiday not found");

            existing.Name = holiday.Name;
            existing.Date = holiday.Date;
            existing.IsOptional = holiday.IsOptional;

            await _context.SaveChangesAsync();
        }

        public async Task<int> ImportPublicHolidaysAsync(int year, string countryCode = "IN")
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            Guid userOrgId = user.OrganizationId!.Value;

            // Calendarific API Endpoint URL structure
            string apiUrl = $"https://calendarific.com/api/v2/holidays?api_key={ApiKey}&country={countryCode}&year={year}&type=national";

            CalendarificResponse? apiResponse;
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Humatrix-HRMS-App/1.0");

                apiResponse = await client.GetFromJsonAsync<CalendarificResponse>(apiUrl);
            }
            catch (Exception ex)
            {
                throw new Exception($"Holiday API sync failed: {ex.Message}");
            }

            if (apiResponse?.Response?.Holidays == null || apiResponse.Response.Holidays.Count == 0)
                return 0;

            var existingHolidayDates = await _context.Holidays
                .Where(h => h.OrganizationId == userOrgId && h.Date.Year == year)
                .Select(h => h.Date.Date)
                .ToListAsync();

            int importedCount = 0;

            foreach (var extHoliday in apiResponse.Response.Holidays)
            {
                // Calendarific string date format (YYYY-MM-DD) ko parse karna
                if (DateTime.TryParse(extHoliday.Date?.Iso, out DateTime holidayDate))
                {
                    if (!existingHolidayDates.Contains(holidayDate.Date))
                    {
                        var newHoliday = new Holiday
                        {
                            HolidayId = Guid.NewGuid(),
                            Name = extHoliday.Name ?? "Public Holiday",
                            Date = holidayDate.Date,
                            // Calendarific primary types hamesha mandatory public holidays hote hain
                            IsOptional = false,
                            OrganizationId = userOrgId
                        };

                        _context.Holidays.Add(newHoliday);
                        // Loop ke andar duplicate avoid karne ke liye local tracking array mein add karein
                        existingHolidayDates.Add(holidayDate.Date);
                        importedCount++;
                    }
                }
            }

            if (importedCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return importedCount;
        }
    }

    // --- New DTO Classes for Calendarific JSON Schema JSON Mapping ---
    internal class CalendarificResponse
    {
        [JsonPropertyName("response")]
        public ResponseContent? Response { get; set; }
    }

    internal class ResponseContent
    {
        [JsonPropertyName("holidays")]
        public List<CalendarificHolidayDto>? Holidays { get; set; }
    }

    internal class CalendarificHolidayDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("date")]
        public HolidayDateContainer? Date { get; set; }
    }

    internal class HolidayDateContainer
    {
        [JsonPropertyName("iso")]
        public string? Iso { get; set; } // Contains date string like "2026-01-26"
    }
}