-- Simple script to add the missing ConnectionStringSecretName column
ALTER TABLE DatabaseCredentials 
ADD ConnectionStringSecretName NVARCHAR(255) NULL;

-- Set default value for existing rows
UPDATE DatabaseCredentials 
SET ConnectionStringSecretName = '' 
WHERE ConnectionStringSecretName IS NULL;

-- Verify the column was added
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'DatabaseCredentials' 
  AND COLUMN_NAME = 'ConnectionStringSecretName';