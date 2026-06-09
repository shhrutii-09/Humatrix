using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class exitempupdatedagainagaintwice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRehireable",
                table: "Employees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRehireDate",
                table: "Employees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RehireCount",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeRehires",
                columns: table => new
                {
                    RehireId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousExitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RehireDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RehiredByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousExitType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRehires", x => x.RehireId);
                    table.ForeignKey(
                        name: "FK_EmployeeRehires_EmployeeExits_PreviousExitId",
                        column: x => x.PreviousExitId,
                        principalTable: "EmployeeExits",
                        principalColumn: "ExitId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeRehires_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRehires_EmployeeId",
                table: "EmployeeRehires",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRehires_PreviousExitId",
                table: "EmployeeRehires",
                column: "PreviousExitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeRehires");

            migrationBuilder.DropColumn(
                name: "IsRehireable",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LastRehireDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "RehireCount",
                table: "Employees");
        }
    }
}
