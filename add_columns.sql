-- Add new columns to DatabaseCredentials table
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'DatabaseUsername')
BEGIN
    ALTER TABLE DatabaseCredentials ADD DatabaseUsername NVARCHAR(128) NOT NULL DEFAULT ''
    PRINT 'Added DatabaseUsername column'
END
ELSE
BEGIN
    PRINT 'DatabaseUsername column already exists'
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'Port')
BEGIN
    ALTER TABLE DatabaseCredentials ADD Port INT NULL
    PRINT 'Added Port column'
END
ELSE
BEGIN
    PRINT 'Port column already exists'
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'CurrentSchema')
BEGIN
    ALTER TABLE DatabaseCredentials ADD CurrentSchema NVARCHAR(128) NULL
    PRINT 'Added CurrentSchema column'
END
ELSE
BEGIN
    PRINT 'CurrentSchema column already exists'
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'Encrypt')
BEGIN
    ALTER TABLE DatabaseCredentials ADD Encrypt BIT NOT NULL DEFAULT 1
    PRINT 'Added Encrypt column'
END
ELSE
BEGIN
    PRINT 'Encrypt column already exists'
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'SSLValidateCertificate')
BEGIN
    ALTER TABLE DatabaseCredentials ADD SSLValidateCertificate BIT NOT NULL DEFAULT 0
    PRINT 'Added SSLValidateCertificate column'
END
ELSE
BEGIN
    PRINT 'SSLValidateCertificate column already exists'
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DatabaseCredentials' AND COLUMN_NAME = 'TrustServerCertificate')
BEGIN
    ALTER TABLE DatabaseCredentials ADD TrustServerCertificate BIT NOT NULL DEFAULT 1
    PRINT 'Added TrustServerCertificate column'
END
ELSE
BEGIN
    PRINT 'TrustServerCertificate column already exists'
END

-- Add migration history records
IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20250803215902_InitialCreate')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20250803215902_InitialCreate', '9.0.0')
    PRINT 'Added InitialCreate migration to history'
END
ELSE
BEGIN
    PRINT 'InitialCreate migration already in history'
END

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20250804105903_AddDatabaseUsername')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20250804105903_AddDatabaseUsername', '9.0.0')
    PRINT 'Added AddDatabaseUsername migration to history'
END
ELSE
BEGIN
    PRINT 'AddDatabaseUsername migration already in history'
END

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20250804113419_AddNewConnectionProperties')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20250804113419_AddNewConnectionProperties', '9.0.0')
    PRINT 'Added AddNewConnectionProperties migration to history'
END
ELSE
BEGIN
    PRINT 'AddNewConnectionProperties migration already in history'
END