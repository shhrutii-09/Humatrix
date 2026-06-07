using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgGeneratedDocumentsnew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployeeNotified",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "OrgGeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "OrgGeneratedDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmployeeNotified",
                table: "OrgGeneratedDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "OrgGeneratedDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAt",
                table: "OrgGeneratedDocuments",
                type: "datetime2",
                nullable: true);
        }
    }
}
