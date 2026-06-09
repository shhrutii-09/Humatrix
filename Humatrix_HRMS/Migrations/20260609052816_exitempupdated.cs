using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class exitempupdated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "EmployeeExits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "EmployeeExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByEmployeeId",
                table: "EmployeeExits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_CancelledByEmployeeId",
                table: "EmployeeExits",
                column: "CancelledByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeExits_Employees_CancelledByEmployeeId",
                table: "EmployeeExits",
                column: "CancelledByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeExits_Employees_CancelledByEmployeeId",
                table: "EmployeeExits");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeExits_CancelledByEmployeeId",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "CancelledByEmployeeId",
                table: "EmployeeExits");
        }
    }
}
