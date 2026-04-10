using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveEmployeeRelationnnnn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "Attendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Attendances",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Attendances",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OvertimeHours",
                table: "Attendances",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TotalHours",
                table: "Attendances",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "Attendances",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "OvertimeHours",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "TotalHours",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Attendances");
        }
    }
}
