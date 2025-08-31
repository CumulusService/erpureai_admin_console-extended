using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddRequireSupervisorEmailToAgentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireSupervisorEmail",
                table: "AgentTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireSupervisorEmail",
                table: "AgentTypes");
        }
    }
}
