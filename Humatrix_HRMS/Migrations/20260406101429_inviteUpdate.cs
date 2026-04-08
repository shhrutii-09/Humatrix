using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class inviteUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserInvites_OrganizationId",
                table: "UserInvites",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserInvites_Organizations_OrganizationId",
                table: "UserInvites",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserInvites_Organizations_OrganizationId",
                table: "UserInvites");

            migrationBuilder.DropIndex(
                name: "IX_UserInvites_OrganizationId",
                table: "UserInvites");
        }
    }
}
