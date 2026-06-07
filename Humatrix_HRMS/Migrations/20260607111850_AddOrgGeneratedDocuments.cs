using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgGeneratedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "EmployeeDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "EmployeeDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrganizationGenerated",
                table: "DocumentTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OrgGeneratedDocuments",
                columns: table => new
                {
                    OrgDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IssuedByRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PreviousOrgDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsLatestVersion = table.Column<bool>(type: "bit", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmployeeNotified = table.Column<bool>(type: "bit", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgGeneratedDocuments", x => x.OrgDocumentId);
                    table.ForeignKey(
                        name: "FK_OrgGeneratedDocuments_AspNetUsers_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrgGeneratedDocuments_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "DocumentTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrgGeneratedDocuments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrgGeneratedDocuments_OrgGeneratedDocuments_PreviousOrgDocumentId",
                        column: x => x.PreviousOrgDocumentId,
                        principalTable: "OrgGeneratedDocuments",
                        principalColumn: "OrgDocumentId");
                    table.ForeignKey(
                        name: "FK_OrgGeneratedDocuments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_DocumentTypeId",
                table: "OrgGeneratedDocuments",
                column: "DocumentTypeId");

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

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_PreviousOrgDocumentId",
                table: "OrgGeneratedDocuments",
                column: "PreviousOrgDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "IsOrganizationGenerated",
                table: "DocumentTypes");
        }
    }
}
