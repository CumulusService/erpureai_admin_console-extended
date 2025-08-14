using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRevocationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserRevocationRecords",
                columns: table => new
                {
                    RevocationRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UserEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UserDisplayName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevokedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RevokedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RestoredBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RestoredOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SecurityGroupsRemoved = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    M365GroupsRemoved = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AppRolesRevoked = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdditionalDetails = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccountDisabled = table.Column<bool>(type: "bit", nullable: false),
                    RevocationSuccessful = table.Column<bool>(type: "bit", nullable: false),
                    RestorationSuccessful = table.Column<bool>(type: "bit", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRevocationRecords", x => x.RevocationRecordId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_OrganizationId",
                table: "UserRevocationRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_OrganizationId_Status",
                table: "UserRevocationRecords",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_RevokedOn",
                table: "UserRevocationRecords",
                column: "RevokedOn");

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_Status",
                table: "UserRevocationRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_UserEmail",
                table: "UserRevocationRecords",
                column: "UserEmail");

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_UserId",
                table: "UserRevocationRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRevocationRecords_UserId_Status",
                table: "UserRevocationRecords",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRevocationRecords");
        }
    }
}
