-- Create table for tracking user assignments to agent-based security groups
-- This is a pure addition that doesn't affect existing functionality

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserAgentTypeGroupAssignments')
BEGIN
    CREATE TABLE UserAgentTypeGroupAssignments (
        Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
        UserId nvarchar(100) NOT NULL,
        AgentTypeId uniqueidentifier NOT NULL,
        SecurityGroupId nvarchar(100) NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        AssignedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
        AssignedBy nvarchar(100) NULL,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
        ModifiedDate datetime2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    -- Create indexes for performance
    CREATE INDEX IX_UserAgentTypeGroupAssignments_UserId ON UserAgentTypeGroupAssignments (UserId);
    CREATE INDEX IX_UserAgentTypeGroupAssignments_AgentTypeId ON UserAgentTypeGroupAssignments (AgentTypeId);
    CREATE INDEX IX_UserAgentTypeGroupAssignments_OrganizationId ON UserAgentTypeGroupAssignments (OrganizationId);
    CREATE INDEX IX_UserAgentTypeGroupAssignments_IsActive ON UserAgentTypeGroupAssignments (IsActive);
    
    PRINT 'Created UserAgentTypeGroupAssignments table successfully';
END
ELSE
BEGIN
    PRINT 'UserAgentTypeGroupAssignments table already exists';
END

-- Add new columns to OnboardedUsers table (additive only)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'IsDeleted')
BEGIN
    ALTER TABLE OnboardedUsers ADD IsDeleted bit NOT NULL DEFAULT 0;
    PRINT 'Added IsDeleted column to OnboardedUsers';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'RedirectUri')
BEGIN
    ALTER TABLE OnboardedUsers ADD RedirectUri nvarchar(500) NULL;
    PRINT 'Added RedirectUri column to OnboardedUsers';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'LastInvitationDate')
BEGIN
    ALTER TABLE OnboardedUsers ADD LastInvitationDate datetime2 NULL;
    PRINT 'Added LastInvitationDate column to OnboardedUsers';
END