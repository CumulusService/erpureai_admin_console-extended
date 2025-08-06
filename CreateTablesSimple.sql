-- AdminConsole Database Schema (Simple Version)
-- Run this script to create the database tables

BEGIN TRANSACTION;

-- Organizations Table
CREATE TABLE [Organizations] (
    [OrganizationId] uniqueidentifier NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [AdminEmail] nvarchar(255) NOT NULL,
    [KeyVaultUri] nvarchar(500) NOT NULL,
    [KeyVaultSecretPrefix] nvarchar(100) NOT NULL,
    [DatabaseType] int NOT NULL,
    [SAPServiceLayerHostname] nvarchar(255) NOT NULL,
    [SAPAPIGatewayHostname] nvarchar(255) NOT NULL,
    [SAPBusinessOneWebClientHost] nvarchar(255) NOT NULL,
    [DocumentCode] nvarchar(50) NOT NULL,
    [CreatedOn] datetime2 NOT NULL,
    [ModifiedOn] datetime2 NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [CreatedOnBehalfBy] uniqueidentifier NULL,
    [ModifiedOnBehalfBy] uniqueidentifier NULL,
    [OwnerId] uniqueidentifier NOT NULL,
    [OwningBusinessUnit] uniqueidentifier NULL,
    [OwningTeam] uniqueidentifier NULL,
    [OwningUser] uniqueidentifier NULL,
    [StateCode] int NOT NULL,
    [StatusCode] int NOT NULL,
    [ImportSequenceNumber] int NULL,
    [OverriddenCreatedOn] datetime2 NULL,
    [TimeZoneRuleVersionNumber] int NULL,
    [UTCConversionTimeZoneCode] int NULL,
    [Id] nvarchar(max) NULL,
    [Domain] nvarchar(max) NOT NULL,
    [AdminUserId] nvarchar(max) NOT NULL,
    [AdminUserName] nvarchar(max) NOT NULL,
    [AdminUserEmail] nvarchar(max) NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [UserCount] int NOT NULL,
    [SecretCount] int NOT NULL,
    CONSTRAINT [PK_Organizations] PRIMARY KEY ([OrganizationId])
);

-- Database Credentials Table
CREATE TABLE [DatabaseCredentials] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [DatabaseType] int NOT NULL,
    [ServerInstance] nvarchar(255) NOT NULL,
    [DatabaseName] nvarchar(255) NOT NULL,
    [FriendlyName] nvarchar(100) NOT NULL,
    [SAPUsername] nvarchar(128) NOT NULL,
    [PasswordSecretName] nvarchar(max) NOT NULL,
    [ConnectionString] nvarchar(1000) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedOn] datetime2 NOT NULL,
    [ModifiedOn] datetime2 NOT NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [ModifiedBy] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_DatabaseCredentials] PRIMARY KEY ([Id])
);

-- Onboarded Users Table
CREATE TABLE [OnboardedUsers] (
    [OnboardedUserId] uniqueidentifier NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Email] nvarchar(255) NOT NULL,
    [FullName] nvarchar(255) NOT NULL,
    [AssignedDatabaseIds] nvarchar(max) NOT NULL,
    [OrganizationLookupId] uniqueidentifier NULL,
    [IsActive] bit NOT NULL,
    [UserActive] bit NOT NULL,
    [AgentTypes] nvarchar(max) NOT NULL,
    [AgentNameId] uniqueidentifier NULL,
    [AssignedSupervisorEmail] nvarchar(max) NOT NULL,
    [CreatedOn] datetime2 NOT NULL,
    [ModifiedOn] datetime2 NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [CreatedOnBehalfBy] uniqueidentifier NULL,
    [ModifiedOnBehalfBy] uniqueidentifier NULL,
    [OwnerId] uniqueidentifier NOT NULL,
    [OwningBusinessUnit] uniqueidentifier NULL,
    [OwningTeam] uniqueidentifier NULL,
    [OwningUser] uniqueidentifier NULL,
    [StateCode] int NOT NULL,
    [StatusCode] int NOT NULL,
    [ImportSequenceNumber] int NULL,
    [OverriddenCreatedOn] datetime2 NULL,
    [TimeZoneRuleVersionNumber] int NULL,
    [UTCConversionTimeZoneCode] int NULL,
    [OrganizationId] uniqueidentifier NULL,
    CONSTRAINT [PK_OnboardedUsers] PRIMARY KEY ([OnboardedUserId])
);

-- User Database Assignments Table
CREATE TABLE [UserDatabaseAssignments] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [DatabaseCredentialId] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [AssignedOn] datetime2 NOT NULL,
    [AssignedBy] nvarchar(255) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_UserDatabaseAssignments] PRIMARY KEY ([Id])
);

-- Create Indexes for Performance
CREATE INDEX [IX_OnboardedUsers_OrganizationId] ON [OnboardedUsers] ([OrganizationId]);
CREATE INDEX [IX_UserDatabaseAssignments_OrganizationId] ON [UserDatabaseAssignments] ([OrganizationId]);
CREATE UNIQUE INDEX [IX_UserDatabaseAssignments_UserId_DatabaseCredentialId] ON [UserDatabaseAssignments] ([UserId], [DatabaseCredentialId]);

COMMIT;
GO