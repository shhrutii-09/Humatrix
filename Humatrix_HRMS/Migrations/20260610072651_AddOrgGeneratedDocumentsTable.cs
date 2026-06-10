using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgGeneratedDocumentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_AspNetUsers_IssuedByUserId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_DocumentTypeId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgGeneratedDocuments_PreviousOrgDocumentId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_EmployeeId_DocumentTypeId_IsLatestVersion",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_IssuedByUserId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_DocumentTypeId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_IssuedAt",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentDate",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "OrgGeneratedDocuments",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "RevocationReason",
                table: "OrgGeneratedDocuments",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "PreviousOrgDocumentId",
                table: "OrgGeneratedDocuments",
                newName: "PreviousDocumentId");

            migrationBuilder.RenameColumn(
                name: "IssuedByUserId",
                table: "OrgGeneratedDocuments",
                newName: "GeneratedByUserId");

            migrationBuilder.RenameColumn(
                name: "IssuedByRole",
                table: "OrgGeneratedDocuments",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "IssuedAt",
                table: "OrgGeneratedDocuments",
                newName: "GeneratedAt");

            migrationBuilder.RenameColumn(
                name: "IsRevoked",
                table: "OrgGeneratedDocuments",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "DocumentTypeId",
                table: "OrgGeneratedDocuments",
                newName: "TemplateId");

            migrationBuilder.RenameColumn(
                name: "OrgDocumentId",
                table: "OrgGeneratedDocuments",
                newName: "DocumentId");

            migrationBuilder.RenameIndex(
                name: "IX_OrgGeneratedDocuments_PreviousOrgDocumentId",
                table: "OrgGeneratedDocuments",
                newName: "IX_OrgGeneratedDocuments_PreviousDocumentId");

            migrationBuilder.RenameIndex(
                name: "IX_OrgGeneratedDocuments_DocumentTypeId",
                table: "OrgGeneratedDocuments",
                newName: "IX_OrgGeneratedDocuments_TemplateId");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumber",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "OrgGeneratedDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByEmployeeId",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgmentRemarks",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentSnapshot",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentName",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GeneratedByRole",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OrgDocumentHistories",
                columns: table => new
                {
                    HistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerformedByRole = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDocumentHistories", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_OrgDocumentHistories_OrgGeneratedDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "OrgGeneratedDocuments",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_DocumentNumber",
                table: "OrgGeneratedDocuments",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_EmployeeId",
                table: "OrgGeneratedDocuments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_GeneratedAt",
                table: "OrgGeneratedDocuments",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_EmployeeId",
                table: "OrgGeneratedDocuments",
                columns: new[] { "OrganizationId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_Status",
                table: "OrgGeneratedDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDocumentHistories_DocumentId",
                table: "OrgDocumentHistories",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_TemplateId",
                table: "OrgGeneratedDocuments",
                column: "TemplateId",
                principalTable: "DocumentTypes",
                principalColumn: "DocumentTypeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgGeneratedDocuments_PreviousDocumentId",
                table: "OrgGeneratedDocuments",
                column: "PreviousDocumentId",
                principalTable: "OrgGeneratedDocuments",
                principalColumn: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_TemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgGeneratedDocuments_PreviousDocumentId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropTable(
                name: "OrgDocumentHistories");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_DocumentNumber",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_EmployeeId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_GeneratedAt",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_EmployeeId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_Status",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByEmployeeId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "AcknowledgmentRemarks",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "ContentSnapshot",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentName",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "GeneratedByRole",
                table: "OrgGeneratedDocuments");

            migrationBuilder.RenameColumn(
                name: "TemplateId",
                table: "OrgGeneratedDocuments",
                newName: "DocumentTypeId");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "OrgGeneratedDocuments",
                newName: "IssuedByRole");

            migrationBuilder.RenameColumn(
                name: "PreviousDocumentId",
                table: "OrgGeneratedDocuments",
                newName: "PreviousOrgDocumentId");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "OrgGeneratedDocuments",
                newName: "IsRevoked");

            migrationBuilder.RenameColumn(
                name: "GeneratedByUserId",
                table: "OrgGeneratedDocuments",
                newName: "IssuedByUserId");

            migrationBuilder.RenameColumn(
                name: "GeneratedAt",
                table: "OrgGeneratedDocuments",
                newName: "IssuedAt");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "OrgGeneratedDocuments",
                newName: "RevocationReason");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "OrgGeneratedDocuments",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "DocumentId",
                table: "OrgGeneratedDocuments",
                newName: "OrgDocumentId");

            migrationBuilder.RenameIndex(
                name: "IX_OrgGeneratedDocuments_TemplateId",
                table: "OrgGeneratedDocuments",
                newName: "IX_OrgGeneratedDocuments_DocumentTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_OrgGeneratedDocuments_PreviousDocumentId",
                table: "OrgGeneratedDocuments",
                newName: "IX_OrgGeneratedDocuments_PreviousOrgDocumentId");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumber",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DocumentDate",
                table: "OrgGeneratedDocuments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedByUserId",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_EmployeeId_DocumentTypeId_IsLatestVersion",
                table: "OrgGeneratedDocuments",
                columns: new[] { "EmployeeId", "DocumentTypeId", "IsLatestVersion" },
                filter: "[IsRevoked] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_IssuedByUserId",
                table: "OrgGeneratedDocuments",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_DocumentTypeId",
                table: "OrgGeneratedDocuments",
                columns: new[] { "OrganizationId", "DocumentTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_OrganizationId_IssuedAt",
                table: "OrgGeneratedDocuments",
                columns: new[] { "OrganizationId", "IssuedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_AspNetUsers_IssuedByUserId",
                table: "OrgGeneratedDocuments",
                column: "IssuedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_DocumentTypeId",
                table: "OrgGeneratedDocuments",
                column: "DocumentTypeId",
                principalTable: "DocumentTypes",
                principalColumn: "DocumentTypeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgGeneratedDocuments_PreviousOrgDocumentId",
                table: "OrgGeneratedDocuments",
                column: "PreviousOrgDocumentId",
                principalTable: "OrgGeneratedDocuments",
                principalColumn: "OrgDocumentId");
        }
    }
}
