    using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgDocumentTemplateTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_TemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.AddColumn<Guid>(
                name: "OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrgDocumentTemplates",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TemplateContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlaceholderSchema = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    RequiresAcknowledgment = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDocumentTemplates", x => x.TemplateId);
                    table.ForeignKey(
                        name: "FK_OrgDocumentTemplates_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrgDocumentTemplates_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgGeneratedDocuments_OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments",
                column: "OrgDocumentTemplateTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDocumentTemplates_CreatedByUserId",
                table: "OrgDocumentTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDocumentTemplates_OrganizationId",
                table: "OrgDocumentTemplates",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgDocumentTemplates_OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments",
                column: "OrgDocumentTemplateTemplateId",
                principalTable: "OrgDocumentTemplates",
                principalColumn: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgDocumentTemplates_TemplateId",
                table: "OrgGeneratedDocuments",
                column: "TemplateId",
                principalTable: "OrgDocumentTemplates",
                principalColumn: "TemplateId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgDocumentTemplates_OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrgGeneratedDocuments_OrgDocumentTemplates_TemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropTable(
                name: "OrgDocumentTemplates");

            migrationBuilder.DropIndex(
                name: "IX_OrgGeneratedDocuments_OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "OrgDocumentTemplateTemplateId",
                table: "OrgGeneratedDocuments");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgGeneratedDocuments_DocumentTypes_TemplateId",
                table: "OrgGeneratedDocuments",
                column: "TemplateId",
                principalTable: "DocumentTypes",
                principalColumn: "DocumentTypeId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
