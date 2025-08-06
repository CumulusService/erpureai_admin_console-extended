-- Fix Organization GUID Mismatch
-- The organization was created with a random GUID but the user authentication 
-- expects a deterministic GUID based on the domain 'cumulus-service_com'

-- Current situation:
-- Database GUID: 9F1A43A3-DB57-4CB1-9E5E-9017811B4279
-- Expected GUID: ac2987b9-d357-738f-a737-92733866ce54 (from MD5 hash of 'cumulus-service_com')

BEGIN TRANSACTION;

-- Update the organization to use the expected GUID
UPDATE Organizations 
SET OrganizationId = 'ac2987b9-d357-738f-a737-92733866ce54'
WHERE OrganizationId = '9F1A43A3-DB57-4CB1-9E5E-9017811B4279';

-- Verify the update
SELECT 
    OrganizationId,
    Name,
    Domain,
    AdminUserEmail,
    IsActive,
    CreatedDate
FROM Organizations 
WHERE OrganizationId = 'ac2987b9-d357-738f-a737-92733866ce54';

COMMIT;