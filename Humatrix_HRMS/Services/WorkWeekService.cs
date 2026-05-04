using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class WorkWeekService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public WorkWeekService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // GET current org workweek
        public async Task<WorkWeek> GetAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId!.Value;

            var workWeek = await _context.WorkWeeks
                .FirstOrDefaultAsync(w => w.OrganizationId == orgId);

            if (workWeek != null)
                return workWeek;

            // ✅ AUTO CREATE DEFAULT (Mon–Fri working)
            workWeek = new WorkWeek
            {
                WorkWeekId = Guid.NewGuid(),
                OrganizationId = orgId,
                IsMondayWorking = true,
                IsTuesdayWorking = true,
                IsWednesdayWorking = true,
                IsThursdayWorking = true,
                IsFridayWorking = true,
                IsSaturdayWorking = false,
                IsSundayWorking = false
            };

            _context.WorkWeeks.Add(workWeek);
            await _context.SaveChangesAsync();

            return workWeek;
        }

        // CREATE or UPDATE
        public async Task SaveAsync(WorkWeek model)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId!.Value;

            // ✅ VALIDATION (CRITICAL)
            if (!model.IsMondayWorking &&
                !model.IsTuesdayWorking &&
                !model.IsWednesdayWorking &&
                !model.IsThursdayWorking &&
                !model.IsFridayWorking &&
                !model.IsSaturdayWorking &&
                !model.IsSundayWorking)
            {
                throw new Exception("At least one working day must be selected");
            }

            var existing = await _context.WorkWeeks
                .FirstOrDefaultAsync(w => w.OrganizationId == orgId);

            if (existing == null)
            {
                model.WorkWeekId = Guid.NewGuid();
                model.OrganizationId = orgId;

                _context.WorkWeeks.Add(model);
            }
            else
            {
                existing.IsMondayWorking = model.IsMondayWorking;
                existing.IsTuesdayWorking = model.IsTuesdayWorking;
                existing.IsWednesdayWorking = model.IsWednesdayWorking;
                existing.IsThursdayWorking = model.IsThursdayWorking;
                existing.IsFridayWorking = model.IsFridayWorking;
                existing.IsSaturdayWorking = model.IsSaturdayWorking;
                existing.IsSundayWorking = model.IsSundayWorking;
            }

            await _context.SaveChangesAsync();
        }
    }
}