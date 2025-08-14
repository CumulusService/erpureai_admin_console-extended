using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddSAPConfigurationToDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentCode",
                table: "DatabaseCredentials",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SAPAPIGatewayHostname",
                table: "DatabaseCredentials",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SAPBusinessOneWebClientHost",
                table: "DatabaseCredentials",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SAPServiceLayerHostname",
                table: "DatabaseCredentials",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentCode",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "SAPAPIGatewayHostname",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "SAPBusinessOneWebClientHost",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "SAPServiceLayerHostname",
                table: "DatabaseCredentials");
        }
    }
}
