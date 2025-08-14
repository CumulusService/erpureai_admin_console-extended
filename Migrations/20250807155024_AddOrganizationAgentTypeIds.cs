using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAgentTypeIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationAgentTypeIds",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrganizationAgentTypeIds",
                table: "Organizations");
        }
    }
}
