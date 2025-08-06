using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminConsole.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatabaseType = table.Column<int>(type: "int", nullable: false),
                    ServerInstance = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FriendlyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SAPUsername = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PasswordSecretName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConnectionString = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    KeyVaultUri = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    KeyVaultSecretPrefix = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DatabaseType = table.Column<int>(type: "int", nullable: false),
                    SAPServiceLayerHostname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SAPAPIGatewayHostname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SAPBusinessOneWebClientHost = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DocumentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnBehalfBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedOnBehalfBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwningBusinessUnit = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwningTeam = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwningUser = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateCode = table.Column<int>(type: "int", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ImportSequenceNumber = table.Column<int>(type: "int", nullable: true),
                    OverriddenCreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeZoneRuleVersionNumber = table.Column<int>(type: "int", nullable: true),
                    UTCConversionTimeZoneCode = table.Column<int>(type: "int", nullable: true),
                    Id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Domain = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdminUserEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UserCount = table.Column<int>(type: "int", nullable: false),
                    SecretCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "UserDatabaseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatabaseCredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDatabaseAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardedUsers",
                columns: table => new
                {
                    OnboardedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AssignedDatabaseIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrganizationLookupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UserActive = table.Column<bool>(type: "bit", nullable: false),
                    AgentTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentNameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedSupervisorEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnBehalfBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedOnBehalfBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwningBusinessUnit = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwningTeam = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwningUser = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateCode = table.Column<int>(type: "int", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ImportSequenceNumber = table.Column<int>(type: "int", nullable: true),
                    OverriddenCreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeZoneRuleVersionNumber = table.Column<int>(type: "int", nullable: true),
                    UTCConversionTimeZoneCode = table.Column<int>(type: "int", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardedUsers", x => x.OnboardedUserId);
                    table.ForeignKey(
                        name: "FK_OnboardedUsers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardedUsers_OrganizationId",
                table: "OnboardedUsers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseAssignments_OrganizationId",
                table: "UserDatabaseAssignments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseAssignments_UserId_DatabaseCredentialId",
                table: "UserDatabaseAssignments",
                columns: new[] { "UserId", "DatabaseCredentialId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseCredentials");

            migrationBuilder.DropTable(
                name: "OnboardedUsers");

            migrationBuilder.DropTable(
                name: "UserDatabaseAssignments");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
