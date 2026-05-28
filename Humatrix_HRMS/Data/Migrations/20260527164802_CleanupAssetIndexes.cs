using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class CleanupAssetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetRequests_OrganizationId_Status",
                table: "AssetRequests");

            migrationBuilder.AddColumn<string>(
                name: "RequestCategory",
                table: "AssetRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AssetRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AssetAssignments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestCategory",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AssetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRequests_OrganizationId_Status",
                table: "AssetRequests",
                columns: new[] { "OrganizationId", "Status" });
        }
    }
}
