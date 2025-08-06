-- Add ConnectionStringSecretName column to DatabaseCredentials table
-- This enables secure storage of connection strings in Key Vault

-- Check if column already exists to avoid errors
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'DatabaseCredentials') AND name = 'ConnectionStringSecretName')
BEGIN
    ALTER TABLE DatabaseCredentials 
    ADD ConnectionStringSecretName NVARCHAR(255) NULL DEFAULT '';
    
    PRINT 'ConnectionStringSecretName column added successfully';
END
ELSE
BEGIN
    PRINT 'ConnectionStringSecretName column already exists';
END
GO

-- Make ConnectionString column nullable since it's now deprecated in favor of Key Vault storage
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'DatabaseCredentials') AND name = 'ConnectionString' AND is_nullable = 0)
BEGIN
    ALTER TABLE DatabaseCredentials 
    ALTER COLUMN ConnectionString NVARCHAR(1000) NULL;
    
    PRINT 'ConnectionString column made nullable (deprecated field)';
END
ELSE
BEGIN
    PRINT 'ConnectionString column is already nullable or does not exist';
END
GO