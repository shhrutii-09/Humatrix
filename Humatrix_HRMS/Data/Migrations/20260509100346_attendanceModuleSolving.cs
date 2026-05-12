using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class attendanceModuleSolving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HRRemarks",
                table: "OvertimeRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "OvertimeRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HrNote",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalCheckOut",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "AttendanceCorrectionRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HRRemarks",
                table: "OvertimeRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "OvertimeRequests");

            migrationBuilder.DropColumn(
                name: "HrNote",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "OriginalCheckIn",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "OriginalCheckOut",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");
        }
    }
}
