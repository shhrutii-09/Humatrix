using Humatrix_HRMS.Infrastructure.Constants;

namespace Humatrix_HRMS.Helpers
{
    public static class AssetTransitionEngine
    {
        public static void Validate(
            string currentStatus,
            string newStatus,
            bool hasOpenAssignment)
        {
            if (currentStatus == AssetStatuses.Disposed)
            {
                throw new InvalidOperationException(
                    "Disposed assets cannot change status.");
            }

            if (newStatus == AssetStatuses.InRepair &&
                hasOpenAssignment)
            {
                throw new InvalidOperationException(
                    "Assigned assets must be returned before repair.");
            }

            if ((newStatus == AssetStatuses.Retired ||
                 newStatus == AssetStatuses.Disposed) &&
                 hasOpenAssignment)
            {
                throw new InvalidOperationException(
                    "Assigned assets must be returned first.");
            }

            if (newStatus == AssetStatuses.Available &&
                currentStatus != AssetStatuses.InRepair &&
                currentStatus != AssetStatuses.Lost)
            {
                throw new InvalidOperationException(
                    "Only Lost or InRepair assets can become Available.");
            }
        }
    }
}