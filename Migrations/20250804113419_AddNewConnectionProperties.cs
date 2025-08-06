using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddNewConnectionProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add DatabaseUsername column if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'DatabaseUsername')
                BEGIN
                    ALTER TABLE DatabaseCredentials ADD DatabaseUsername NVARCHAR(128) NOT NULL DEFAULT ''
                END
            ");

            migrationBuilder.AddColumn<string>(
                name: "CurrentSchema",
                table: "DatabaseCredentials",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Encrypt",
                table: "DatabaseCredentials",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "DatabaseCredentials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SSLValidateCertificate",
                table: "DatabaseCredentials",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrustServerCertificate",
                table: "DatabaseCredentials",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentSchema",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "Encrypt",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "SSLValidateCertificate",
                table: "DatabaseCredentials");

            migrationBuilder.DropColumn(
                name: "TrustServerCertificate",
                table: "DatabaseCredentials");
        }
    }
}
