-- Fix the organization GUID mismatch
-- Update from: F1931C79-B220-42F0-8E5F-64CD42ECA411
-- Update to:   ac2987b9-d357-738f-a737-92733866ce54

UPDATE Organizations 
SET OrganizationId = 'ac2987b9-d357-738f-a737-92733866ce54'
WHERE OrganizationId = 'F1931C79-B220-42F0-8E5F-64CD42ECA411';

-- Verify the update
SELECT OrganizationId, Name, Domain, AdminUserEmail, IsActive 
FROM Organizations 
WHERE OrganizationId = 'ac2987b9-d357-738f-a737-92733866ce54';