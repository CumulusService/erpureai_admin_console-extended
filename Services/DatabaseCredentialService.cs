using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Sap.Data.Hana;

namespace AdminConsole.Services;

/// <summary>
/// Implementation of database credential service using Entity Framework and Key Vault for passwords
/// </summary>
public class DatabaseCredentialService : IDatabaseCredentialService
{
    private readonly AdminConsoleDbContext _context;
    private readonly IKeyVaultService _keyVaultService;
    private readonly IDataIsolationService _dataIsolationService;
    private readonly ILogger<DatabaseCredentialService> _logger;
    private readonly IMemoryCache _cache;

    public DatabaseCredentialService(
        AdminConsoleDbContext context,
        IKeyVaultService keyVaultService,
        IDataIsolationService dataIsolationService,
        ILogger<DatabaseCredentialService> logger,
        IMemoryCache cache)
    {
        _context = context;
        _keyVaultService = keyVaultService;
        _dataIsolationService = dataIsolationService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<List<DatabaseCredential>> GetByOrganizationAsync(Guid organizationId)
    {
        try
        {
            var cacheKey = $"org_credentials_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<DatabaseCredential>? cachedCredentials))
            {
                return cachedCredentials ?? new List<DatabaseCredential>();
            }

            var credentials = await _context.DatabaseCredentials
                .Where(c => c.OrganizationId == organizationId)
                .OrderByDescending(c => c.CreatedOn)
                .ToListAsync();

            // Cache for 5 minutes
            _cache.Set(cacheKey, credentials, TimeSpan.FromMinutes(5));

            return credentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database credentials for organization {OrganizationId}", organizationId);
            return new List<DatabaseCredential>();
        }
    }

    public async Task<List<DatabaseCredential>> GetActiveByOrganizationAsync(Guid organizationId)
    {
        try
        {
            var cacheKey = $"org_active_credentials_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<DatabaseCredential>? cachedCredentials))
            {
                return cachedCredentials ?? new List<DatabaseCredential>();
            }

            var credentials = await _context.DatabaseCredentials
                .Where(c => c.OrganizationId == organizationId && c.IsActive)
                .OrderByDescending(c => c.CreatedOn)
                .ToListAsync();

            // Cache for 5 minutes
            _cache.Set(cacheKey, credentials, TimeSpan.FromMinutes(5));

            return credentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active database credentials for organization {OrganizationId}", organizationId);
            return new List<DatabaseCredential>();
        }
    }

    public async Task<DatabaseCredential?> GetByIdAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            return await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId && c.IsActive)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database credential {CredentialId} for organization {OrganizationId}", 
                credentialId, organizationId);
            return null;
        }
    }

    public async Task<List<DatabaseCredential>> GetByOrganizationAndTypeAsync(Guid organizationId, DatabaseType databaseType)
    {
        try
        {
            return await _context.DatabaseCredentials
                .Where(c => c.OrganizationId == organizationId && c.DatabaseType == databaseType && c.IsActive)
                .OrderByDescending(c => c.CreatedOn)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database credentials for organization {OrganizationId} and type {DatabaseType}", 
                organizationId, databaseType);
            return new List<DatabaseCredential>();
        }
    }

    public async Task<DatabaseCredential> CreateAsync(Guid organizationId, DatabaseCredentialModel model, Guid createdBy)
    {
        return await CreateAsync(model, organizationId, createdBy);
    }

    public async Task<DatabaseCredential> CreateAsync(DatabaseCredentialModel model, Guid organizationId, Guid createdBy)
    {
        _logger.LogInformation("=== DatabaseCredentialService.CreateAsync ===");
        _logger.LogInformation("  FriendlyName: {FriendlyName}", model.FriendlyName);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        _logger.LogInformation("  CreatedBy: {CreatedBy}", createdBy);
        
        try
        {
            var credential = new DatabaseCredential
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                DatabaseType = model.DatabaseType!.Value,
                ServerInstance = model.ServerInstance,
                InstanceName = model.InstanceName,
                DatabaseName = model.DatabaseName,
                FriendlyName = model.FriendlyName,
                DatabaseUsername = model.DatabaseUsername,
                SAPUsername = model.SAPUsername,
                Description = model.Description,
                IsActive = model.IsActive,
                Port = model.Port,
                CurrentSchema = model.CurrentSchema,
                Encrypt = model.Encrypt,
                SSLValidateCertificate = model.SSLValidateCertificate,
                TrustServerCertificate = model.TrustServerCertificate,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                CreatedBy = createdBy,
                ModifiedBy = createdBy
            };

            // Generate secret name and store password in Key Vault
            var passwordSecretName = credential.GeneratePasswordSecretName();
            _logger.LogInformation("  Generated password secret name: {PasswordSecretName}", passwordSecretName);
            
            _logger.LogInformation("  Calling KeyVaultService.SetSecretAsync...");
            var keyVaultResult = await _keyVaultService.SetSecretAsync(passwordSecretName, model.SAPPassword, organizationId.ToString());
            _logger.LogInformation("  KeyVaultService.SetSecretAsync returned: {Result}", keyVaultResult);
            
            if (!keyVaultResult)
            {
                _logger.LogError("Failed to store password in Key Vault for credential {FriendlyName}", model.FriendlyName);
                throw new InvalidOperationException("Failed to store password in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
            }

            // Get the secret URI and store it instead of the secret name
            _logger.LogInformation("  Retrieving password secret URI...");
            var passwordSecretUri = await _keyVaultService.GetSecretIdentifierAsync(passwordSecretName, organizationId.ToString());
            if (passwordSecretUri != null)
            {
                credential.PasswordSecretName = passwordSecretUri;
                _logger.LogInformation("  Stored password secret URI: {PasswordSecretUri}", passwordSecretUri);
            }
            else
            {
                _logger.LogWarning("  Failed to retrieve password secret URI, storing secret name as fallback");
                credential.PasswordSecretName = passwordSecretName;
            }

            // Generate connection string secret name and store full connection string in Key Vault
            var connectionStringSecretName = credential.GenerateConnectionStringSecretName();
            var fullConnectionString = credential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
            
            _logger.LogInformation("  Generated connection string secret name: {SecretName}", connectionStringSecretName);
            _logger.LogInformation("  Built connection string (length: {Length}): {ConnectionString}", 
                fullConnectionString.Length, 
                fullConnectionString.Replace(model.DatabasePassword ?? "", "***PASSWORD***"));
            
            _logger.LogInformation("  Calling KeyVaultService.SetSecretAsync for connection string...");
            var connectionStringResult = await _keyVaultService.SetSecretAsync(connectionStringSecretName, fullConnectionString, organizationId.ToString());
            _logger.LogInformation("  KeyVaultService.SetSecretAsync for connection string returned: {Result}", connectionStringResult);
            
            if (!connectionStringResult)
            {
                _logger.LogError("Failed to store connection string in Key Vault for credential {FriendlyName}", model.FriendlyName);
                throw new InvalidOperationException("Failed to store connection string in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
            }

            // Get the connection string secret URI and store it instead of the secret name
            _logger.LogInformation("  Retrieving connection string secret URI...");
            var connectionStringSecretUri = await _keyVaultService.GetSecretIdentifierAsync(connectionStringSecretName, organizationId.ToString());
            if (connectionStringSecretUri != null)
            {
                credential.ConnectionStringSecretName = connectionStringSecretUri;
                _logger.LogInformation("  Stored connection string secret URI: {ConnectionStringSecretUri}", connectionStringSecretUri);
            }
            else
            {
                _logger.LogWarning("  Failed to retrieve connection string secret URI, storing secret name as fallback");
                credential.ConnectionStringSecretName = connectionStringSecretName;
            }

            // Keep connection string empty in database for security (deprecated field)
            credential.ConnectionString = string.Empty;

            _context.DatabaseCredentials.Add(credential);
            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);

            _logger.LogInformation("Created database credential {FriendlyName} for organization {OrganizationId}", 
                model.FriendlyName, organizationId);

            return credential;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database credential {FriendlyName}", model.FriendlyName);
            throw;
        }
    }

    public async Task<DatabaseCredential> UpdateAsync(Guid credentialId, DatabaseCredentialModel model, Guid modifiedBy)
    {
        // First find the credential to get organization ID
        var existingCredential = await _context.DatabaseCredentials
            .Where(c => c.Id == credentialId)
            .FirstOrDefaultAsync();

        if (existingCredential == null)
        {
            throw new ArgumentException($"Database credential {credentialId} not found");
        }

        var success = await UpdateAsync(credentialId, model, existingCredential.OrganizationId, modifiedBy);
        if (!success)
        {
            throw new InvalidOperationException($"Failed to update database credential {credentialId}");
        }

        return existingCredential;
    }

    public async Task<bool> UpdateAsync(Guid credentialId, DatabaseCredentialModel model, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            _logger.LogInformation("UpdateAsync called with credentialId: {CredentialId}, organizationId: {OrganizationId}", credentialId, organizationId);
            
            // First, let's see if the credential exists at all
            var anyCredential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId)
                .FirstOrDefaultAsync();
            
            if (anyCredential != null)
            {
                _logger.LogInformation("Found credential {CredentialId} with organization {FoundOrganizationId}", credentialId, anyCredential.OrganizationId);
            }
            else
            {
                _logger.LogWarning("Credential {CredentialId} not found at all", credentialId);
                return false;
            }
            
            var existingCredential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .FirstOrDefaultAsync();

            if (existingCredential == null)
            {
                _logger.LogWarning("Database credential {CredentialId} not found for organization {OrganizationId} (but exists for {ActualOrgId})", 
                    credentialId, organizationId, anyCredential?.OrganizationId);
                return false;
            }

            // Update properties
            existingCredential.DatabaseType = model.DatabaseType!.Value;
            existingCredential.ServerInstance = model.ServerInstance;
            existingCredential.InstanceName = model.InstanceName;
            existingCredential.DatabaseName = model.DatabaseName;
            existingCredential.FriendlyName = model.FriendlyName;
            existingCredential.DatabaseUsername = model.DatabaseUsername;
            existingCredential.SAPUsername = model.SAPUsername;
            existingCredential.Description = model.Description;
            existingCredential.IsActive = model.IsActive;
            existingCredential.Port = model.Port;
            existingCredential.CurrentSchema = model.CurrentSchema;
            existingCredential.Encrypt = model.Encrypt;
            existingCredential.SSLValidateCertificate = model.SSLValidateCertificate;
            existingCredential.TrustServerCertificate = model.TrustServerCertificate;
            existingCredential.ModifiedOn = DateTime.UtcNow;
            existingCredential.ModifiedBy = modifiedBy;

            // Update SAP password in Key Vault if provided
            _logger.LogInformation("Checking SAP password update - SAPPassword: '{PasswordProvided}', Length: {PasswordLength}", 
                string.IsNullOrEmpty(model.SAPPassword) ? "NULL/EMPTY" : "PROVIDED", model.SAPPassword?.Length ?? 0);
                
            if (!string.IsNullOrEmpty(model.SAPPassword))
            {
                _logger.LogInformation("Updating SAP password in Key Vault for credential {FriendlyName}", model.FriendlyName);
                
                // Use URI-based update to create new version of existing secret
                var (success, newVersionUri) = await _keyVaultService.UpdateSecretByUriAsync(existingCredential.PasswordSecretName, model.SAPPassword, organizationId.ToString());
                if (!success)
                {
                    _logger.LogError("Failed to update SAP password in Key Vault for credential {FriendlyName}", model.FriendlyName);
                    throw new InvalidOperationException("Failed to update SAP password in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
                }

                // Update to the new version URI in SQL database
                if (!string.IsNullOrEmpty(newVersionUri))
                {
                    existingCredential.PasswordSecretName = newVersionUri;
                    _logger.LogInformation("Updated SAP password secret to new version URI: {NewVersionUri}", newVersionUri);
                }

                _logger.LogInformation("SAP password updated successfully in Key Vault (new version created for existing secret)");
            }
            else
            {
                _logger.LogInformation("No SAP password provided - skipping Key Vault update");
            }

            // Update connection string in Key Vault if database password is provided
            _logger.LogInformation("Checking database password update - DatabasePassword: '{PasswordProvided}', Length: {PasswordLength}", 
                string.IsNullOrEmpty(model.DatabasePassword) ? "NULL/EMPTY" : "PROVIDED", model.DatabasePassword?.Length ?? 0);
                
            if (!string.IsNullOrEmpty(model.DatabasePassword))
            {
                _logger.LogInformation("Updating connection string in Key Vault with new database password");
                
                // Ensure ConnectionStringSecretName exists (for backward compatibility)
                if (string.IsNullOrEmpty(existingCredential.ConnectionStringSecretName))
                {
                    var connectionStringSecretName = existingCredential.GenerateConnectionStringSecretName();
                    var initialConnectionString = existingCredential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
                    
                    // Create new connection string secret for legacy records
                    var createResult = await _keyVaultService.SetSecretAsync(connectionStringSecretName, initialConnectionString, organizationId.ToString());
                    if (!createResult)
                    {
                        _logger.LogError("Failed to create connection string in Key Vault for credential {FriendlyName}", model.FriendlyName);
                        throw new InvalidOperationException("Failed to create connection string in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
                    }
                    
                    // Get the new URI and store it
                    var newConnectionStringUri = await _keyVaultService.GetSecretIdentifierAsync(connectionStringSecretName, organizationId.ToString());
                    if (newConnectionStringUri != null)
                    {
                        existingCredential.ConnectionStringSecretName = newConnectionStringUri;
                        _logger.LogInformation("Created new connection string secret URI: {ConnectionStringSecretUri}", newConnectionStringUri);
                    }
                }
                else
                {
                    // Use URI-based update to create new version of existing secret
                    var fullConnectionString = existingCredential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
                    var (success, newVersionUri) = await _keyVaultService.UpdateSecretByUriAsync(existingCredential.ConnectionStringSecretName, fullConnectionString, organizationId.ToString());
                    
                    if (!success)
                    {
                        _logger.LogError("Failed to update connection string in Key Vault for credential {FriendlyName}", model.FriendlyName);
                        throw new InvalidOperationException("Failed to update connection string in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
                    }

                    // Update to the new version URI in SQL database
                    if (!string.IsNullOrEmpty(newVersionUri))
                    {
                        existingCredential.ConnectionStringSecretName = newVersionUri;
                        _logger.LogInformation("Updated connection string secret to new version URI: {NewVersionUri}", newVersionUri);
                    }
                }

                _logger.LogInformation("Connection string updated successfully in Key Vault (new version created for existing secret)");
            }
            else
            {
                _logger.LogInformation("No database password provided - keeping existing connection string");
            }

            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);

            _logger.LogInformation("Updated database credential {CredentialId} for organization {OrganizationId}", 
                credentialId, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating database credential {CredentialId}", credentialId);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .FirstOrDefaultAsync();

            if (credential == null)
            {
                _logger.LogWarning("Database credential {CredentialId} not found for organization {OrganizationId}", 
                    credentialId, organizationId);
                return false;
            }

            // Soft delete by setting IsActive to false
            credential.IsActive = false;
            credential.ModifiedOn = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);
            InvalidateActiveCache(organizationId);

            _logger.LogInformation("Soft deleted database credential {CredentialId} for organization {OrganizationId}", 
                credentialId, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting database credential {CredentialId}", credentialId);
            return false;
        }
    }

    public async Task<bool> HardDeleteAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .FirstOrDefaultAsync();

            if (credential == null)
            {
                _logger.LogWarning("Database credential {CredentialId} not found for organization {OrganizationId}", 
                    credentialId, organizationId);
                return false;
            }

            // Remove from Key Vault first - delete both password and connection string secrets
            bool passwordDeleted = true;
            bool connectionStringDeleted = true;
            
            // Delete SAP password secret
            try
            {
                _logger.LogInformation("Attempting to delete password secret URI: {PasswordSecretName}", credential.PasswordSecretName);
                
                // Extract tenant secret name from URI - this is the full tenant-specific name
                var tenantSecretName = ExtractTenantSecretNameFromUri(credential.PasswordSecretName);
                if (tenantSecretName != null)
                {
                    _logger.LogInformation("Extracted tenant secret name: {TenantSecretName}", tenantSecretName);
                    await _keyVaultService.DeleteSecretByExactNameAsync(tenantSecretName);
                    _logger.LogInformation("Successfully deleted Key Vault password secret {TenantSecretName} for credential {CredentialId}", 
                        tenantSecretName, credentialId);
                }
                else
                {
                    _logger.LogWarning("Could not extract tenant secret name from URI: {PasswordSecretName}", credential.PasswordSecretName);
                    passwordDeleted = false;
                }
            }
            catch (Exception ex)
            {
                passwordDeleted = false;
                _logger.LogError(ex, "Failed to delete Key Vault password secret. URI: {SecretUri}, Error: {Error}", 
                    credential.PasswordSecretName, ex.Message);
            }

            // Delete connection string secret
            if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
            {
                try
                {
                    _logger.LogInformation("Attempting to delete connection string secret URI: {ConnectionStringSecretName}", credential.ConnectionStringSecretName);
                    
                    // Extract tenant secret name from URI - this is the full tenant-specific name
                    var tenantSecretName = ExtractTenantSecretNameFromUri(credential.ConnectionStringSecretName);
                    if (tenantSecretName != null)
                    {
                        _logger.LogInformation("Extracted tenant secret name: {TenantSecretName}", tenantSecretName);
                        await _keyVaultService.DeleteSecretByExactNameAsync(tenantSecretName);
                        _logger.LogInformation("Successfully deleted Key Vault connection string secret {TenantSecretName} for credential {CredentialId}", 
                            tenantSecretName, credentialId);
                    }
                    else
                    {
                        _logger.LogWarning("Could not extract tenant secret name from URI: {ConnectionStringSecretName}", credential.ConnectionStringSecretName);
                        connectionStringDeleted = false;
                    }
                }
                catch (Exception ex)
                {
                    connectionStringDeleted = false;
                    _logger.LogError(ex, "Failed to delete Key Vault connection string secret. URI: {SecretUri}, Error: {Error}", 
                        credential.ConnectionStringSecretName, ex.Message);
                }
            }
            else
            {
                _logger.LogInformation("No connection string secret to delete for credential {CredentialId}", credentialId);
            }

            if (!passwordDeleted || !connectionStringDeleted)
            {
                _logger.LogWarning("Some Key Vault secrets could not be deleted for credential {CredentialId}, but continuing with database deletion", credentialId);
            }

            // Remove from database
            _context.DatabaseCredentials.Remove(credential);
            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);
            InvalidateActiveCache(organizationId);

            _logger.LogInformation("Hard deleted database credential {CredentialId} for organization {OrganizationId}", 
                credentialId, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hard deleting database credential {CredentialId}", credentialId);
            return false;
        }
    }

    public async Task<(bool Success, string Error)> TestConnectionAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return (false, "Database credential not found");
            }

            // Get connection string from Key Vault (secure approach)
            var connectionString = await BuildConnectionStringAsync(credentialId, organizationId);
            if (connectionString == null)
            {
                return (false, "Failed to retrieve connection string from Key Vault");
            }

            return await TestDatabaseConnectionAsync(connectionString, credential.DatabaseType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection for credential {CredentialId}", credentialId);
            return (false, $"Connection test failed: {ex.Message}");
        }
    }

    public async Task<DatabaseConnectionTestResult> TestConnectionBeforeCreateAsync(DatabaseCredentialModel model)
    {
        try
        {
            _logger.LogInformation("Testing connection before create - Type: {DatabaseType}, Server: {Server}", 
                model.DatabaseType, model.ServerInstance);
            _logger.LogInformation("Database credentials - Username: {DatabaseUsername}, Password Length: {PasswordLength}", 
                model.DatabaseUsername, model.DatabasePassword?.Length ?? 0);

            var connectionString = model.BuildConnectionStringTemplate()
                .Replace("{password}", model.DatabasePassword);
            _logger.LogInformation("Built connection string: {ConnectionString}", 
                connectionString.Replace(model.DatabasePassword ?? "", "***PASSWORD***"));

            var stopwatch = Stopwatch.StartNew();
            var (success, message) = await TestDatabaseConnectionAsync(connectionString, model.DatabaseType!.Value);
            stopwatch.Stop();

            if (success)
            {
                // Get additional database info for successful connections
                var serverInfo = await GetServerInfoAsync(connectionString, model.DatabaseType.Value);
                
                return new DatabaseConnectionTestResult
                {
                    Success = true,
                    Message = message,
                    ResponseTime = stopwatch.Elapsed,
                    ServerInfo = serverInfo,
                    DatabaseVersion = ExtractVersionFromMessage(message)
                };
            }
            else
            {
                return new DatabaseConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = message,
                    ResponseTime = stopwatch.Elapsed
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection before create");
            return new DatabaseConnectionTestResult
            {
                Success = false,
                ErrorMessage = $"Connection test failed: {ex.Message}"
            };
        }
    }

    private async Task<string> GetServerInfoAsync(string connectionString, DatabaseType databaseType)
    {
        try
        {
            switch (databaseType)
            {
                case DatabaseType.MSSQL:
                    using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        using var command = connection.CreateCommand();
                        command.CommandText = "SELECT @@SERVERNAME";
                        var serverName = await command.ExecuteScalarAsync();
                        return $"Server: {serverName}";
                    }

                case DatabaseType.HANA:
                    using (var connection = new HanaConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        using var command = connection.CreateCommand();
                        command.CommandText = "SELECT HOST, DATABASE_NAME FROM SYS.M_DATABASES WHERE ACTIVE_STATUS = 'YES' AND ROWNUM = 1";
                        using var reader = await command.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            var host = reader.GetString(0);
                            var dbName = reader.GetString(1);
                            return $"Host: {host}, Database: {dbName}";
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get server info for {DatabaseType}", databaseType);
        }

        return "Server information not available";
    }

    private string? ExtractVersionFromMessage(string message)
    {
        if (message.Contains("HANA"))
        {
            var hanaIndex = message.IndexOf("HANA");
            if (hanaIndex != -1)
            {
                return message.Substring(hanaIndex).Trim();
            }
        }
        return null;
    }

    public async Task<(bool Success, string Error)> TestConnectionAsync(DatabaseCredentialModel model)
    {
        try
        {
            var connectionString = model.BuildConnectionStringTemplate()
                .Replace("{password}", model.DatabasePassword);

            return await TestDatabaseConnectionAsync(connectionString, model.DatabaseType!.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection for model {FriendlyName}", model.FriendlyName);
            return (false, $"Connection test failed: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Error)> TestDatabaseConnectionAsync(string connectionString, DatabaseType databaseType)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            switch (databaseType)
            {
                case DatabaseType.MSSQL:
                    using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        stopwatch.Stop();
                        _logger.LogInformation("SQL Server connection test successful in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                        return (true, $"Connection successful ({stopwatch.ElapsedMilliseconds}ms)");
                    }

                case DatabaseType.HANA:
                    using (var connection = new HanaConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        stopwatch.Stop();
                        
                        // Get database version info
                        using var command = connection.CreateCommand();
                        command.CommandText = "SELECT VERSION FROM SYS.M_DATABASE";
                        var version = await command.ExecuteScalarAsync();
                        
                        _logger.LogInformation("SAP HANA connection test successful in {ElapsedMs}ms, Version: {Version}", 
                            stopwatch.ElapsedMilliseconds, version);
                        return (true, $"Connection successful ({stopwatch.ElapsedMilliseconds}ms) - HANA {version}");
                    }

                default:
                    return (false, $"Unsupported database type: {databaseType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database connection test failed for {DatabaseType}", databaseType);
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<DatabaseConnectionTestResult> TestConnectionAsync(Guid credentialId)
    {
        try
        {
            var credential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.IsActive)
                .FirstOrDefaultAsync();

            if (credential == null)
            {
                return new DatabaseConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = "Database credential not found"
                };
            }

            var (success, error) = await TestConnectionAsync(credentialId, credential.OrganizationId);
            return new DatabaseConnectionTestResult
            {
                Success = success,
                ErrorMessage = success ? null : error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection for credential {CredentialId}", credentialId);
            return new DatabaseConnectionTestResult
            {
                Success = false,
                ErrorMessage = $"Connection test failed: {ex.Message}"
            };
        }
    }

    public async Task<string?> GetSAPPasswordAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Extract secret name from URI (backward compatibility)
            var passwordSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.PasswordSecretName) ?? credential.PasswordSecretName;
            
            return await _keyVaultService.GetSecretAsync(passwordSecretName, organizationId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SAP password for credential {CredentialId}", credentialId);
            return null;
        }
    }

    public async Task<bool> UpdateSAPPasswordAsync(Guid credentialId, string newPassword, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            _logger.LogInformation("UpdateSAPPasswordAsync called for credential {CredentialId}", credentialId);
            
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                _logger.LogWarning("Credential {CredentialId} not found for organization {OrganizationId}", credentialId, organizationId);
                throw new ArgumentException($"Database credential not found or access denied");
            }

            _logger.LogInformation("Found credential {FriendlyName}, IsActive: {IsActive}", credential.FriendlyName, credential.IsActive);

            // Check if credential is active - inactive credentials shouldn't have password updates
            if (!credential.IsActive)
            {
                _logger.LogWarning("Attempted to update password for inactive credential {CredentialId} ({FriendlyName})", credentialId, credential.FriendlyName);
                throw new InvalidOperationException($"Cannot update password for inactive credential '{credential.FriendlyName}'. Please activate the credential first.");
            }

            _logger.LogInformation("Updating Key Vault secret by URI {SecretUri}", credential.PasswordSecretName);
            var (success, newVersionUri) = await _keyVaultService.UpdateSecretByUriAsync(credential.PasswordSecretName, newPassword, organizationId.ToString());
            if (!success)
            {
                _logger.LogError("Failed to update password in Key Vault for credential {CredentialId}", credentialId);
                throw new InvalidOperationException("Failed to update password in Key Vault. The secret may be in an invalid state or Key Vault permissions may be insufficient.");
            }

            // Update to the new version URI in SQL database
            if (!string.IsNullOrEmpty(newVersionUri))
            {
                credential.PasswordSecretName = newVersionUri;
                _logger.LogInformation("Updated SAP password secret to new version URI: {NewVersionUri}", newVersionUri);
            }

            _logger.LogInformation("SAP password updated successfully in Key Vault (new version created for existing secret)");

            // Note: Connection string is not updated here as this method only updates SAP password
            // Connection string uses database password, not SAP password
            credential.ModifiedOn = DateTime.UtcNow;
            credential.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);
            InvalidateActiveCache(organizationId);

            _logger.LogInformation("Successfully updated password for credential {CredentialId}", credentialId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SAP password for credential {CredentialId}", credentialId);
            throw; // Re-throw to preserve the original exception message
        }
    }

    public async Task<string?> BuildConnectionStringAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Try to get connection string from Key Vault first (new secure approach)
            if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
            {
                // Extract secret name from URI (backward compatibility)
                var connectionStringSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConnectionStringSecretName) ?? credential.ConnectionStringSecretName;
                
                var connectionString = await _keyVaultService.GetSecretAsync(connectionStringSecretName, organizationId.ToString());
                if (connectionString != null)
                {
                    return connectionString;
                }
            }

            // Fallback to legacy approach for backward compatibility
            if (!string.IsNullOrEmpty(credential.ConnectionString))
            {
                _logger.LogWarning("Using legacy connection string for credential {CredentialId} - consider migrating to Key Vault storage", credentialId);
                return credential.ConnectionString;
            }

            _logger.LogWarning("No connection string found for credential {CredentialId}", credentialId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building connection string for credential {CredentialId}", credentialId);
            return null;
        }
    }

    /// <summary>
    /// Updates general settings (non-password properties) and syncs with Key Vault metadata
    /// </summary>
    public async Task<bool> UpdateGeneralSettingsAsync(Guid credentialId, DatabaseCredentialGeneralSettingsModel model, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            _logger.LogInformation("=== DatabaseCredentialService.UpdateGeneralSettingsAsync ===");
            _logger.LogInformation("  CredentialId: {CredentialId}", credentialId);
            _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
            _logger.LogInformation("  ModifiedBy: {ModifiedBy}", modifiedBy);

            var existingCredential = await _context.DatabaseCredentials
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .FirstOrDefaultAsync();

            if (existingCredential == null)
            {
                _logger.LogWarning("Database credential {CredentialId} not found for organization {OrganizationId}", credentialId, organizationId);
                return false;
            }

            // Update general properties
            existingCredential.FriendlyName = model.FriendlyName;
            existingCredential.Description = model.Description;
            existingCredential.IsActive = model.IsActive;
            existingCredential.SAPUsername = model.SAPUsername;
            existingCredential.DatabaseUsername = model.DatabaseUsername;
            existingCredential.ServerInstance = model.ServerInstance;
            existingCredential.InstanceName = model.InstanceName;
            existingCredential.DatabaseName = model.DatabaseName;
            existingCredential.Port = model.Port;
            existingCredential.CurrentSchema = model.CurrentSchema;
            existingCredential.Encrypt = model.Encrypt;
            existingCredential.SSLValidateCertificate = model.SSLValidateCertificate;
            existingCredential.TrustServerCertificate = model.TrustServerCertificate;
            existingCredential.ModifiedBy = modifiedBy;
            existingCredential.ModifiedOn = DateTime.UtcNow;

            // STEP 1: Update SQL Database first (we've already modified the existingCredential object above)
            await _context.SaveChangesAsync();
            _logger.LogInformation("✅ SQL Database updated successfully for credential {CredentialId}", credentialId);

            // STEP 2: Now sync the changes to Key Vault to match the SQL database state
            _logger.LogInformation("🔄 Starting Key Vault sync to match SQL database changes...");
            var keyVaultSyncSuccess = await SyncKeyVaultMetadataAsync(existingCredential, organizationId.ToString());
            
            if (keyVaultSyncSuccess)
            {
                _logger.LogInformation("✅ Key Vault sync completed successfully - both SQL and Key Vault are now in sync");
                
                // STEP 3: Save any URI updates that happened during Key Vault sync
                _logger.LogInformation("🔄 Saving any updated secret URIs to database...");
                await _context.SaveChangesAsync();
                _logger.LogInformation("✅ Updated secret URIs saved to database");
            }
            else
            {
                _logger.LogWarning("⚠️ Key Vault sync had issues - SQL database updated but some Key Vault secrets may not reflect changes");
            }
            InvalidateCache(organizationId);

            _logger.LogInformation("Updated general settings for credential {CredentialId} in organization {OrganizationId}", credentialId, organizationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating general settings for credential {CredentialId}", credentialId);
            return false;
        }
    }

    /// <summary>
    /// Syncs metadata between SQL database and Key Vault secrets
    /// </summary>
    private async Task<bool> SyncKeyVaultMetadataAsync(DatabaseCredential credential, string organizationId)
    {
        try
        {
            _logger.LogInformation("=== SyncKeyVaultMetadataAsync for credential {CredentialId} ===", credential.Id);
            _logger.LogInformation("  IsActive: {IsActive}", credential.IsActive);
            _logger.LogInformation("  PasswordSecretName: {PasswordSecretName}", credential.PasswordSecretName);
            _logger.LogInformation("  ConnectionStringSecretName: {ConnectionStringSecretName}", credential.ConnectionStringSecretName);
            
            int successCount = 0;
            int attemptCount = 0;
            
            // Update SAP password secret metadata (this should always exist)
            if (!string.IsNullOrEmpty(credential.PasswordSecretName))
            {
                attemptCount++;
                _logger.LogInformation("Attempting to sync password secret metadata...");
                try
                {
                    bool passwordSyncResult = await UpdateSecretMetadataAsync(credential.PasswordSecretName, credential, organizationId);
                    if (passwordSyncResult)
                    {
                        successCount++;
                        _logger.LogInformation("✅ Password secret metadata sync completed successfully");
                    }
                    else
                    {
                        _logger.LogWarning("❌ Password secret metadata sync failed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Exception during password secret metadata sync");
                }
            }
            
            // Update connection string secret metadata (may not exist for older credentials)
            if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
            {
                attemptCount++;
                _logger.LogInformation("Attempting to sync connection string secret metadata...");
                try
                {
                    bool connectionStringSyncResult = await UpdateSecretMetadataAsync(credential.ConnectionStringSecretName, credential, organizationId);
                    if (connectionStringSyncResult)
                    {
                        successCount++;
                        _logger.LogInformation("✅ Connection string secret metadata sync completed successfully");
                    }
                    else
                    {
                        _logger.LogWarning("❌ Connection string secret metadata sync failed - this may be expected for older credentials");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ Exception during connection string secret metadata sync - this may be expected for older credentials");
                }
            }
            
            _logger.LogInformation("Key Vault metadata sync summary: {SuccessCount}/{AttemptCount} secrets updated", successCount, attemptCount);
            
            // Return true if we successfully updated at least one secret, or if there were no secrets to update
            bool overallSuccess = (attemptCount == 0) || (successCount > 0);
            _logger.LogInformation("Overall Key Vault sync result: {Success}", overallSuccess ? "SUCCESS" : "FAILED");
            return overallSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Key Vault metadata for credential {CredentialId}", credential.Id);
            return false;
        }
    }

    /// <summary>
    /// Updates Key Vault secret metadata and enabled status
    /// </summary>
    private async Task<bool> UpdateSecretMetadataAsync(string secretUri, DatabaseCredential credential, string organizationId)
    {
        try
        {
            _logger.LogInformation("=== UpdateSecretMetadataAsync Debug ===");
            _logger.LogInformation("  SecretUri: {SecretUri}", secretUri);
            _logger.LogInformation("  Credential IsActive: {IsActive}", credential.IsActive);
            
            // Extract the tenant secret name from the URI (this is what's actually stored in Key Vault)
            var uri = new Uri(secretUri);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            if (pathSegments.Length < 2 || pathSegments[0] != "secrets")
            {
                _logger.LogWarning("Invalid secret URI format: {SecretUri}", secretUri);
                return false;
            }
            
            var tenantSecretName = pathSegments[1]; // This is the actual secret name in Key Vault
            _logger.LogInformation("  Extracted tenant secret name: {TenantSecretName}", tenantSecretName);
            
            // The tenantSecretName we extracted from the URI IS the actual secret name in Key Vault
            // We don't need to regenerate it through GenerateTenantSecretName - just use it directly
            _logger.LogInformation("  Using tenant secret name directly from URI: {TenantSecretName}", tenantSecretName);
            
            // Get current secret value and properties by calling Key Vault directly with the tenant secret name
            var (currentSecret, currentEnabledStatus) = await _keyVaultService.GetSecretWithPropertiesAsync(tenantSecretName);
            _logger.LogInformation("  Retrieved secret - HasValue: {HasValue}, Enabled: {Enabled}", currentSecret != null, currentEnabledStatus);
            
            if (currentSecret != null)
            {
                bool isCurrentlyActive = currentEnabledStatus == true;
                _logger.LogInformation("  Secret current state: {State} (IsActive: {IsActive})", isCurrentlyActive ? "ACTIVE" : "INACTIVE", currentEnabledStatus);

                // Check if we need to update the status
                if (isCurrentlyActive == credential.IsActive)
                {
                    _logger.LogInformation("✅ Secret status already matches SQL database - no update needed");
                    return true; // Already in correct state
                }
                
                // Update secret metadata to match SQL database status
                _logger.LogInformation("🔄 Updating secret status to match SQL database: {IsActive}", credential.IsActive);
                
                // Prepare additional tags with database credential information
                var additionalTags = new Dictionary<string, string>
                {
                    { "friendlyName", credential.FriendlyName },
                    { "databaseType", credential.DatabaseType.ToString() },
                    { "databaseName", credential.DatabaseName }
                };
                
                // Update secret metadata using the UpdateSecretMetadataByUriAsync method
                var (success, newVersionUri) = await _keyVaultService.UpdateSecretMetadataByUriAsync(
                    secretUri, 
                    currentSecret, 
                    organizationId, 
                    credential.IsActive, // This will be stored in the isActive tag
                    additionalTags);
                
                if (success && !string.IsNullOrEmpty(newVersionUri))
                {
                    _logger.LogInformation("✅ Successfully updated Key Vault metadata for secret {TenantSecretName}, isActive: {IsActive}, new version URI: {NewVersionUri}", 
                        tenantSecretName, credential.IsActive, newVersionUri);
                        
                    // Update the URI in the database credential to point to the new version
                    if (secretUri == credential.PasswordSecretName)
                    {
                        credential.PasswordSecretName = newVersionUri;
                        _logger.LogInformation("Updated PasswordSecretName to new version URI");
                    }
                    else if (secretUri == credential.ConnectionStringSecretName)
                    {
                        credential.ConnectionStringSecretName = newVersionUri;
                        _logger.LogInformation("Updated ConnectionStringSecretName to new version URI");
                    }
                    
                    return true; // Success!
                }
                else
                {
                    _logger.LogError("❌ Failed to update Key Vault metadata for secret {TenantSecretName}", tenantSecretName);
                    return false; // Key Vault update failed
                }
            }
            else
            {
                _logger.LogWarning("Could not retrieve current secret value for {TenantSecretName} to update metadata", tenantSecretName);
                return false; // Secret doesn't exist, so sync failed
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while updating secret metadata for {SecretUri}", secretUri);
            return false; // Exception occurred, so sync failed
        }
    }


    private void InvalidateCache(Guid organizationId)
    {
        var cacheKey = $"org_credentials_{organizationId}";
        _cache.Remove(cacheKey);
    }

    /// <summary>
    /// Extracts the tenant secret name from a Key Vault secret URI
    /// Format: https://vault-name.vault.azure.net/secrets/tenant-secret-name/version
    /// </summary>
    private string? ExtractTenantSecretNameFromUri(string? secretUri)
    {
        if (string.IsNullOrWhiteSpace(secretUri))
            return null;

        try
        {
            var uri = new Uri(secretUri);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            
            // Should have at least "secrets" and "tenant-secret-name"
            if (pathSegments.Length >= 2 && pathSegments[0] == "secrets")
            {
                return pathSegments[1]; // This is the full tenant secret name
            }
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid URI format when extracting tenant secret name: {SecretUri}", secretUri);
        }
        
        return null;
    }

    private void InvalidateActiveCache(Guid organizationId)
    {
        var cacheKey = $"org_active_credentials_{organizationId}";
        _cache.Remove(cacheKey);
    }
}