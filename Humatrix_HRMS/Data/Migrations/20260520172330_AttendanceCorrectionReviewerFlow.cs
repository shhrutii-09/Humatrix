using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceCorrectionReviewerFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewLevel",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubmittedByRole",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "ReviewLevel",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedByRole",
                table: "AttendanceCorrectionRequests");
        }
    }
}
