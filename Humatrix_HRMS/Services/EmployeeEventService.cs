namespace Humatrix_HRMS.Services;

public class EmployeeEventService
{
    // This event will be triggered whenever an employee status is updated
    public event Action? OnEmployeeChanged;

    public void NotifyStateChanged() => OnEmployeeChanged?.Invoke();
}