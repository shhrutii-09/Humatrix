using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Data.SeedData;

public static class OrgDocumentTemplateSeeder
{
    public static async Task SeedTemplatesForAllOrganizationsAsync(ApplicationDbContext db)
    {
        var organizations = await db.Organizations.ToListAsync();

        foreach (var org in organizations)
        {
            await SeedTemplatesForOrganizationAsync(db, org.OrganizationId);
        }
    }

    public static async Task SeedTemplatesForOrganizationAsync(ApplicationDbContext db, Guid organizationId)
    {
        var existingCount = await db.OrgDocumentTemplates
            .CountAsync(x => x.OrganizationId == organizationId);

        if (existingCount > 0)
        {
            Console.WriteLine($"Organization {organizationId} already has {existingCount} templates.");
            return;
        }

        var orgAdmin = await (from u in db.Users
                              join ur in db.UserRoles on u.Id equals ur.UserId
                              join r in db.Roles on ur.RoleId equals r.Id
                              where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
                              select u).FirstOrDefaultAsync();

        string createdByUserId = orgAdmin?.Id ?? "SYSTEM";
        var now = DateTime.UtcNow;

        var templates = new List<OrgDocumentTemplate>
        {
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Offer Letter",
                Category = "Onboarding",
                TemplateContent = GetOfferLetterTemplate(),
                IsActive = true,
                DisplayOrder = 1,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Appointment Letter",
                Category = "Onboarding",
                TemplateContent = GetAppointmentLetterTemplate(),
                IsActive = true,
                DisplayOrder = 2,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Warning Letter",
                Category = "Disciplinary",
                TemplateContent = GetWarningLetterTemplate(),
                IsActive = true,
                DisplayOrder = 10,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Appreciation Letter",
                Category = "Recognition",
                TemplateContent = GetAppreciationLetterTemplate(),
                IsActive = true,
                DisplayOrder = 20,
                RequiresAcknowledgment = false,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Experience Letter",
                Category = "Offboarding",
                TemplateContent = GetExperienceLetterTemplate(),
                IsActive = true,
                DisplayOrder = 30,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Relieving Letter",
                Category = "Offboarding",
                TemplateContent = GetRelievingLetterTemplate(),
                IsActive = true,
                DisplayOrder = 31,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Employment Proof Letter",
                Category = "Verification",
                TemplateContent = GetEmploymentProofTemplate(),
                IsActive = true,
                DisplayOrder = 40,
                RequiresAcknowledgment = false,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Promotion Letter",
                Category = "Recognition",
                TemplateContent = GetPromotionLetterTemplate(),
                IsActive = true,
                DisplayOrder = 22,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Transfer Letter",
                Category = "Operational",
                TemplateContent = GetTransferLetterTemplate(),
                IsActive = true,
                DisplayOrder = 50,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "No Dues Certificate",
                Category = "Offboarding",
                TemplateContent = GetNoDuesCertificateTemplate(),
                IsActive = true,
                DisplayOrder = 32,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Show Cause Notice",
                Category = "Disciplinary",
                TemplateContent = GetShowCauseNoticeTemplate(),
                IsActive = true,
                DisplayOrder = 11,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Confirmation Letter",
                Category = "Onboarding",
                TemplateContent = GetConfirmationLetterTemplate(),
                IsActive = true,
                DisplayOrder = 3,
                RequiresAcknowledgment = true,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            },
            new OrgDocumentTemplate
            {
                TemplateId = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = "Salary Certificate",
                Category = "Verification",
                TemplateContent = GetSalaryCertificateTemplate(),
                IsActive = true,
                DisplayOrder = 41,
                RequiresAcknowledgment = false,
                CreatedAt = now,
                CreatedByUserId = createdByUserId
            }
        };

        await db.OrgDocumentTemplates.AddRangeAsync(templates);
        await db.SaveChangesAsync();

        Console.WriteLine($"✅ Added {templates.Count} organization document templates for organization {organizationId}");
    }

    // ==================== TEMPLATE CONTENT METHODS ====================
    private static string GetOfferLetterTemplate()
    {
        return @"<h2>Offer of Employment</h2>
<p>Dear {{FirstName}},</p>
<p>We are pleased to offer you the position of <strong>{{Designation}}</strong> in the <strong>{{Department}}</strong> department at {{OrganizationName}}.</p>
<p>Joining Date: {{JoiningDate}}</p>
<p>Sincerely,<br/>{{GeneratedBy}}<br/>{{OrganizationName}}</p>";
    }

    private static string GetAppointmentLetterTemplate()
    {
        return @"<h2>Appointment Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>This confirms your appointment as <strong>{{Designation}}</strong> in the <strong>{{Department}}</strong> department effective {{JoiningDate}}.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetConfirmationLetterTemplate()
    {
        return @"<h2>Confirmation Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>Employment confirmed effective {{ConfirmationDate}}.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetWarningLetterTemplate()
    {
        return @"<h2>Warning Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>Warning regarding: {{IssueType}}</p>
<p>Details: {{IssueDetails}}</p>
<p>Issued by: {{GeneratedBy}}</p>";
    }

    private static string GetShowCauseNoticeTemplate()
    {
        return @"<h2>Show Cause Notice</h2>
<p>Dear {{EmployeeName}},</p>
<p>Please explain: {{Allegations}}</p>
<p>Response within {{ResponseDays}} days.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetAppreciationLetterTemplate()
    {
        return @"<h2>Appreciation Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>Thank you for {{Achievement}}.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetExperienceLetterTemplate()
    {
        return @"<h2>Experience Certificate</h2>
<p>{{EmployeeName}} worked from {{JoiningDate}} to {{LastWorkingDay}} as {{Designation}}.</p>
<p>Date: {{CurrentDate}}<br/>{{GeneratedBy}}</p>";
    }

    private static string GetRelievingLetterTemplate()
    {
        return @"<h2>Relieving Letter</h2>
<p>{{EmployeeName}} is relieved from duties effective {{LastWorkingDay}}.</p>
<p>Date: {{CurrentDate}}<br/>{{GeneratedBy}}</p>";
    }

    private static string GetEmploymentProofTemplate()
    {
        return @"<h2>Employment Proof</h2>
<p>{{EmployeeName}} is employed as {{Designation}} since {{JoiningDate}}.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetSalaryCertificateTemplate()
    {
        return @"<h2>Salary Certificate</h2>
<p>{{EmployeeName}} earns {{MonthlySalary}} per month.</p>
<p>Date: {{CurrentDate}}<br/>{{GeneratedBy}}</p>";
    }

    private static string GetPromotionLetterTemplate()
    {
        return @"<h2>Promotion Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>Promoted from {{OldDesignation}} to {{NewDesignation}} effective {{PromotionDate}}.</p>
<p>Congratulations!<br/>{{GeneratedBy}}</p>";
    }

    private static string GetTransferLetterTemplate()
    {
        return @"<h2>Transfer Letter</h2>
<p>Dear {{EmployeeName}},</p>
<p>Transferred from {{OldDepartment}} to {{NewDepartment}} effective {{TransferDate}}.</p>
<p>Sincerely,<br/>{{GeneratedBy}}</p>";
    }

    private static string GetNoDuesCertificateTemplate()
    {
        return @"<h2>No Dues Certificate</h2>
<p>{{EmployeeName}} has cleared all dues.</p>
<p>Date: {{CurrentDate}}<br/>{{GeneratedBy}}</p>";
    }
}