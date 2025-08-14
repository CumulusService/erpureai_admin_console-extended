using AdminConsole.Data;
using AdminConsole.Models;
using AdminConsole.Services;
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
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<DatabaseCredentialService> _logger;
    private readonly IMemoryCache _cache;

    public DatabaseCredentialService(
        AdminConsoleDbContext context,
        IKeyVaultService keyVaultService,
        IDataIsolationService dataIsolationService,
        IOrganizationService organizationService,
        ILogger<DatabaseCredentialService> logger,
        IMemoryCache cache)
    {
        _context = context;
        _keyVaultService = keyVaultService;
        _dataIsolationService = dataIsolationService;
        _organizationService = organizationService;
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
        
        // CRITICAL DEBUG: Check if SAP configuration fields are received
        _logger.LogInformation("  SAP Configuration Fields DEBUG:");
        _logger.LogInformation("    SAPServiceLayerHostname: '{SAPServiceLayerHostname}'", model.SAPServiceLayerHostname ?? "NULL");
        _logger.LogInformation("    SAPAPIGatewayHostname: '{SAPAPIGatewayHostname}'", model.SAPAPIGatewayHostname ?? "NULL");
        _logger.LogInformation("    SAPBusinessOneWebClientHost: '{SAPBusinessOneWebClientHost}'", model.SAPBusinessOneWebClientHost ?? "NULL");
        _logger.LogInformation("    DocumentCode: '{DocumentCode}'", model.DocumentCode ?? "NULL");
        
        // Validate HANA consistency before creating
        var (isValid, errorMessage) = model.ValidateHANAConsistency();
        if (!isValid)
        {
            _logger.LogError("HANA validation failed for credential: {Error}", errorMessage);
            throw new ArgumentException(errorMessage);
        }
        
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
                
                // SAP Configuration fields (now mandatory)
                SAPServiceLayerHostname = model.SAPServiceLayerHostname ?? string.Empty,
                SAPAPIGatewayHostname = model.SAPAPIGatewayHostname ?? string.Empty,
                SAPBusinessOneWebClientHost = model.SAPBusinessOneWebClientHost ?? string.Empty,
                DocumentCode = model.DocumentCode ?? string.Empty,
                
                // Set SAPServiceLayerDBSchema based on database type
                SAPServiceLayerDBSchema = model.DatabaseType == DatabaseType.HANA 
                    ? model.CurrentSchema 
                    : model.DatabaseName,
                
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                CreatedBy = createdBy,
                ModifiedBy = createdBy
            };

            // CRITICAL DEBUG: Verify credential entity has SAP fields populated
            _logger.LogInformation("  Created credential entity SAP fields:");
            _logger.LogInformation("    Entity.SAPServiceLayerHostname: '{SAPServiceLayerHostname}'", credential.SAPServiceLayerHostname);
            _logger.LogInformation("    Entity.SAPAPIGatewayHostname: '{SAPAPIGatewayHostname}'", credential.SAPAPIGatewayHostname);
            _logger.LogInformation("    Entity.SAPBusinessOneWebClientHost: '{SAPBusinessOneWebClientHost}'", credential.SAPBusinessOneWebClientHost);
            _logger.LogInformation("    Entity.DocumentCode: '{DocumentCode}'", credential.DocumentCode);

            // Skip creating separate password secret - we'll use consolidated secret approach only
            _logger.LogInformation("  Skipping separate password secret creation - using consolidated secret approach");
            credential.PasswordSecretName = string.Empty; // Will be retrieved from consolidated secret

            // Skip creating separate connection string secret - consolidated secret will contain connection string as tag
            _logger.LogInformation("  Skipping separate connection string secret creation - using consolidated secret approach");
            var fullConnectionString = credential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
            _logger.LogInformation("  Built connection string (length: {Length}) for consolidated secret", fullConnectionString.Length);
            
            // Keep connection string empty in database for security (deprecated field) 
            credential.ConnectionString = string.Empty;
            credential.ConnectionStringSecretName = string.Empty; // Will be retrieved from consolidated secret tags

            // Create consolidated secret (NEW APPROACH) - SAP password as value, connection string as tag
            var consolidatedSecretName = credential.GenerateConsolidatedSecretName();
            _logger.LogInformation("  Generated consolidated secret name: {ConsolidatedSecretName}", consolidatedSecretName);
            
            _logger.LogInformation("  Calling KeyVaultService.SetSecretAsync for consolidated secret...");
            var consolidatedKeyVaultResult = await CreateConsolidatedSecretAsync(consolidatedSecretName, model.SAPPassword, fullConnectionString, organizationId.ToString(), credential);
            _logger.LogInformation("  KeyVaultService.SetSecretAsync for consolidated secret returned: {Result}", consolidatedKeyVaultResult);
            
            if (consolidatedKeyVaultResult)
            {
                // Get the consolidated secret URI and store it
                _logger.LogInformation("  Retrieving consolidated secret URI...");
                var consolidatedSecretUri = await _keyVaultService.GetSecretIdentifierAsync(consolidatedSecretName, organizationId.ToString());
                if (consolidatedSecretUri != null)
                {
                    credential.ConsolidatedSecretName = consolidatedSecretUri;
                    _logger.LogInformation("  Stored consolidated secret URI: {ConsolidatedSecretUri}", consolidatedSecretUri);
                }
                else
                {
                    _logger.LogWarning("  Failed to retrieve consolidated secret URI, storing secret name as fallback");
                    credential.ConsolidatedSecretName = consolidatedSecretName;
                }
            }
            else
            {
                _logger.LogWarning("Failed to store consolidated secret in Key Vault for credential {FriendlyName} - continuing with separate secrets only", model.FriendlyName);
                credential.ConsolidatedSecretName = string.Empty;
            }

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
            
            // Update SAP Configuration fields
            existingCredential.SAPServiceLayerHostname = model.SAPServiceLayerHostname ?? string.Empty;
            existingCredential.SAPAPIGatewayHostname = model.SAPAPIGatewayHostname ?? string.Empty;
            existingCredential.SAPBusinessOneWebClientHost = model.SAPBusinessOneWebClientHost ?? string.Empty;
            existingCredential.DocumentCode = model.DocumentCode ?? string.Empty;
            
            // Update SAPServiceLayerDBSchema based on database type
            existingCredential.SAPServiceLayerDBSchema = model.DatabaseType == DatabaseType.HANA 
                ? model.CurrentSchema 
                : model.DatabaseName;
            
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
                
                // Skip separate connection string secret updates - using consolidated secret approach only
                _logger.LogInformation("Using consolidated secret approach - skipping separate connection string secret operations");
            }
            else
            {
                _logger.LogInformation("No database password provided - keeping existing connection string");
            }

            // Update consolidated secret if passwords are provided and consolidated secret exists
            if ((!string.IsNullOrEmpty(model.SAPPassword) || !string.IsNullOrEmpty(model.DatabasePassword)) 
                && !string.IsNullOrEmpty(existingCredential.ConsolidatedSecretName))
            {
                _logger.LogInformation("Updating consolidated secret in Key Vault for credential {FriendlyName}", model.FriendlyName);
                
                try
                {
                    // Get current SAP password if not provided in update
                    string currentSAPPassword = model.SAPPassword;
                    if (string.IsNullOrEmpty(currentSAPPassword))
                    {
                        try
                        {
                            currentSAPPassword = await GetSAPPasswordAsync(existingCredential.Id, organizationId);
                            if (string.IsNullOrEmpty(currentSAPPassword))
                            {
                                // Check if this is a new credential with no secrets created yet
                                if (string.IsNullOrEmpty(existingCredential.ConsolidatedSecretName) && string.IsNullOrEmpty(existingCredential.PasswordSecretName))
                                {
                                    _logger.LogInformation("New credential with no existing secrets detected. Creating consolidated secret for the first time during update.");
                                    // Use the provided SAP password from the update (this is likely the first real SAP password)
                                    if (!string.IsNullOrEmpty(model.SAPPassword))
                                    {
                                        currentSAPPassword = model.SAPPassword;
                                        _logger.LogInformation("Using SAP password from update request for new consolidated secret creation");
                                    }
                                    else
                                    {
                                        _logger.LogError("Cannot create consolidated secret for new credential - no SAP password provided in update");
                                        throw new InvalidOperationException("Unable to retrieve current SAP password and no new SAP password provided. Please provide a SAP password to create the consolidated secret.");
                                    }
                                }
                                else
                                {
                                    _logger.LogError("GetSAPPasswordAsync returned null or empty for credential {CredentialId}", existingCredential.Id);
                                    throw new InvalidOperationException("Unable to retrieve current SAP password. Please provide a new SAP password to update the consolidated secret.");
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Retrieved current SAP password from existing secret");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to retrieve current SAP password for credential {CredentialId}", existingCredential.Id);
                            _logger.LogWarning("Skipping consolidated secret update due to SAP password retrieval failure. Separate secrets will still be updated.");
                            // Skip consolidated secret update but continue with separate secret updates
                            goto SkipConsolidatedUpdate;
                        }
                    }
                    
                    // Build connection string with current or new database password
                    string connectionString;
                    if (!string.IsNullOrEmpty(model.DatabasePassword))
                    {
                        connectionString = existingCredential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
                        _logger.LogInformation("Built connection string with new database password");
                    }
                    else
                    {
                        try
                        {
                            connectionString = await BuildConnectionStringAsync(existingCredential.Id, organizationId);
                            if (string.IsNullOrEmpty(connectionString))
                            {
                                // Check if this is a new credential with no secrets created yet
                                if (string.IsNullOrEmpty(existingCredential.ConsolidatedSecretName) && string.IsNullOrEmpty(existingCredential.ConnectionStringSecretName))
                                {
                                    _logger.LogInformation("New credential with no existing connection string secret detected. Building connection string from template during update.");
                                    // Use the provided database password from the update to build connection string
                                    if (!string.IsNullOrEmpty(model.DatabasePassword))
                                    {
                                        connectionString = existingCredential.BuildConnectionStringTemplate().Replace("{password}", model.DatabasePassword);
                                        _logger.LogInformation("Built connection string using database password from update request for new consolidated secret creation");
                                    }
                                    else
                                    {
                                        _logger.LogError("Cannot build connection string for new credential - no database password provided in update");
                                        throw new InvalidOperationException("Unable to retrieve current connection string and no new database password provided. Please provide a database password to create the consolidated secret.");
                                    }
                                }
                                else
                                {
                                    _logger.LogError("BuildConnectionStringAsync returned null or empty for credential {CredentialId}", existingCredential.Id);
                                    throw new InvalidOperationException("Unable to retrieve current connection string. Please provide a new database password to update the consolidated secret.");
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Retrieved current connection string from existing secret");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to build current connection string for credential {CredentialId}", existingCredential.Id);
                            _logger.LogWarning("Skipping consolidated secret update due to connection string build failure. Separate secrets will still be updated.");
                            // Skip consolidated secret update but continue with separate secret updates
                            goto SkipConsolidatedUpdate;
                        }
                    }
                    
                    // Update consolidated secret with new version (using metadata update to include tags)
                    var (success, newVersionUri) = await _keyVaultService.UpdateSecretMetadataByUriAsync(
                        existingCredential.ConsolidatedSecretName, 
                        currentSAPPassword, 
                        organizationId.ToString(),
                        true, // enabled
                        new Dictionary<string, string> { { "connectionString", connectionString } });
                        
                    if (success && !string.IsNullOrEmpty(newVersionUri))
                    {
                        existingCredential.ConsolidatedSecretName = newVersionUri;
                        _logger.LogInformation("Updated consolidated secret to new version URI: {NewVersionUri}", newVersionUri);
                    }
                    else if (!success)
                    {
                        _logger.LogError("Failed to update consolidated secret in Key Vault for credential {FriendlyName}", model.FriendlyName);
                        throw new InvalidOperationException("Failed to update consolidated secret in Key Vault. Please check that the Key Vault is accessible and your service principal has the required permissions (Secret Officer role).");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating consolidated secret for credential {FriendlyName}", model.FriendlyName);
                    _logger.LogWarning("Consolidated secret update failed, but separate secret updates succeeded. System will continue to function using separate secrets.");
                    // Don't throw - let separate secret updates work
                }
            }
            else if (!string.IsNullOrEmpty(existingCredential.ConsolidatedSecretName))
            {
                _logger.LogInformation("No password updates provided - consolidated secret remains unchanged");
            }
            
            SkipConsolidatedUpdate:
            // Continue with database save even if consolidated secret update failed

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
        // Use transaction to ensure atomic operations
        using var transaction = await _context.Database.BeginTransactionAsync();
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

            // Remove from Key Vault first - delete password, connection string, and consolidated secrets
            bool passwordDeleted = true;
            bool connectionStringDeleted = true;
            bool consolidatedSecretDeleted = true;
            
            // DEBUG: Log all secret names that will be attempted for deletion
            _logger.LogInformation("=== Key Vault Secret Deletion Debug Info ===");
            _logger.LogInformation("  PasswordSecretName: '{PasswordSecretName}'", credential.PasswordSecretName ?? "NULL");
            _logger.LogInformation("  ConnectionStringSecretName: '{ConnectionStringSecretName}'", credential.ConnectionStringSecretName ?? "NULL");
            _logger.LogInformation("  ConsolidatedSecretName: '{ConsolidatedSecretName}'", credential.ConsolidatedSecretName ?? "NULL");
            
            // Delete SAP password secret
            try
            {
                _logger.LogInformation("Attempting to delete password secret URI: {PasswordSecretName}", credential.PasswordSecretName);
                
                // Extract tenant secret name from URI - this is the full tenant-specific name
                var tenantSecretName = ExtractTenantSecretNameFromUri(credential.PasswordSecretName);
                if (tenantSecretName != null)
                {
                    _logger.LogInformation("Extracted tenant secret name: {TenantSecretName}", tenantSecretName);
                    var deleteResult = await _keyVaultService.DeleteSecretByExactNameAsync(tenantSecretName);
                    if (deleteResult)
                    {
                        // Also purge the legacy secret
                        await Task.Delay(1000);
                        await _keyVaultService.PurgeSecretAsync(tenantSecretName);
                    }
                    _logger.LogInformation("Successfully deleted and purged Key Vault password secret {TenantSecretName} for credential {CredentialId}", 
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
                        var deleteResult = await _keyVaultService.DeleteSecretByExactNameAsync(tenantSecretName);
                        if (deleteResult)
                        {
                            // Also purge the legacy secret
                            await Task.Delay(1000);
                            await _keyVaultService.PurgeSecretAsync(tenantSecretName);
                        }
                        _logger.LogInformation("Successfully deleted and purged Key Vault connection string secret {TenantSecretName} for credential {CredentialId}", 
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

            // Delete consolidated secret (NEW approach)
            if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
            {
                try
                {
                    _logger.LogInformation("Attempting to delete consolidated secret: {ConsolidatedSecretName}", credential.ConsolidatedSecretName);
                    
                    // Extract just the secret name from the full URI/name
                    var secretName = ExtractSecretNameFromConsolidatedSecretName(credential.ConsolidatedSecretName);
                    if (secretName == null)
                    {
                        _logger.LogWarning("Could not extract secret name from consolidated secret: {ConsolidatedSecretName}", credential.ConsolidatedSecretName);
                        consolidatedSecretDeleted = false;
                    }
                    else
                    {
                    
                    _logger.LogInformation("Extracted secret name: {SecretName}", secretName);
                    
                    // DEBUG: Check if secret exists before deletion
                    try
                    {
                        var (value, isEnabled) = await _keyVaultService.GetSecretWithPropertiesAsync(secretName);
                        _logger.LogInformation("DEBUG: Secret exists before deletion - Value: {HasValue}, Enabled: {IsEnabled}", 
                            !string.IsNullOrEmpty(value), isEnabled);
                    }
                    catch (Exception debugEx)
                    {
                        _logger.LogInformation("DEBUG: Could not check secret before deletion: {Error}", debugEx.Message);
                    }
                    
                    var deletionResult = await _keyVaultService.DeleteSecretByExactNameAsync(secretName);
                    _logger.LogInformation("Key Vault deletion result: {Result} for secret {SecretName}", 
                        deletionResult, secretName);
                    
                    // If deletion succeeded, also purge the secret to completely remove it
                    if (deletionResult)
                    {
                        _logger.LogInformation("Attempting to purge secret completely: {SecretName}", secretName);
                        await Task.Delay(3000); // Wait for Azure to process the deletion
                        
                        var purgeResult = await _keyVaultService.PurgeSecretAsync(secretName);
                        _logger.LogInformation("Key Vault purge result: {Result} for secret {SecretName}", 
                            purgeResult, secretName);
                        
                        if (!purgeResult)
                        {
                            _logger.LogWarning("Purge failed for secret {SecretName} - secret may still be in soft-deleted state", secretName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Deletion failed for secret {SecretName} - skipping purge attempt", secretName);
                    }
                    
                    // DEBUG: Check if secret still exists after deletion and purging
                    try
                    {
                        await Task.Delay(1000); // Wait a bit for Azure propagation
                        var (value, isEnabled) = await _keyVaultService.GetSecretWithPropertiesAsync(secretName);
                        _logger.LogWarning("DEBUG: Secret still exists after deletion+purge - Value: {HasValue}, Enabled: {IsEnabled}", 
                            !string.IsNullOrEmpty(value), isEnabled);
                    }
                    catch (Exception debugEx)
                    {
                        _logger.LogInformation("DEBUG: Secret properly deleted and purged (could not retrieve): {Error}", debugEx.Message);
                    }
                    
                    _logger.LogInformation("Successfully deleted Key Vault consolidated secret {SecretName} for credential {CredentialId}", 
                        secretName, credentialId);
                    }
                }
                catch (Exception ex)
                {
                    consolidatedSecretDeleted = false;
                    _logger.LogError(ex, "Failed to delete Key Vault consolidated secret. Name: {SecretName}, Error: {Error}", 
                        credential.ConsolidatedSecretName, ex.Message);
                }
            }
            else
            {
                _logger.LogInformation("No consolidated secret to delete for credential {CredentialId}", credentialId);
            }

            if (!passwordDeleted || !connectionStringDeleted || !consolidatedSecretDeleted)
            {
                _logger.LogWarning("Some Key Vault secrets could not be deleted for credential {CredentialId}, but continuing with database deletion", credentialId);
            }

            // CRITICAL FIX: Clean up all related data before deleting credential
            _logger.LogInformation("Cleaning up related data for credential {CredentialId}", credentialId);
            
            // 1. Remove all UserDatabaseAssignments for this credential
            var userAssignments = await _context.UserDatabaseAssignments
                .Where(uda => uda.DatabaseCredentialId == credentialId && uda.OrganizationId == organizationId)
                .ToListAsync();
                
            if (userAssignments.Any())
            {
                _logger.LogInformation("Removing {Count} UserDatabaseAssignments for credential {CredentialId}", 
                    userAssignments.Count, credentialId);
                _context.UserDatabaseAssignments.RemoveRange(userAssignments);
                
                // 2. Remove credential ID from OnboardedUsers.AssignedDatabaseIds JSON arrays
                var affectedUserIds = userAssignments.Select(ua => ua.UserId).Distinct().ToList();
                var affectedUsers = await _context.OnboardedUsers
                    .Where(u => affectedUserIds.Contains(u.OnboardedUserId) && u.OrganizationId == organizationId)
                    .ToListAsync();
                    
                foreach (var user in affectedUsers)
                {
                    if (user.AssignedDatabaseIds.Contains(credentialId))
                    {
                        user.AssignedDatabaseIds.Remove(credentialId);
                        _logger.LogInformation("Removed credential {CredentialId} from user {UserId} AssignedDatabaseIds", 
                            credentialId, user.OnboardedUserId);
                    }
                }
            }

            // Remove from database
            _context.DatabaseCredentials.Remove(credential);
            await _context.SaveChangesAsync();

            InvalidateCache(organizationId);
            InvalidateActiveCache(organizationId);

            // Commit the transaction
            await transaction.CommitAsync();
            
            _logger.LogInformation("Hard deleted database credential {CredentialId} for organization {OrganizationId}", 
                credentialId, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            // Rollback transaction on any error
            await transaction.RollbackAsync();
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
            // For HANA connections, ensure native libraries are loaded first
            if (databaseType == DatabaseType.HANA)
            {
                _logger.LogInformation("🔧 Ensuring SAP HANA native libraries are loaded...");
                HanaNativeLibraryLoader.EnsureLibrariesLoaded();
                
                // Log diagnostic information
                var diagnostics = HanaNativeLibraryLoader.GetLibraryDiagnostics();
                _logger.LogInformation("📋 SAP HANA Library Diagnostics:\n{Diagnostics}", diagnostics);
            }

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

            // Log what's actually stored in the database
            _logger.LogInformation("Database credential fields - ConsolidatedSecretName: '{ConsolidatedSecretName}', PasswordSecretName: '{PasswordSecretName}'", 
                credential.ConsolidatedSecretName, credential.PasswordSecretName);

            // Try consolidated secret first (new approach) - only if we have a consolidated secret name in database
            if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
            {
                var consolidatedSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConsolidatedSecretName) ?? credential.ConsolidatedSecretName;
                _logger.LogInformation("Attempting to get SAP password from existing consolidated secret: {ConsolidatedSecretName}", consolidatedSecretName);
                
                // If the secret name already has the service prefix (from URI extraction), use it directly without going through KeyVault prefix generation
                string? sapPassword = null;
                if (consolidatedSecretName.StartsWith("cumulus-service-com-"))
                {
                    _logger.LogInformation("Consolidated secret name already has service prefix, calling KeyVault directly");
                    // Use the KeyVault client directly to bypass prefix generation
                    try
                    {
                        var secret = await _keyVaultService.GetSecretByExactNameAsync(consolidatedSecretName);
                        sapPassword = secret;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get secret directly by exact name: {SecretName}", consolidatedSecretName);
                    }
                }
                else
                {
                    // Let KeyVault service add the prefix (for tenant-scoped names)
                    sapPassword = await _keyVaultService.GetSecretAsync(consolidatedSecretName, organizationId.ToString());
                }
                
                if (sapPassword != null)
                {
                    _logger.LogInformation("Successfully retrieved SAP password from consolidated secret for credential {CredentialId}", credentialId);
                    return sapPassword;
                }
                _logger.LogWarning("Failed to get SAP password from consolidated secret, falling back to separate secret for credential {CredentialId}", credentialId);
            }
            else
            {
                _logger.LogInformation("No consolidated secret name in database, will try separate password secret for credential {CredentialId}", credentialId);
            }

            // Fallback to separate password secret (backward compatibility)
            _logger.LogInformation("Getting SAP password from separate secret for credential {CredentialId}", credentialId);
            
            if (!string.IsNullOrEmpty(credential.PasswordSecretName))
            {
                // Extract the actual secret name from URI if it's a URI
                var passwordSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.PasswordSecretName) ?? credential.PasswordSecretName;
                _logger.LogInformation("Using existing password secret name from database: {PasswordSecretName}", passwordSecretName);
                return await _keyVaultService.GetSecretAsync(passwordSecretName, organizationId.ToString());
            }
            else
            {
                // Generate password secret name as last resort (for very old credentials) - KeyVault service will add prefix
                var passwordSecretName = credential.GeneratePasswordSecretName();
                _logger.LogInformation("No password secret name in database, generated tenant-scoped name: {PasswordSecretName} for credential {CredentialId}", passwordSecretName, credentialId);
                return await _keyVaultService.GetSecretAsync(passwordSecretName, organizationId.ToString());
            }
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

            // 🔧 Handle both consolidated secrets (new format) and legacy URIs (old format)
            bool useConsolidatedSecret = !string.IsNullOrEmpty(credential.ConsolidatedSecretName);
            
            if (useConsolidatedSecret)
            {
                // NEW FORMAT: Use consolidated secret URI (same as legacy format)
                _logger.LogInformation("Updating consolidated Key Vault secret by URI {SecretUri}", credential.ConsolidatedSecretName);
                
                var (success, newVersionUri) = await _keyVaultService.UpdateSecretByUriAsync(credential.ConsolidatedSecretName, newPassword, organizationId.ToString());
                if (!success)
                {
                    _logger.LogError("Failed to update password in Key Vault consolidated secret {SecretUri} for credential {CredentialId}", 
                        credential.ConsolidatedSecretName, credentialId);
                    throw new InvalidOperationException("Failed to update password in Key Vault. The secret may be in an invalid state or Key Vault permissions may be insufficient.");
                }

                // Update to the new version URI in SQL database 
                if (!string.IsNullOrEmpty(newVersionUri))
                {
                    credential.ConsolidatedSecretName = newVersionUri;
                    _logger.LogInformation("Updated consolidated secret to new version URI: {NewVersionUri}", newVersionUri);
                }
                
                _logger.LogInformation("SAP password updated successfully in consolidated Key Vault secret (new version created)");
            }
            else
            {
                // OLD FORMAT: Use legacy password secret URI (backward compatibility)
                _logger.LogInformation("Updating legacy Key Vault secret by URI {SecretUri}", credential.PasswordSecretName);
                
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
            }

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

            // Try consolidated secret first (newest approach)
            if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
            {
                _logger.LogInformation("Attempting to get connection string from consolidated secret for credential {CredentialId}", credentialId);
                
                // Extract secret name from consolidated URI
                var consolidatedSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConsolidatedSecretName) ?? credential.ConsolidatedSecretName;
                
                // Smart prefix handling for consolidated secrets
                string? secretValue = null;
                Dictionary<string, string>? tags = null;
                
                if (consolidatedSecretName.StartsWith("cumulus-service-com-"))
                {
                    // For prefixed names, use direct access - tags not available yet, so assume connection string is in the secret value
                    secretValue = await _keyVaultService.GetSecretByExactNameAsync(consolidatedSecretName);
                    // TODO: Add proper tag retrieval for exact name access when method becomes available
                    // For now, assume consolidated secret contains connection string directly
                    if (!string.IsNullOrEmpty(secretValue))
                    {
                        _logger.LogInformation("Successfully retrieved connection string from consolidated secret (direct access) for credential {CredentialId}", credentialId);
                        return secretValue; // Consolidated secret value might contain the connection string
                    }
                }
                else
                {
                    (secretValue, tags) = await _keyVaultService.GetSecretWithTagsAsync(consolidatedSecretName, organizationId.ToString());
                    if (tags != null && tags.ContainsKey("connectionString"))
                    {
                        _logger.LogInformation("Successfully retrieved connection string from consolidated secret for credential {CredentialId}", credentialId);
                        return tags["connectionString"];
                    }
                }
                
                _logger.LogWarning("Failed to get connection string from consolidated secret, falling back to separate secret for credential {CredentialId}", credentialId);
            }

            // Fallback to separate connection string secret (previous approach)
            if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
            {
                _logger.LogInformation("Getting connection string from separate secret for credential {CredentialId}", credentialId);
                
                // Extract secret name from URI (backward compatibility)
                var connectionStringSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConnectionStringSecretName) ?? credential.ConnectionStringSecretName;
                
                // Smart prefix handling: use direct access if already prefixed, otherwise let KeyVault service add prefix
                string? connectionString = null;
                if (connectionStringSecretName.StartsWith("cumulus-service-com-"))
                {
                    connectionString = await _keyVaultService.GetSecretByExactNameAsync(connectionStringSecretName);
                }
                else
                {
                    connectionString = await _keyVaultService.GetSecretAsync(connectionStringSecretName, organizationId.ToString());
                }
                
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
            
            // Update SAP Configuration fields
            existingCredential.SAPServiceLayerHostname = model.SAPServiceLayerHostname ?? string.Empty;
            existingCredential.SAPAPIGatewayHostname = model.SAPAPIGatewayHostname ?? string.Empty;
            existingCredential.SAPBusinessOneWebClientHost = model.SAPBusinessOneWebClientHost ?? string.Empty;
            existingCredential.DocumentCode = model.DocumentCode ?? string.Empty;
            
            // Update SAPServiceLayerDBSchema based on database type
            existingCredential.SAPServiceLayerDBSchema = existingCredential.DatabaseType == DatabaseType.HANA 
                ? model.CurrentSchema 
                : model.DatabaseName;
            
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
            _logger.LogInformation("  ConsolidatedSecretName: {ConsolidatedSecretName}", credential.ConsolidatedSecretName);
            
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
            
            // Update consolidated secret metadata (newer approach - may not exist for older credentials)
            if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
            {
                attemptCount++;
                _logger.LogInformation("Attempting to sync consolidated secret metadata...");
                try
                {
                    // For consolidated secrets, we need to update both the value and connection string tag
                    string currentSAPPassword;
                    string currentConnectionString;
                    
                    try
                    {
                        currentSAPPassword = await GetSAPPasswordAsync(credential.Id, Guid.Parse(organizationId));
                        if (string.IsNullOrEmpty(currentSAPPassword))
                        {
                            _logger.LogError("GetSAPPasswordAsync returned null or empty for credential {CredentialId} during sync", credential.Id);
                            throw new InvalidOperationException("Unable to retrieve SAP password for consolidated secret sync.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to retrieve SAP password for consolidated secret sync for credential {CredentialId}", credential.Id);
                        _logger.LogWarning("❌ Skipping consolidated secret sync due to SAP password retrieval failure");
                        goto SkipConsolidatedSync; // Skip this consolidated secret sync but continue
                    }
                    
                    try
                    {
                        currentConnectionString = await BuildConnectionStringAsync(credential.Id, Guid.Parse(organizationId));
                        if (string.IsNullOrEmpty(currentConnectionString))
                        {
                            _logger.LogError("BuildConnectionStringAsync returned null or empty for credential {CredentialId} during sync", credential.Id);
                            throw new InvalidOperationException("Unable to build connection string for consolidated secret sync.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to build connection string for consolidated secret sync for credential {CredentialId}", credential.Id);
                        _logger.LogWarning("❌ Skipping consolidated secret sync due to connection string build failure");
                        goto SkipConsolidatedSync; // Skip this consolidated secret sync but continue
                    }
                    
                    var (success, newVersionUri) = await _keyVaultService.UpdateSecretMetadataByUriAsync(
                        credential.ConsolidatedSecretName,
                        currentSAPPassword,
                        organizationId,
                        credential.IsActive, // enabled status matches credential active status
                        new Dictionary<string, string> { { "connectionString", currentConnectionString } });
                        
                    if (success)
                    {
                        if (!string.IsNullOrEmpty(newVersionUri))
                        {
                            credential.ConsolidatedSecretName = newVersionUri;
                            _logger.LogInformation("Updated consolidated secret to new version URI: {NewVersionUri}", newVersionUri);
                        }
                        
                        successCount++;
                        _logger.LogInformation("✅ Consolidated secret metadata sync completed successfully");
                    }
                    else
                    {
                        _logger.LogWarning("❌ Consolidated secret metadata sync failed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ Exception during consolidated secret metadata sync");
                }
            }
            
            SkipConsolidatedSync:
            // Continue with regular sync summary even if consolidated secret sync failed
            
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
        var activeCacheKey = $"org_active_credentials_{organizationId}";
        _cache.Remove(cacheKey);
        _cache.Remove(activeCacheKey);
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

    /// <summary>
    /// Creates a consolidated Key Vault secret with SAP password as value and connection string + SAP configuration as tags
    /// </summary>
    private async Task<bool> CreateConsolidatedSecretAsync(string secretName, string sapPassword, string connectionString, string organizationId, DatabaseCredential? credential = null)
    {
        try
        {
            _logger.LogInformation("Creating consolidated secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            
            // Validate connection string length for Key Vault tag (max 256 chars)
            if (connectionString.Length > 256)
            {
                _logger.LogWarning("Connection string too long ({Length} chars) for Key Vault tag, falling back to separate secrets", connectionString.Length);
                return false;
            }
            
            // 🔧 FIX: Create consolidated secret with ALL tags in ONE operation (no double creation)
            var allTags = new Dictionary<string, string>
            {
                { "connectionString", connectionString },
                { "secretType", "consolidated" }
            };

            // Add database-level SAP configuration as tags (only if provided and not empty)
            if (credential != null)
            {
                if (!string.IsNullOrWhiteSpace(credential.SAPServiceLayerHostname))
                {
                    allTags["sapServiceLayer"] = credential.SAPServiceLayerHostname;
                    _logger.LogDebug("Adding SAP Service Layer hostname tag: {Hostname}", credential.SAPServiceLayerHostname);
                }

                if (!string.IsNullOrWhiteSpace(credential.SAPAPIGatewayHostname))
                {
                    allTags["sapAPIGateway"] = credential.SAPAPIGatewayHostname;
                    _logger.LogDebug("Adding SAP API Gateway hostname tag: {Hostname}", credential.SAPAPIGatewayHostname);
                }

                if (!string.IsNullOrWhiteSpace(credential.SAPBusinessOneWebClientHost))
                {
                    allTags["sapWebClient"] = credential.SAPBusinessOneWebClientHost;
                    _logger.LogDebug("Adding SAP Web Client hostname tag: {Hostname}", credential.SAPBusinessOneWebClientHost);
                }

                if (!string.IsNullOrWhiteSpace(credential.DocumentCode))
                {
                    allTags["documentCode"] = credential.DocumentCode;
                    _logger.LogDebug("Adding Document Code tag: {DocumentCode}", credential.DocumentCode);
                }
            }
            
            _logger.LogInformation("🎉 Creating consolidated secret with {TagCount} total tags in SINGLE operation (fixing double-creation bug)", allTags.Count + 5); // +5 for default tags (org, type, created, secretName, isActive)
            var success = await _keyVaultService.SetSecretWithTagsAsync(secretName, sapPassword, organizationId, allTags);
            if (!success)
            {
                _logger.LogError("Failed to create consolidated secret {SecretName} with all tags", secretName);
                return false;
            }
            
            _logger.LogInformation("Successfully created consolidated secret {SecretName} with connection string tag", secretName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating consolidated secret {SecretName}", secretName);
            return false;
        }
    }

    public async Task<string?> GetEffectiveSAPServiceLayerHostnameAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Check database-level setting first (from database columns)
            if (!string.IsNullOrWhiteSpace(credential.SAPServiceLayerHostname))
            {
                _logger.LogDebug("Using database-level SAP Service Layer hostname for credential {CredentialId}: {Hostname}", 
                    credentialId, credential.SAPServiceLayerHostname);
                return credential.SAPServiceLayerHostname;
            }

            // Check consolidated secret tags (if available)
            if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
            {
                try
                {
                    var consolidatedSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConsolidatedSecretName) ?? credential.ConsolidatedSecretName;
                    var (_, tags) = await _keyVaultService.GetSecretWithTagsAsync(consolidatedSecretName, organizationId.ToString());
                    
                    if (tags != null && tags.ContainsKey("sapServiceLayer") && !string.IsNullOrWhiteSpace(tags["sapServiceLayer"]))
                    {
                        _logger.LogDebug("Using consolidated secret tag SAP Service Layer hostname for credential {CredentialId}: {Hostname}", 
                            credentialId, tags["sapServiceLayer"]);
                        return tags["sapServiceLayer"];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading SAP Service Layer hostname from consolidated secret for credential {CredentialId}", credentialId);
                }
            }

            // Fall back to organization-level setting
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization != null && !string.IsNullOrWhiteSpace(organization.SAPServiceLayerHostname))
            {
                _logger.LogDebug("Using organization-level SAP Service Layer hostname for credential {CredentialId}: {Hostname}", 
                    credentialId, organization.SAPServiceLayerHostname);
                return organization.SAPServiceLayerHostname;
            }

            _logger.LogDebug("No SAP Service Layer hostname configured at database, secret, or organization level for credential {CredentialId}", credentialId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting effective SAP Service Layer hostname for credential {CredentialId}", credentialId);
            return null;
        }
    }

    public async Task<string?> GetEffectiveSAPAPIGatewayHostnameAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Check database-level setting first
            if (!string.IsNullOrWhiteSpace(credential.SAPAPIGatewayHostname))
            {
                _logger.LogDebug("Using database-level SAP API Gateway hostname for credential {CredentialId}: {Hostname}", 
                    credentialId, credential.SAPAPIGatewayHostname);
                return credential.SAPAPIGatewayHostname;
            }

            // Fall back to organization-level setting
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization != null && !string.IsNullOrWhiteSpace(organization.SAPAPIGatewayHostname))
            {
                _logger.LogDebug("Using organization-level SAP API Gateway hostname for credential {CredentialId}: {Hostname}", 
                    credentialId, organization.SAPAPIGatewayHostname);
                return organization.SAPAPIGatewayHostname;
            }

            _logger.LogDebug("No SAP API Gateway hostname configured at database or organization level for credential {CredentialId}", credentialId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting effective SAP API Gateway hostname for credential {CredentialId}", credentialId);
            return null;
        }
    }

    public async Task<string?> GetEffectiveSAPBusinessOneWebClientHostAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Check database-level setting first
            if (!string.IsNullOrWhiteSpace(credential.SAPBusinessOneWebClientHost))
            {
                _logger.LogDebug("Using database-level SAP Business One Web Client host for credential {CredentialId}: {Host}", 
                    credentialId, credential.SAPBusinessOneWebClientHost);
                return credential.SAPBusinessOneWebClientHost;
            }

            // Fall back to organization-level setting
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization != null && !string.IsNullOrWhiteSpace(organization.SAPBusinessOneWebClientHost))
            {
                _logger.LogDebug("Using organization-level SAP Business One Web Client host for credential {CredentialId}: {Host}", 
                    credentialId, organization.SAPBusinessOneWebClientHost);
                return organization.SAPBusinessOneWebClientHost;
            }

            _logger.LogDebug("No SAP Business One Web Client host configured at database or organization level for credential {CredentialId}", credentialId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting effective SAP Business One Web Client host for credential {CredentialId}", credentialId);
            return null;
        }
    }

    public async Task<string?> GetEffectiveDocumentCodeAsync(Guid credentialId, Guid organizationId)
    {
        try
        {
            var credential = await GetByIdAsync(credentialId, organizationId);
            if (credential == null)
            {
                return null;
            }

            // Check database-level setting first
            if (!string.IsNullOrWhiteSpace(credential.DocumentCode))
            {
                _logger.LogDebug("Using database-level Document Code for credential {CredentialId}: {DocumentCode}", 
                    credentialId, credential.DocumentCode);
                return credential.DocumentCode;
            }

            // Fall back to organization-level setting
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization != null && !string.IsNullOrWhiteSpace(organization.DocumentCode))
            {
                _logger.LogDebug("Using organization-level Document Code for credential {CredentialId}: {DocumentCode}", 
                    credentialId, organization.DocumentCode);
                return organization.DocumentCode;
            }

            _logger.LogDebug("No Document Code configured at database or organization level for credential {CredentialId}", credentialId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting effective Document Code for credential {CredentialId}", credentialId);
            return null;
        }
    }

    private string? ExtractSecretNameFromConsolidatedSecretName(string? consolidatedSecretName)
    {
        if (string.IsNullOrWhiteSpace(consolidatedSecretName))
            return null;

        // If it's just a name (not a URI), return it as-is
        if (!consolidatedSecretName.Contains("://"))
        {
            return consolidatedSecretName;
        }

        // If it's a full URI, extract the secret name
        try
        {
            var uri = new Uri(consolidatedSecretName);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            
            // Should have at least "secrets" and "secret-name"
            if (pathSegments.Length >= 2 && pathSegments[0] == "secrets")
            {
                return pathSegments[1]; // This is the secret name without version
            }
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid URI format when extracting consolidated secret name: {ConsolidatedSecretName}", consolidatedSecretName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error extracting consolidated secret name: {ConsolidatedSecretName}", consolidatedSecretName);
        }

        return null;
    }
}