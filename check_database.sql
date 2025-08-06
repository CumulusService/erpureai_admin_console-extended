-- Check current database state for organization records
-- Run this script to understand what organization data exists

SELECT 'Organizations Table:' as TableName;
SELECT 
    OrganizationId,
    Name,
    Domain,
    AdminUserEmail,
    KeyVaultUri,
    KeyVaultSecretPrefix,
    IsActive,
    CreatedDate,
    CreatedBy
FROM Organizations;

SELECT 'OnboardedUsers Table:' as TableName;
SELECT 
    OnboardedUserId,
    Name,
    Email,
    OrganizationId,
    IsActive,
    CreatedOn
FROM OnboardedUsers;

SELECT 'DatabaseCredentials Table:' as TableName;
SELECT 
    Id,
    OrganizationId,
    FriendlyName,
    DatabaseType,
    IsActive,
    CreatedOn
FROM DatabaseCredentials;

-- Check if organization with domain-based ID exists
SELECT 'Domain-based Organization Check:' as CheckType;
DECLARE @DomainOrgId UNIQUEIDENTIFIER;
-- Generate MD5-based GUID for cumulus-service_com (same logic as C# code)
-- This is what the OrganizationService.GetByIdAsync method would do
SELECT 'Looking for organization with domain-based ID for cumulus-service_com';

-- Show all organizations to see what was actually created
SELECT 'All Organization Records:' as Summary;
SELECT COUNT(*) as TotalOrganizations FROM Organizations;