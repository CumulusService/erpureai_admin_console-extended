using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedRoleToOnboardedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedRole",
                table: "OnboardedUsers",
                type: "int",
                nullable: false,
                defaultValue: 2); // UserRole.User = 2 (safe default)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedRole",
                table: "OnboardedUsers");
        }
    }
}
