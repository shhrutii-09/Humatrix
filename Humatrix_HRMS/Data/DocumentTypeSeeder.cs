using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Data.Seeders;

public static class DocumentTypeSeeder
{
    public static async Task SeedDocumentTypesAsync(
        ApplicationDbContext ctx,
        Guid orgId)
    {
        var defaults = new[]
            {
        new { Name="Aadhaar Card",           Cat="Personal",      Mand=true,  Exp=false, EmpUp=true,  HrUp=false },
        new { Name="PAN Card",               Cat="Personal",      Mand=true,  Exp=false, EmpUp=true,  HrUp=false },
        new { Name="Resume",                 Cat="Personal",      Mand=true,  Exp=false, EmpUp=true,  HrUp=true  },
        new { Name="Degree Certificate",     Cat="Personal",      Mand=false, Exp=false, EmpUp=true,  HrUp=false },
        new { Name="Passport",               Cat="Personal",      Mand=false, Exp=true,  EmpUp=true,  HrUp=false },
        new { Name="Offer Letter",           Cat="Organization",  Mand=false, Exp=false, EmpUp=false, HrUp=true  },
        new { Name="Appointment Letter",     Cat="Organization",  Mand=false, Exp=false, EmpUp=false, HrUp=true  },
        new { Name="Experience Letter",      Cat="Organization",  Mand=false, Exp=false, EmpUp=false, HrUp=true  },
        new { Name="Relieving Letter",       Cat="Organization",  Mand=false, Exp=false, EmpUp=false, HrUp=true  },
        new { Name="Bank Passbook / Cheque", Cat="Personal",      Mand=false, Exp=false, EmpUp=true,  HrUp=false },
    };

        foreach (var d in defaults)
        {
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
                    RequiresVerification = true,
                    AllowedFileTypes = ".pdf,.jpg,.jpeg,.png",
                    MaxFileSizeMB = 10
                });
            }
        }

        await ctx.SaveChangesAsync();
    }
}