namespace Humatrix_HRMS.DTOs.Exit;

public class ExitDashboardCardDto
{
    public int Pending { get; set; }

    public int Approved { get; set; }

    public int Clearance { get; set; }

    public int Completed { get; set; }

    public int UpcomingLastWorkingDays { get; set; }
}