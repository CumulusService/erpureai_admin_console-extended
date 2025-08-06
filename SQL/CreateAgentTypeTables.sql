-- Create AgentTypes table
CREATE TABLE [dbo].[AgentTypes] (
    [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
    [Name] nvarchar(100) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [AgentShareUrl] nvarchar(500) NULL,
    [EntraApplicationId] nvarchar(100) NULL,
    [GlobalSecurityGroupId] nvarchar(100) NULL,
    [MSTeams] bit NOT NULL DEFAULT 0,
    [Description] nvarchar(500) NULL,
    [IsActive] bit NOT NULL DEFAULT 1,
    [DisplayOrder] int NOT NULL DEFAULT 0,
    [CreatedDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_AgentTypes] PRIMARY KEY ([Id])
);

-- Create unique index on Name
CREATE UNIQUE INDEX [IX_AgentTypes_Name] ON [dbo].[AgentTypes] ([Name]);

-- Create OrganizationTeamsGroups table (without foreign key constraint due to permissions)
CREATE TABLE [dbo].[OrganizationTeamsGroups] (
    [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
    [OrganizationId] uniqueidentifier NOT NULL,
    [AgentTypeId] uniqueidentifier NOT NULL,
    [TeamsGroupId] nvarchar(100) NOT NULL,
    [TeamName] nvarchar(255) NOT NULL,
    [TeamUrl] nvarchar(500) NULL,
    [Description] nvarchar(500) NULL,
    [IsActive] bit NOT NULL DEFAULT 1,
    [CreatedDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] nvarchar(100) NULL,
    [ModifiedDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_OrganizationTeamsGroups] PRIMARY KEY ([Id])
    -- Note: Foreign key constraint omitted due to REFERENCES permission limitations
    -- Application-level referential integrity will be maintained in code
);

-- Create indexes for OrganizationTeamsGroups
CREATE UNIQUE INDEX [IX_OrganizationTeamsGroups_OrganizationId_AgentTypeId] 
    ON [dbo].[OrganizationTeamsGroups] ([OrganizationId], [AgentTypeId]);
CREATE INDEX [IX_OrganizationTeamsGroups_OrganizationId] 
    ON [dbo].[OrganizationTeamsGroups] ([OrganizationId]);
CREATE INDEX [IX_OrganizationTeamsGroups_AgentTypeId] 
    ON [dbo].[OrganizationTeamsGroups] ([AgentTypeId]);
CREATE INDEX [IX_OrganizationTeamsGroups_TeamsGroupId] 
    ON [dbo].[OrganizationTeamsGroups] ([TeamsGroupId]);

-- Seed initial AgentType data
INSERT INTO [dbo].[AgentTypes] ([Id], [Name], [DisplayName], [MSTeams], [Description], [DisplayOrder])
VALUES 
    (NEWID(), 'SBOAgentAppv1', 'SBO Agent App v1', 1, 'SAP Business One integration agent', 1),
    (NEWID(), 'Sales', 'Sales Agent', 1, 'Sales and customer management agent', 2),
    (NEWID(), 'Admin', 'Admin Agent', 0, 'Administrative and management agent', 3);

PRINT 'AgentType and OrganizationTeamsGroup tables created successfully!';