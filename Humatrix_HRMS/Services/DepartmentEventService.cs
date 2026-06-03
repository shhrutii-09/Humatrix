namespace Humatrix_HRMS.Services
{
    public class DepartmentEventService
    {
        public event Action? OnDepartmentChanged;
        public void NotifyStateChanged() => OnDepartmentChanged?.Invoke();
    }
}