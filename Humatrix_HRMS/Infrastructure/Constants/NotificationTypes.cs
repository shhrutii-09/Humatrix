// Infrastructure/Constants/NotificationTypes.cs
namespace Humatrix_HRMS.Infrastructure.Constants
{
    public static class NotificationTypes
    {
        // Leave
        public const string LeaveApplied = "leave.applied";
        public const string LeaveApproved = "leave.approved";
        public const string LeaveRejected = "leave.rejected";
        public const string LeaveCancelled = "leave.cancelled";

        // WFH
        public const string WfhApplied = "wfh.applied";
        public const string WfhApproved = "wfh.approved";
        public const string WfhRejected = "wfh.rejected";

        // Overtime
        public const string OtApplied = "overtime.applied";
        public const string OtApproved = "overtime.approved";
        public const string OtRejected = "overtime.rejected";

        // Attendance
        public const string AttendanceCorrectionApplied = "attendance.correction.applied";
        public const string AttendanceCorrectionApproved = "attendance.correction.approved";
        public const string AttendanceCorrectionRejected = "attendance.correction.rejected";

        // Employee
        public const string EmployeeCreated = "employee.created";
        public const string EmployeeUpdated = "employee.updated";

        // Tasks (future)
        public const string TaskAssigned = "task.assigned";
        public const string TaskCompleted = "task.completed";

        // Payroll (future)
        public const string PayslipGenerated = "payroll.payslip.generated";

        // Assets (future)
        public const string AssetAssigned = "asset.assigned";
        public const string AssetReturned = "asset.returned";

        // HelpDesk (future)
        public const string TicketRaised = "helpdesk.ticket.raised";
        public const string TicketResolved = "helpdesk.ticket.resolved";
    }

    public static class NotificationPriorities
    {
        public const string Low = "Low";
        public const string Normal = "Normal";
        public const string High = "High";
        public const string Urgent = "Urgent";
    }

    public static class ReferenceTypes
    {
        public const string LeaveRequest = "LeaveRequest";
        public const string WorkFromHomeRequest = "WorkFromHomeRequest";
        public const string OvertimeRequest = "OvertimeRequest";
        public const string AttendanceCorrectionRequest = "AttendanceCorrectionRequest";
        public const string Employee = "Employee";
        public const string Task = "Task";
        public const string Payslip = "Payslip";
        public const string Asset = "Asset";
        public const string Ticket = "Ticket";
    }
}