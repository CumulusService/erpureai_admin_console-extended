-- Add InstanceName column to DatabaseCredentials table
-- This separates SQL Server instance names from server hostnames/IPs

-- Check if column already exists to avoid errors
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'DatabaseCredentials') AND name = 'InstanceName')
BEGIN
    ALTER TABLE DatabaseCredentials 
    ADD InstanceName NVARCHAR(128) NULL DEFAULT '';
    
    PRINT 'InstanceName column added successfully';
END
ELSE
BEGIN
    PRINT 'InstanceName column already exists';
END
GO