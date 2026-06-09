using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class exitempupdatedagain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExitType",
                table: "EmployeeExits",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TerminatedByEmployeeId",
                table: "EmployeeExits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TerminationDate",
                table: "EmployeeExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                table: "EmployeeExits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_TerminatedByEmployeeId",
                table: "EmployeeExits",
                column: "TerminatedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeExits_Employees_TerminatedByEmployeeId",
                table: "EmployeeExits",
                column: "TerminatedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeExits_Employees_TerminatedByEmployeeId",
                table: "EmployeeExits");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeExits_TerminatedByEmployeeId",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "ExitType",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "TerminatedByEmployeeId",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "TerminationReason",
                table: "EmployeeExits");
        }
    }
}
