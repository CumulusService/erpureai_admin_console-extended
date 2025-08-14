using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddSAPServiceLayerDBSchemaColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SAPServiceLayerDBSchema",
                table: "DatabaseCredentials",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SAPServiceLayerDBSchema",
                table: "DatabaseCredentials");
        }
    }
}
