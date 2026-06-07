using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class FixDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeDocuments_EmployeeId_DocumentTypeId",
                table: "EmployeeDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DocumentExpiryAlerts_DocumentId_DaysBeforeExpiry",
                table: "DocumentExpiryAlerts");

            migrationBuilder.AddColumn<bool>(
                name: "IsLatestVersion",
                table: "EmployeeDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "DocumentExpiryAlerts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeId_DocumentTypeId_IsLatestVersion",
                table: "EmployeeDocuments",
                columns: new[] { "EmployeeId", "DocumentTypeId", "IsLatestVersion" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExpiryAlerts_DocumentId_DaysBeforeExpiry",
                table: "DocumentExpiryAlerts",
                columns: new[] { "DocumentId", "DaysBeforeExpiry" },
                unique: true,
                filter: "[DocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExpiryAlerts_EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts",
                column: "EmployeeDocumentDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts",
                column: "EmployeeDocumentDocumentId",
                principalTable: "EmployeeDocuments",
                principalColumn: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentHistories_Employees_EmployeeId",
                table: "DocumentHistories",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentExpiryAlerts_EmployeeDocuments_EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentHistories_Employees_EmployeeId",
                table: "DocumentHistories");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDocuments_EmployeeId_DocumentTypeId_IsLatestVersion",
                table: "EmployeeDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DocumentExpiryAlerts_DocumentId_DaysBeforeExpiry",
                table: "DocumentExpiryAlerts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentExpiryAlerts_EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts");

            migrationBuilder.DropColumn(
                name: "IsLatestVersion",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "EmployeeDocumentDocumentId",
                table: "DocumentExpiryAlerts");

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "DocumentExpiryAlerts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeId_DocumentTypeId",
                table: "EmployeeDocuments",
                columns: new[] { "EmployeeId", "DocumentTypeId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExpiryAlerts_DocumentId_DaysBeforeExpiry",
                table: "DocumentExpiryAlerts",
                columns: new[] { "DocumentId", "DaysBeforeExpiry" },
                unique: true);
        }
    }
}
