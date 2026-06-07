using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Data.Seeders;

public static class DocumentTypeSeeder
{
    public static async Task SeedDocumentTypesAsync(
        ApplicationDbContext ctx,
        Guid orgId)
    {
        // Unified array including your original types and the structural flags from Claude
        var defaults = new[]
        {
            // --- PERSONAL DOCUMENTS (Employee Uploaded) ---
            new { Name="Aadhaar Card",           Cat="Personal",     Mand=true,  Exp=false, EmpUp=true,  HrUp=false, IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },
            new { Name="PAN Card",               Cat="Personal",     Mand=true,  Exp=false, EmpUp=true,  HrUp=false, IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },
            new { Name="Resume",                 Cat="Personal",     Mand=true,  Exp=false, EmpUp=true,  HrUp=true,  IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },
            new { Name="Degree Certificate",     Cat="Personal",     Mand=false, Exp=false, EmpUp=true,  HrUp=false, IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },
            new { Name="Passport",               Cat="Personal",     Mand=false, Exp=true,  EmpUp=true,  HrUp=false, IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },
            new { Name="Bank Passbook / Cheque", Cat="Personal",     Mand=false, Exp=false, EmpUp=true,  HrUp=false, IsOrgGen=false, ReqVer=true,  Files=".pdf,.jpg,.jpeg,.png", Size=10 },

            // --- ORGANIZATION GENERATED DOCUMENTS (HR Issued) ---
            new { Name="Offer Letter",           Cat="Organization", Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf,.docx",           Size=20 },
            new { Name="Appointment Letter",     Cat="Organization", Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf,.docx",           Size=20 },
            new { Name="Experience Letter",      Cat="Organization", Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf,.docx",           Size=30 }, // Fixed typo or expanded
            new { Name="Relieving Letter",        Cat="Organization", Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf,.docx",           Size=20 },
            new { Name="Salary Slip",            Cat="Organization", Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf",                 Size=5  },
            new { Name="Warning Letter",           Cat="Compliance",   Mand=false, Exp=false, EmpUp=false, HrUp=true,  IsOrgGen=true,  ReqVer=false, Files=".pdf,.docx",           Size=10 }
        };

        foreach (var d in defaults)
        {
            // Check if this DocumentType already exists for the specific organization
            if (!await ctx.Set<DocumentType>()
                .AnyAsync(dt => dt.OrganizationId == orgId && dt.Name == d.Name))
            {
                ctx.Set<DocumentType>().Add(new DocumentType
                {
                    OrganizationId = orgId,
                    Name = d.Name,
                    Category = d.Cat,
                    IsMandatory = d.Mand,
                    TrackExpiry = d.Exp,
                    IsEmployeeUploadAllowed = d.EmpUp,
                    IsHRUploadAllowed = d.HrUp,

                    // Newly mapped configuration items from the updated array:
                    IsOrganizationGenerated = d.IsOrgGen,
                    RequiresVerification = d.ReqVer,
                    AllowedFileTypes = d.Files,
                    MaxFileSizeMB = d.Size
                });
            }
        }

        await ctx.SaveChangesAsync();
    }
}