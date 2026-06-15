using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class newcolumnninsupportticket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "SupportTickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedByUserId",
                table: "SupportTickets",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUserId",
                table: "SupportTickets",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ClosedByUserId",
                table: "SupportTickets",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ResolvedByUserId",
                table: "SupportTickets",
                column: "ResolvedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_ClosedByUserId",
                table: "SupportTickets",
                column: "ClosedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_ResolvedByUserId",
                table: "SupportTickets",
                column: "ResolvedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_ClosedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_ResolvedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_ClosedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_ResolvedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "SupportTickets");
        }
    }
}
