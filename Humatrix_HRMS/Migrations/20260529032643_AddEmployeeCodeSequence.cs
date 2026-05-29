using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCodeSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "EmployeeCodeSequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "EmployeeCodeSequence");
        }
    }
}
