using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationIdToCorrectionAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Attendances_AttendanceId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropTable(
                name: "AttendanceAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "HRRemarks",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.RenameColumn(
                name: "ReviewedBy",
                table: "AttendanceCorrectionRequests",
                newName: "InitiatedByHrEmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExistingCheckOut",
                table: "AttendanceCorrectionRequests",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "ExistingCheckIn",
                table: "AttendanceCorrectionRequests",
                newName: "ApprovedCheckOut");

            migrationBuilder.AddColumn<bool>(
                name: "IsHrCorrected",
                table: "Attendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAt",
                table: "Attendances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifiedByEmployeeId",
                table: "Attendances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationReason",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "RejectionReason",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "HrNote",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAt",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedStatus",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionType",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OriginalStatus",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OriginalTotalHours",
                table: "AttendanceCorrectionRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedStatus",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrectionAuditLogs",
                columns: table => new
                {
                    CorrectionAuditLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceCorrectionRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousCheckIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousCheckOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewCheckIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewCheckOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionAuditLogs", x => x.CorrectionAuditLogId);
                    table.ForeignKey(
                        name: "FK_CorrectionAuditLogs_AttendanceCorrectionRequests_AttendanceCorrectionRequestId",
                        column: x => x.AttendanceCorrectionRequestId,
                        principalTable: "AttendanceCorrectionRequests",
                        principalColumn: "AttendanceCorrectionRequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrectionAuditLogs_Employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId_WorkDate_Status",
                table: "AttendanceCorrectionRequests",
                columns: new[] { "EmployeeId", "WorkDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_InitiatedByHrEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "InitiatedByHrEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId",
                table: "AttendanceCorrectionRequests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_ReviewedByEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "ReviewedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_SubmittedAt",
                table: "AttendanceCorrectionRequests",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionAuditLogs_ActorEmployeeId",
                table: "CorrectionAuditLogs",
                column: "ActorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionAuditLogs_AttendanceCorrectionRequestId",
                table: "CorrectionAuditLogs",
                column: "AttendanceCorrectionRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Attendances_AttendanceId",
                table: "AttendanceCorrectionRequests",
                column: "AttendanceId",
                principalTable: "Attendances",
                principalColumn: "AttendanceId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_InitiatedByHrEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "InitiatedByHrEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_ReviewedByEmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "ReviewedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Organizations_OrganizationId",
                table: "AttendanceCorrectionRequests",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Attendances_AttendanceId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_InitiatedByHrEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_ReviewedByEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrectionRequests_Organizations_OrganizationId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropTable(
                name: "CorrectionAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId_WorkDate_Status",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_InitiatedByHrEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_OrganizationId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_ReviewedByEmployeeId",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrectionRequests_SubmittedAt",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "IsHrCorrected",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LastModifiedAt",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LastModifiedByEmployeeId",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "ModificationReason",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedCheckIn",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedStatus",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "CorrectionType",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "OriginalStatus",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "OriginalTotalHours",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.DropColumn(
                name: "RequestedStatus",
                table: "AttendanceCorrectionRequests");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "AttendanceCorrectionRequests",
                newName: "ExistingCheckOut");

            migrationBuilder.RenameColumn(
                name: "InitiatedByHrEmployeeId",
                table: "AttendanceCorrectionRequests",
                newName: "ReviewedBy");

            migrationBuilder.RenameColumn(
                name: "ApprovedCheckOut",
                table: "AttendanceCorrectionRequests",
                newName: "ExistingCheckIn");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedCheckIn",
                table: "AttendanceCorrectionRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RejectionReason",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "HrNote",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HRRemarks",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "AttendanceCorrectionRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AttendanceAuditLogs",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAuditLogs", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_AttendanceAuditLogs_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "AttendanceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrectionRequests_EmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAuditLogs_AttendanceId",
                table: "AttendanceAuditLogs",
                column: "AttendanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Attendances_AttendanceId",
                table: "AttendanceCorrectionRequests",
                column: "AttendanceId",
                principalTable: "Attendances",
                principalColumn: "AttendanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrectionRequests_Employees_EmployeeId",
                table: "AttendanceCorrectionRequests",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
