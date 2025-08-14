using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddM365GroupIdOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only add M365GroupId column if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'M365GroupId')
                BEGIN
                    ALTER TABLE [Organizations] ADD [M365GroupId] nvarchar(50) NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop M365GroupId column if it exists
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Organizations' AND COLUMN_NAME = 'M365GroupId')
                BEGIN
                    ALTER TABLE [Organizations] DROP COLUMN [M365GroupId];
                END
            ");
        }
    }
}
