using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class Helpdesk_AssignmentAndInternalNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "TicketReplies",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsInternalNote",
                table: "TicketReplies",
                type: "bit",
                nullable: false,
                defaultValue: false);

          

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SupportTickets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Open",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Resolution",
                table: "SupportTickets",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "SupportTickets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Medium",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "SupportTickets",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "SupportTickets",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "SupportTickets",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SupportTickets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SupportTickets",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_TicketReplies_CreatedAt",
                table: "TicketReplies",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedToEmployeeId",
                table: "SupportTickets",
                column: "AssignedToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedToUserId",
                table: "SupportTickets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrganizationId_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrganizationId_Status",
                table: "SupportTickets",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_TicketNumber",
                table: "SupportTickets",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_Employees_AssignedToEmployeeId",
                table: "SupportTickets",
                column: "AssignedToEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_Employees_AssignedToEmployeeId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_TicketReplies_CreatedAt",
                table: "TicketReplies");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_AssignedToEmployeeId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_AssignedToUserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_OrganizationId_CreatedAt",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_OrganizationId_Status",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_TicketNumber",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "IsInternalNote",
                table: "TicketReplies");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SupportTickets");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "TicketReplies",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

          

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SupportTickets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Open");

            migrationBuilder.AlterColumn<string>(
                name: "Resolution",
                table: "SupportTickets",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "SupportTickets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Medium");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "SupportTickets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);
        }
    }
}
