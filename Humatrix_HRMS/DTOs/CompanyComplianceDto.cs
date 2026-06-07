using System;
using System.Collections.Generic;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.DTOs.Documents
{
    public class CompanyComplianceDto
    {
        public int OverallCompliancePercentage { get; set; }
        public int TotalActiveEmployees { get; set; }
        public int FullyCompliantEmployeesCount { get; set; }
        public int NonCompliantEmployeesCount { get; set; }
        public int TotalPendingVerificationsCount { get; set; }
        public int TotalExpiredDocumentsCount { get; set; }
        public bool IsConfigured { get; set; }

        public List<DepartmentComplianceDto> DepartmentStats { get; set; } = new();
        public List<EmployeeComplianceSummaryDto> NonCompliantEmployees { get; set; } = new();
        public List<PendingVerificationSummaryDto> PendingVerifications { get; set; } = new();
        public List<ExpiringDocumentSummaryDto> ExpiringDocuments { get; set; } = new();
    }

    public class DepartmentComplianceDto
    {
        public string DepartmentName { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int CompliantEmployees { get; set; }
        public int CompliancePercentage { get; set; }
    }

    public class EmployeeComplianceSummaryDto
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public int MissingMandatoryCount { get; set; }
        public List<string> MissingDocumentTypeNames { get; set; } = new();
    }

    public class PendingVerificationSummaryDto
    {
        public Guid DocumentId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string DocumentTypeName { get; set; } = string.Empty;
        public DateTime? UploadedAt { get; set; }
    }

    public class ExpiringDocumentSummaryDto
    {
        public Guid DocumentId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string DocumentTypeName { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
    }
}