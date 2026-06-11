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
            },


            // Add these to the templates list in SeedTemplatesForOrganizationAsync
new OrgDocumentTemplate
{
    TemplateId = Guid.NewGuid(),
    OrganizationId = organizationId,
    Name = "Leave Sanction Letter",
    Category = "Leave",
    TemplateContent = GetLeaveSanctionLetterTemplate(),
    IsActive = true,
    DisplayOrder = 60,
    RequiresAcknowledgment = false,
    CreatedAt = now,
    CreatedByUserId = createdByUserId
},
new OrgDocumentTemplate
{
    TemplateId = Guid.NewGuid(),
    OrganizationId = organizationId,
    Name = "Birthday Card",
    Category = "Recognition",
    TemplateContent = GetBirthdayCardTemplate(),
    IsActive = true,
    DisplayOrder = 61,
    RequiresAcknowledgment = false,
    CreatedAt = now,
    CreatedByUserId = createdByUserId
},
new OrgDocumentTemplate
{
    TemplateId = Guid.NewGuid(),
    OrganizationId = organizationId,
    Name = "Anniversary Certificate",
    Category = "Recognition",
    TemplateContent = GetAnniversaryCertificateTemplate(),
    IsActive = true,
    DisplayOrder = 62,
    RequiresAcknowledgment = false,
    CreatedAt = now,
    CreatedByUserId = createdByUserId
},
new OrgDocumentTemplate
{
    TemplateId = Guid.NewGuid(),
    OrganizationId = organizationId,
    Name = "Task Completion Certificate",
    Category = "Recognition",
    TemplateContent = GetTaskCompletionCertificateTemplate(),
    IsActive = true,
    DisplayOrder = 63,
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



    private static string GetLeaveSanctionLetterTemplate()
    {
        return @"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
    <div style='text-align: center; border-bottom: 2px solid #4a6741; margin-bottom: 20px;'>
        <h2>Leave Sanction Letter</h2>
    </div>
    
    <p>Dear {{EmployeeName}},</p>
    
    <p>Your request for <strong>{{LeaveType}}</strong> leave has been <strong style='color: green;'>approved</strong>.</p>
    
    <div style='background: #f8f9fa; padding: 15px; margin: 15px 0; border-radius: 8px;'>
        <p><strong>📅 Leave Period:</strong> {{FromDate}} to {{ToDate}}</p>
        <p><strong>📊 Total Days:</strong> {{TotalDays}} day(s)</p>
        <p><strong>📝 Reason:</strong> {{Reason}}</p>
    </div>
    
    <p>Please ensure all pending work is handed over before proceeding on leave.</p>
    
    <p>Wishing you a refreshing break!</p>
    
    <div style='margin-top: 30px; padding-top: 15px; border-top: 1px solid #ddd;'>
        <p>Best regards,<br/>HR Department</p>
    </div>
</div>";
    }

    private static string GetBirthdayCardTemplate()
    {
        return @"
<div style='font-family: Georgia, serif; max-width: 500px; margin: 0 auto; padding: 30px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 15px; text-align: center; color: white;'>
    <h1 style='font-size: 48px; margin: 0;'>🎂</h1>
    <h2>Happy Birthday!</h2>
    <h3>{{EmployeeName}}</h3>
    <p style='margin: 20px 0;'>Wishing you a fantastic day filled with joy and celebration!</p>
    <p>From,<br/>The HR Team</p>
</div>";
    }

    private static string GetAnniversaryCertificateTemplate()
    {
        return @"
<div style='font-family: Georgia, serif; max-width: 600px; margin: 0 auto; padding: 30px; background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%); border-radius: 15px; text-align: center;'>
    <h1 style='color: #2c3e50;'>🎉 Service Recognition 🎉</h1>
    <h2>{{Years}} Year{{Years > 1 ? 's' : ''}} of Excellence</h2>
    <div style='margin: 20px 0; padding: 15px; background: white; border-radius: 10px;'>
        <p><strong>{{EmployeeName}}</strong></p>
        <p>{{Designation}} | {{Department}}</p>
    </div>
    <p>Thank you for your dedication and contribution to our organization.</p>
    <p>Presented on {{CurrentDate}}</p>
</div>";
    }

    private static string GetTaskCompletionCertificateTemplate()
    {
        return @"
<div style='font-family: Arial, sans-serif; max-width: 550px; margin: 0 auto; padding: 25px; border: 2px solid #4a6741; border-radius: 10px;'>
    <div style='text-align: center;'>
        <h2>Task Completion Certificate</h2>
        <p>This certificate is presented to</p>
        <h3>{{EmployeeName}}</h3>
        <p>for successfully completing</p>
        <div style='background: #f0f8ff; padding: 10px; margin: 15px 0;'>
            <strong>{{TaskTitle}}</strong>
        </div>
        <p>on {{CompletionDate}}</p>
        <hr/>
        <p>Thank you for your contribution!</p>
    </div>
</div>";
    }
}
