using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class leaveModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkFromHomeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkFromHomeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkFromHomeRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkWeeks",
                columns: table => new
                {
                    WorkWeekId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsMondayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsTuesdayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsWednesdayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsThursdayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsFridayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsSaturdayWorking = table.Column<bool>(type: "bit", nullable: false),
                    IsSundayWorking = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkWeeks", x => x.WorkWeekId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkFromHomeRequests_EmployeeId",
                table: "WorkFromHomeRequests",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkFromHomeRequests");

            migrationBuilder.DropTable(
                name: "WorkWeeks");
        }
    }
}
