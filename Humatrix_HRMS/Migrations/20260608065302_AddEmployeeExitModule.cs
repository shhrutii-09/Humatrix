using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeExitModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExitReason",
                table: "Employees",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWorkingDay",
                table: "Employees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeExits",
                columns: table => new
                {
                    ExitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResignationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastWorkingDay = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExitInterviewCompleted = table.Column<bool>(type: "bit", nullable: false),
                    ExitInterviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitInterviewFeedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExitInterviewerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssetsReturned = table.Column<bool>(type: "bit", nullable: false),
                    AssetsReturnedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccessRevoked = table.Column<bool>(type: "bit", nullable: false),
                    AccessRevokedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KnowledgeTransferred = table.Column<bool>(type: "bit", nullable: false),
                    KnowledgeTransferDetails = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoDuesCleared = table.Column<bool>(type: "bit", nullable: false),
                    NoDuesClearedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FullFinalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExperienceLetterIssued = table.Column<bool>(type: "bit", nullable: false),
                    ExperienceLetterDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelievingLetterIssued = table.Column<bool>(type: "bit", nullable: false),
                    RelievingLetterDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClearanceRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeExits", x => x.ExitId);
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Employees_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Employees_CompletedByEmployeeId",
                        column: x => x.CompletedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Employees_ExitInterviewerEmployeeId",
                        column: x => x.ExitInterviewerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_ApprovedByEmployeeId",
                table: "EmployeeExits",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_CompletedByEmployeeId",
                table: "EmployeeExits",
                column: "CompletedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_EmployeeId",
                table: "EmployeeExits",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_ExitInterviewerEmployeeId",
                table: "EmployeeExits",
                column: "ExitInterviewerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_LastWorkingDay",
                table: "EmployeeExits",
                column: "LastWorkingDay");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_OrganizationId_Status",
                table: "EmployeeExits",
                columns: new[] { "OrganizationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeExits");

            migrationBuilder.DropColumn(
                name: "ExitReason",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LastWorkingDay",
                table: "Employees");
        }
    }
}
