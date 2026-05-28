using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddQuantityToAssetRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AssetRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AssetRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "AssetRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByEmployeeId",
                table: "AssetRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetRequests_RequestedByEmployeeId",
                table: "AssetRequests",
                column: "RequestedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetRequests_Employees_RequestedByEmployeeId",
                table: "AssetRequests",
                column: "RequestedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetRequests_Employees_RequestedByEmployeeId",
                table: "AssetRequests");

            migrationBuilder.DropIndex(
                name: "IX_AssetRequests_RequestedByEmployeeId",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "AssetRequests");

            migrationBuilder.DropColumn(
                name: "RequestedByEmployeeId",
                table: "AssetRequests");
        }
    }
}
