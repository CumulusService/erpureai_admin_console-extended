using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowUserInvitationsToOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowUserInvitations",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowUserInvitations",
                table: "Organizations");
        }
    }
}
