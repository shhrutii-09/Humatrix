using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Humatrix_HRMS.Controllers
{
    [Authorize]
    public class TaskController : Controller
    {
        private readonly TaskService _service;

        public TaskController(TaskService service)
        {
            _service = service;
        }

        public async Task<IActionResult> MyTasks()
        {
            var data = await _service.GetMyTasksAsync();
            return View(data);
        }

        public async Task<IActionResult> AllTasks()
        {
            var data = await _service.GetAllTasksAsync();
            return View(data);
        }

        [HttpPost]
        public async Task<IActionResult> Assign(CreateTaskDto dto)
        {
            await _service.AssignTaskAsync(dto);
            return RedirectToAction("AllTasks");
        }

        [HttpPost]
        public async Task<IActionResult> Update(UpdateTaskDto dto)
        {
            await _service.UpdateTaskAsync(dto);
            return RedirectToAction("MyTasks");
        }

        public async Task<IActionResult> Complete(Guid id)
        {
            await _service.MarkCompleteAsync(id);
            return RedirectToAction("MyTasks");
        }
    }
}