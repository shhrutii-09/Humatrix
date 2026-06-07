using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class FixDocumentWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_DocumentId",
                table: "DocumentExpiryAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentHistories_EmployeeDocuments_DocumentId",
                table: "DocumentHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_DocumentId",
                table: "DocumentExpiryAlerts",
                column: "DocumentId",
                principalTable: "EmployeeDocuments",
                principalColumn: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentHistories_EmployeeDocuments_DocumentId",
                table: "DocumentHistories",
                column: "DocumentId",
                principalTable: "EmployeeDocuments",
                principalColumn: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_DocumentId",
                table: "DocumentExpiryAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentHistories_EmployeeDocuments_DocumentId",
                table: "DocumentHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_DocumentId",
                table: "DocumentExpiryAlerts",
                column: "DocumentId",
                principalTable: "EmployeeDocuments",
                principalColumn: "DocumentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentHistories_EmployeeDocuments_DocumentId",
                table: "DocumentHistories",
                column: "DocumentId",
                principalTable: "EmployeeDocuments",
                principalColumn: "DocumentId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
