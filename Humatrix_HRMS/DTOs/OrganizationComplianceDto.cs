namespace Humatrix_HRMS.DTOs.Documents;

public class OrganizationComplianceDto
{
    public int TotalEmployees { get; set; }

    public int FullyCompliantEmployees { get; set; }

    public int NonCompliantEmployees { get; set; }

    public double CompliancePercentage { get; set; }
}