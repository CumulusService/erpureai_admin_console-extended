using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check if the column already exists before adding it
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'DatabaseUsername')
                BEGIN
                    ALTER TABLE DatabaseCredentials ADD DatabaseUsername NVARCHAR(128) NOT NULL DEFAULT ''
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatabaseUsername",
                table: "DatabaseCredentials");
        }
    }
}
