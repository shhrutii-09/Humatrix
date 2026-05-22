using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionWorkflowV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByRole",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewLevel",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsPayrollPeriodLocked",
                table: "AttendanceCorrectionRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionAuditLogs_OccurredAt",
                table: "CorrectionAuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "AssignedReviewerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId_Status_ReviewLevel",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "OrganizationId", "Status", "ReviewLevel" });

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "AssignedReviewerEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionAuditLogs_OccurredAt",
                table: "CorrectionAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_AssignedReviewerEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId_Status_ReviewLevel",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "IsPayrollPeriodLocked",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByRole",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewLevel",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
