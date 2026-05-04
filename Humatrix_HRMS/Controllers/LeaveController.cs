using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Controllers
{
    [Authorize]
    public class LeaveController : Controller
    {
        private readonly LeaveService _leaveService;
        private readonly ApplicationDbContext _context;

        public LeaveController(LeaveService leaveService, ApplicationDbContext context)
        {
            _leaveService = leaveService;
            _context = context;
        }

        // 🔹 Dashboard
        public async Task<IActionResult> Index()
        {
            var balances = await _leaveService.GetMyBalancesAsync();
            return View(balances);
        }

        // 🔹 Apply Leave (GET)
        public async Task<IActionResult> Apply()
        {
            var leaveTypes = await _context.LeaveTypes
                .Where(x => x.IsActive)
                .ToListAsync();

            ViewBag.LeaveTypes = leaveTypes;
            return View();
        }

        // 🔹 Apply Leave (POST)
        [HttpPost]
        public async Task<IActionResult> Apply(LeaveRequest model)
        {
            try
            {
                await _leaveService.ApplyLeaveAsync(model);
                return RedirectToAction("MyLeaves");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);

                ViewBag.LeaveTypes = await _context.LeaveTypes
                    .Where(x => x.IsActive)
                    .ToListAsync();

                return View(model);
            }
        }

        // 🔹 My Leaves
        public async Task<IActionResult> MyLeaves()
        {
            var leaves = await _leaveService.GetMyLeavesAsync();
            return View(leaves);
        }

        // 🔹 Cancel
        public async Task<IActionResult> Cancel(Guid id)
        {
            await _leaveService.CancelRequestAsync(id);
            return RedirectToAction("MyLeaves");
        }
    }
}
