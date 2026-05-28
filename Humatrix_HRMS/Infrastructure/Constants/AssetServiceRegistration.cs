// Infrastructure/Extensions/AssetServiceRegistration.cs
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Humatrix_HRMS.Infrastructure.Extensions
{
    /// <summary>
    /// Call services.AddAssetManagementServices() from Program.cs / Startup.cs
    /// to register the complete asset management service layer.
    /// </summary>
    public static class AssetServiceRegistration
    {
        public static IServiceCollection AddAssetManagementServices(
            this IServiceCollection services)
        {
            // Core asset services (existing)
            services.AddScoped<AssetService>();
            services.AddScoped<AssetAssignmentService>();
            services.AddScoped<AssetRequestService>();

            // New services
            services.AddScoped<HrProcurementRequestService>();
            services.AddScoped<EmployeeAssetRequestService>();
            services.AddScoped<AssetAnalyticsService>();

            return services;
        }
    }
}
