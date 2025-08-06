using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using AdminConsole.Models;

namespace AdminConsole.Services;

public class KeyVaultService : IKeyVaultService
{
    private readonly SecretClient? _secretClient;
    private readonly IOrganizationService _organizationService;
    private readonly IDataIsolationService _dataIsolationService;
    private readonly ITenantIsolationValidator _tenantValidator;
    private readonly ILogger<KeyVaultService> _logger;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(15);

    public KeyVaultService(
        SecretClient? secretClient,
        IOrganizationService organizationService,
        IDataIsolationService dataIsolationService,
        ITenantIsolationValidator tenantValidator,
        ILogger<KeyVaultService> logger, 
        IMemoryCache cache)
    {
        _secretClient = secretClient;
        _organizationService = organizationService;
        _dataIsolationService = dataIsolationService;
        _tenantValidator = tenantValidator;
        _logger = logger;
        _cache = cache;
    }

    public async Task<string?> GetSecretAsync(string secretName, string organizationId)
    {
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            return null;
        }

        try
        {
            // Enhanced tenant isolation validation with security compliance
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "read");

            // Validate organization exists
            var organization = await _organizationService.GetByIdAsync(organizationId);
            if (organization == null)
            {
                _logger.LogWarning("Organization {OrganizationId} not found when retrieving secret {SecretName}", 
                    organizationId, secretName);
                return null;
            }
            var cacheKey = $"secret_{organizationId}_{secretName}";
            
            if (_cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                return cachedValue;
            }

            // Generate tenant-specific secret name
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            
            var secret = await _secretClient.GetSecretAsync(tenantSecretName);
            
            if (secret?.Value != null)
            {
                // Verify organization tag matches
                if (ValidateOrganizationTag(secret.Value.Properties.Tags, organizationId))
                {
                    _cache.Set(cacheKey, secret.Value.Value, _cacheExpiry);
                    return secret.Value.Value;
                }
                else
                {
                    _logger.LogWarning("Organization tag mismatch for secret {SecretName}, expected {OrganizationId}", 
                        tenantSecretName, organizationId);
                }
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {SecretName} not found for organization {OrganizationId}", secretName, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
        }

        return null;
    }

    public async Task<string?> GetSecretIdentifierAsync(string secretName, string organizationId)
    {
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            return null;
        }

        try
        {
            // Enhanced tenant isolation validation with security compliance
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "read");

            // Validate organization exists
            var organization = await _organizationService.GetByIdAsync(organizationId);
            if (organization == null)
            {
                _logger.LogWarning("Organization {OrganizationId} not found when retrieving secret identifier {SecretName}", 
                    organizationId, secretName);
                return null;
            }

            // Generate tenant-specific secret name
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            
            var secret = await _secretClient.GetSecretAsync(tenantSecretName);
            
            if (secret?.Value != null)
            {
                // Verify organization tag matches
                if (ValidateOrganizationTag(secret.Value.Properties.Tags, organizationId))
                {
                    // Return the full Key Vault secret identifier URI
                    return secret.Value.Properties.Id?.ToString();
                }
                else
                {
                    _logger.LogWarning("Organization tag mismatch for secret {SecretName}, expected {OrganizationId}", 
                        tenantSecretName, organizationId);
                }
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {SecretName} not found for organization {OrganizationId}", secretName, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret identifier {SecretName} for organization {OrganizationId}", secretName, organizationId);
        }

        return null;
    }

    public async Task<(bool Success, string? NewVersionUri)> UpdateSecretByUriAsync(string secretUri, string secretValue, string organizationId)
    {
        _logger.LogInformation("=== Key Vault UpdateSecretByUriAsync Debug ===");
        _logger.LogInformation("  SecretUri: {SecretUri}", secretUri);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot update secret by URI {SecretUri} for organization {OrganizationId}", secretUri, organizationId);
            return (false, null);
        }

        try
        {
            // Extract secret name from URI
            var secretName = ExtractSecretNameFromUri(secretUri);
            if (secretName == null)
            {
                _logger.LogError("Failed to extract secret name from URI: {SecretUri}", secretUri);
                return (false, null);
            }

            _logger.LogInformation("  Extracted secret name: {SecretName}", secretName);

            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "write");
            
            // Extract tenant secret name from the existing URI (to maintain the same secret)
            var uri = new Uri(secretUri);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            if (pathSegments.Length < 2 || pathSegments[0] != "secrets")
            {
                _logger.LogError("Invalid secret URI format: {SecretUri}", secretUri);
                return (false, null);
            }
            
            var tenantSecretName = pathSegments[1]; // Use the existing tenant secret name from URI
            _logger.LogInformation("  Using existing tenant secret name from URI: {TenantSecretName}", tenantSecretName);
            
            // Create new version of existing secret (same secret name, new version)
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // Add organization and metadata tags
            secretOptions.Properties.Tags.Add("org", organizationId);
            secretOptions.Properties.Tags.Add("type", "tenant-secret");
            secretOptions.Properties.Tags.Add("updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            secretOptions.Properties.Tags.Add("secretName", secretName); // Original secret name for queries
            
            // Note: Enabled status and other metadata will be set by caller if needed
            // This allows the method to be used for both password updates and metadata sync

            _logger.LogInformation("  Calling Azure Key Vault SetSecretAsync to create new version...");
            var newVersionResponse = await _secretClient.SetSecretAsync(secretOptions);
            _logger.LogInformation("  Successfully created new version of secret {TenantSecretName}", tenantSecretName);
            
            // Get the new version URI from the response
            var newVersionUri = newVersionResponse?.Value?.Properties?.Id?.ToString();
            _logger.LogInformation("  New version URI: {NewVersionUri}", newVersionUri);
            
            // Invalidate cache
            var cacheKey = $"secret_{organizationId}_{secretName}";
            _cache.Remove(cacheKey);
            
            _logger.LogInformation("Successfully updated secret by URI {SecretUri} for organization {OrganizationId}, new version URI: {NewVersionUri}", secretUri, organizationId, newVersionUri);
            return (true, newVersionUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update secret by URI {SecretUri} for organization {OrganizationId}", secretUri, organizationId);
            return (false, null);
        }
    }

    public async Task<bool> SetSecretAsync(string secretName, string secretValue, string organizationId)
    {
        _logger.LogInformation("=== Key Vault SetSecretAsync Debug ===");
        _logger.LogInformation("  SecretName: {SecretName}", secretName);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        _logger.LogInformation("  SecretClient null?: {IsNull}", _secretClient == null);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot store secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }

        try
        {
            // Enhanced tenant isolation validation
            _logger.LogInformation("  Step 1: Validating tenant access...");
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "write");
            _logger.LogInformation("  Step 1: Tenant validation passed");
            
            _logger.LogInformation("  Step 2: Generating tenant secret name...");
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            _logger.LogInformation("  Step 2: Generated tenant secret name: {TenantSecretName}", tenantSecretName);
            
            _logger.LogInformation("  Step 3: Creating Key Vault secret object...");
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // Add organization and metadata tags
            secretOptions.Properties.Tags.Add("org", organizationId);
            secretOptions.Properties.Tags.Add("type", "tenant-secret");
            secretOptions.Properties.Tags.Add("created", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            secretOptions.Properties.Tags.Add("secretName", secretName); // Original secret name for queries
            secretOptions.Properties.Tags.Add("isActive", "true"); // New secrets are active by default
            _logger.LogInformation("  Added isActive tag: true (new secrets are active by default)");

            _logger.LogInformation("  Step 4: Calling Azure Key Vault SetSecretAsync...");
            try
            {
                await _secretClient.SetSecretAsync(secretOptions);
                _logger.LogInformation("  Step 4: Azure Key Vault call succeeded");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "Conflict")
            {
                _logger.LogWarning("Secret {TenantSecretName} is in deleted-but-recoverable state. Attempting to recover and update...", tenantSecretName);
                try
                {
                    // Try to recover the deleted secret first
                    var recoveryOperation = await _secretClient.StartRecoverDeletedSecretAsync(tenantSecretName);
                    _logger.LogInformation("Secret recovery initiated. Waiting for recovery to complete...");
                    
                    // Wait for recovery to complete (with timeout)
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await recoveryOperation.WaitForCompletionAsync(cts.Token);
                    _logger.LogInformation("Secret recovery completed");
                    
                    // Now try to set the secret again
                    await _secretClient.SetSecretAsync(secretOptions);
                    _logger.LogInformation("Successfully recovered and updated secret {TenantSecretName}", tenantSecretName);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("Secret recovery timed out after 30 seconds for {TenantSecretName}", tenantSecretName);
                    throw new InvalidOperationException($"Secret recovery timed out. Please try again later.");
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogError(recoveryEx, "Failed to recover and update secret {TenantSecretName}", tenantSecretName);
                    throw;
                }
            }
            
            // Invalidate cache
            var cacheKey = $"secret_{organizationId}_{secretName}";
            _cache.Remove(cacheKey);
            
            _logger.LogInformation("Successfully set secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetSecretNamesAsync(string organizationId)
    {
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId, "list-secrets");
            
            var cacheKey = $"secretNames_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<string>? cachedNames))
            {
                return cachedNames ?? Enumerable.Empty<string>();
            }

            var secretNames = new List<string>();
            
            await foreach (var secretProperties in _secretClient.GetPropertiesOfSecretsAsync())
            {
                // Check if this secret belongs to the organization using tags
                if (secretProperties.Tags.TryGetValue("org", out var orgTag) && 
                    orgTag == organizationId &&
                    secretProperties.Tags.TryGetValue("secretName", out var originalName))
                {
                    secretNames.Add(originalName);
                }
            }
            
            _cache.Set(cacheKey, secretNames, _cacheExpiry);
            return secretNames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret names for organization {OrganizationId}", organizationId);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<bool> DeleteSecretAsync(string secretName, string organizationId)
    {
        _logger.LogInformation("=== Key Vault DeleteSecretAsync Debug ===");
        _logger.LogInformation("  SecretName: {SecretName}", secretName);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot delete secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }

        try
        {
            // Enhanced tenant isolation validation with delete operation check
            _logger.LogInformation("  Step 1: Validating secret access...");
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "delete");
            
            _logger.LogInformation("  Step 2: Generating tenant secret name...");
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            _logger.LogInformation("  Generated tenant secret name: {TenantSecretName}", tenantSecretName);
            
            // Verify ownership before deletion
            _logger.LogInformation("  Step 3: Verifying secret ownership...");
            var secret = await _secretClient.GetSecretAsync(tenantSecretName);
            if (!ValidateOrganizationTag(secret.Value.Properties.Tags, organizationId))
            {
                _logger.LogWarning("Attempted to delete secret {SecretName} without proper organization access", tenantSecretName);
                return false;
            }
            _logger.LogInformation("  Secret ownership verified");
            
            _logger.LogInformation("  Step 4: Starting secret deletion operation...");
            var deleteOperation = await _secretClient.StartDeleteSecretAsync(tenantSecretName);
            _logger.LogInformation("  Step 5: Waiting for deletion completion...");
            await deleteOperation.WaitForCompletionAsync();
            _logger.LogInformation("  Deletion operation completed");
            
            // Invalidate cache
            var cacheKey = $"secret_{organizationId}_{secretName}";
            _cache.Remove(cacheKey);
            var namesCacheKey = $"secretNames_{organizationId}";
            _cache.Remove(namesCacheKey);
            
            _logger.LogInformation("Successfully deleted secret {SecretName} (tenant: {TenantSecretName}) for organization {OrganizationId}", secretName, tenantSecretName, organizationId);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {SecretName} not found for deletion in organization {OrganizationId}", secretName, organizationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }
    }

    private async Task<string> GenerateTenantSecretName(string secretName, string organizationId)
    {
        _logger.LogInformation("    GenerateTenantSecretName: OrganizationId={OrganizationId}, SecretName={SecretName}", organizationId, secretName);
        
        try
        {
            // Get organization to use its effective secret prefix
            _logger.LogInformation("    Looking up organization by ID: {OrganizationId}", organizationId);
            var organization = await _organizationService.GetByIdAsync(organizationId);
            _logger.LogInformation("    Organization lookup result: {OrganizationFound}", organization != null);
            
            if (organization != null)
            {
                _logger.LogInformation("    Organization found - Name: {Name}, Domain: {Domain}", organization.Name, organization.Domain);
                var effectivePrefix = organization.GetEffectiveSecretPrefix();
                _logger.LogInformation("    Effective secret prefix: {Prefix}", effectivePrefix);
            }
            
            var prefix = organization?.GetEffectiveSecretPrefix() ?? organizationId.Replace("_", "-").Replace(".", "-");
            _logger.LogInformation("    Using prefix: {Prefix}", prefix);
            
            // Ensure Key Vault naming compliance (alphanumeric and hyphens only)
            var sanitizedPrefix = prefix.Replace("_", "-").Replace(".", "-");
            var sanitizedSecretName = secretName.Replace("_", "-").Replace(".", "-");
            
            var finalName = $"{sanitizedPrefix}-{sanitizedSecretName}".ToLowerInvariant();
            _logger.LogInformation("    Final tenant secret name: {FinalName}", finalName);
            return finalName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get organization prefix for {OrganizationId}, using fallback", organizationId);
            // Fallback to original logic
            var sanitizedOrgId = organizationId.Replace("_", "-").Replace(".", "-");
            var sanitizedSecretName = secretName.Replace("_", "-").Replace(".", "-");
            var fallbackName = $"org-{sanitizedOrgId}-{sanitizedSecretName}".ToLowerInvariant();
            _logger.LogInformation("    Fallback secret name: {FallbackName}", fallbackName);
            return fallbackName;
        }
    }

    private bool IsKeyVaultAvailable()
    {
        if (_secretClient == null)
        {
            _logger.LogWarning("Key Vault service is not available - SecretClient is null");
            return false;
        }
        return true;
    }

    private bool ValidateOrganizationTag(IDictionary<string, string> tags, string expectedOrganizationId)
    {
        return tags.TryGetValue("org", out var orgTag) && orgTag == expectedOrganizationId;
    }

    /// <summary>
    /// Extracts the secret name from a Key Vault secret identifier URI
    /// </summary>
    /// <param name="secretIdentifier">The full Key Vault secret identifier URI</param>
    /// <returns>The secret name, or null if invalid</returns>
    public static string? ExtractSecretNameFromUri(string? secretIdentifier)
    {
        if (string.IsNullOrWhiteSpace(secretIdentifier))
            return null;

        try
        {
            // Key Vault secret identifier format: https://vault-name.vault.azure.net/secrets/secret-name/version
            var uri = new Uri(secretIdentifier);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            
            // Should have at least "secrets" and "secret-name"
            if (pathSegments.Length >= 2 && pathSegments[0] == "secrets")
            {
                return pathSegments[1];
            }
        }
        catch (UriFormatException)
        {
            // If it's not a valid URI, assume it's already a secret name (backward compatibility)
            return secretIdentifier;
        }
        
        return null;
    }

    public async Task<(bool Success, string? NewVersionUri)> UpdateSecretMetadataByUriAsync(string secretUri, string secretValue, string organizationId, bool enabled, Dictionary<string, string>? additionalTags = null)
    {
        _logger.LogInformation("=== Key Vault UpdateSecretMetadataByUriAsync Debug ===");
        _logger.LogInformation("  SecretUri: {SecretUri}", secretUri);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        _logger.LogInformation("  Enabled: {Enabled}", enabled);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot update secret metadata by URI {SecretUri} for organization {OrganizationId}", secretUri, organizationId);
            return (false, null);
        }

        try
        {
            // Extract secret name from URI
            var secretName = ExtractSecretNameFromUri(secretUri);
            if (secretName == null)
            {
                _logger.LogError("Failed to extract secret name from URI: {SecretUri}", secretUri);
                return (false, null);
            }

            _logger.LogInformation("  Extracted secret name: {SecretName}", secretName);

            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "write");
            
            // Extract tenant secret name from the existing URI (to maintain the same secret)
            var uri = new Uri(secretUri);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            if (pathSegments.Length < 2 || pathSegments[0] != "secrets")
            {
                _logger.LogError("Invalid secret URI format: {SecretUri}", secretUri);
                return (false, null);
            }
            
            var tenantSecretName = pathSegments[1]; // Use the existing tenant secret name from URI
            _logger.LogInformation("  Using existing tenant secret name from URI: {TenantSecretName}", tenantSecretName);
            
            // Create new version of existing secret with updated metadata
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // Add standard organization and metadata tags
            secretOptions.Properties.Tags.Add("org", organizationId);
            secretOptions.Properties.Tags.Add("type", "tenant-secret");
            secretOptions.Properties.Tags.Add("updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            secretOptions.Properties.Tags.Add("secretName", secretName); // Original secret name for queries
            
            // Instead of disabling secrets in Key Vault (which prevents reading them),
            // we keep all secrets enabled but use tags to track the status
            secretOptions.Properties.Enabled = true; // Always keep enabled in Key Vault
            secretOptions.Properties.Tags.Add("isActive", enabled.ToString().ToLower());
            _logger.LogInformation("  Keeping Key Vault secret enabled but setting isActive tag to: {Enabled}", enabled);
            _logger.LogInformation("  DEBUG: Added isActive tag with value: '{IsActiveValue}'", enabled.ToString().ToLower());
            
            // Add any additional tags provided by caller
            if (additionalTags != null)
            {
                foreach (var tag in additionalTags)
                {
                    secretOptions.Properties.Tags.Add(tag.Key, tag.Value);
                    _logger.LogInformation("  Added additional tag: {Key} = {Value}", tag.Key, tag.Value);
                }
            }

            // Log all tags being set for debugging
            _logger.LogInformation("  DEBUG: All tags being set: {Tags}", string.Join(", ", secretOptions.Properties.Tags.Select(t => $"{t.Key}={t.Value}")));
            _logger.LogInformation("  Calling Azure Key Vault SetSecretAsync to create new version with metadata...");
            var newVersionResponse = await _secretClient.SetSecretAsync(secretOptions);
            _logger.LogInformation("  Successfully created new version of secret {TenantSecretName} with metadata", tenantSecretName);
            
            // Get the new version URI from the response
            var newVersionUri = newVersionResponse?.Value?.Properties?.Id?.ToString();
            _logger.LogInformation("  New version URI: {NewVersionUri}", newVersionUri);
            
            // Invalidate cache
            var cacheKey = $"secret_{organizationId}_{secretName}";
            _cache.Remove(cacheKey);
            
            _logger.LogInformation("Successfully updated secret metadata by URI {SecretUri} for organization {OrganizationId}, new version URI: {NewVersionUri}", secretUri, organizationId, newVersionUri);
            return (true, newVersionUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update secret metadata by URI {SecretUri} for organization {OrganizationId}", secretUri, organizationId);
            return (false, null);
        }
    }

    /// <summary>
    /// Deletes a secret directly from Key Vault using the exact secret name (bypasses tenant name generation)
    /// </summary>
    public async Task<bool> DeleteSecretByExactNameAsync(string exactSecretName)
    {
        _logger.LogInformation("=== DeleteSecretByExactNameAsync ===");
        _logger.LogInformation("  ExactSecretName: {ExactSecretName}", exactSecretName);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot delete secret by exact name {ExactSecretName}", exactSecretName);
            return false;
        }

        try
        {
            _logger.LogInformation("  Step 1: Starting secret deletion operation...");
            var deleteOperation = await _secretClient.StartDeleteSecretAsync(exactSecretName);
            _logger.LogInformation("  Step 2: Waiting for deletion completion...");
            await deleteOperation.WaitForCompletionAsync();
            _logger.LogInformation("  Deletion operation completed");
            
            _logger.LogInformation("✅ Successfully deleted secret by exact name: {ExactSecretName}", exactSecretName);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {ExactSecretName} not found for deletion (404)", exactSecretName);
            return false; // Consider this a success since the secret doesn't exist
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret by exact name {ExactSecretName}", exactSecretName);
            return false;
        }
    }

    /// <summary>
    /// Gets secret properties (including enabled status) directly from Key Vault
    /// </summary>
    public async Task<(string? Value, bool? IsEnabled)> GetSecretWithPropertiesAsync(string exactSecretName)
    {
        _logger.LogInformation("=== GetSecretWithPropertiesAsync ===");
        _logger.LogInformation("  ExactSecretName: {ExactSecretName}", exactSecretName);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogWarning("Key Vault is not available");
            return (null, null);
        }

        try
        {
            var secret = await _secretClient.GetSecretAsync(exactSecretName);
            
            if (secret?.Value != null)
            {
                // Check the isActive tag instead of the Enabled property
                bool isActive = true; // Default to active if tag is missing
                if (secret.Value.Properties.Tags.TryGetValue("isActive", out var isActiveTag))
                {
                    bool.TryParse(isActiveTag, out isActive);
                    _logger.LogInformation("  Found isActive tag: {IsActiveTag} -> parsed as: {IsActive}", isActiveTag, isActive);
                }
                else
                {
                    _logger.LogInformation("  No isActive tag found - defaulting to: {IsActive}", isActive);
                }
                
                // Log all existing tags for debugging
                _logger.LogInformation("  All existing tags: {Tags}", string.Join(", ", secret.Value.Properties.Tags.Select(t => $"{t.Key}={t.Value}")));
                
                _logger.LogInformation("✅ Successfully retrieved secret with properties: {ExactSecretName}, KeyVault Enabled: {Enabled}, IsActive Tag: {IsActive}", 
                    exactSecretName, secret.Value.Properties.Enabled, isActive);
                return (secret.Value.Value, isActive);
            }
            else
            {
                _logger.LogWarning("Secret not found: {ExactSecretName}", exactSecretName);
                return (null, null);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {ExactSecretName} not found (404)", exactSecretName);
            return (null, null);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403 && ex.ErrorCode == "Forbidden")
        {
            _logger.LogWarning("Secret {ExactSecretName} access forbidden (403) - this should not happen with the new tag-based approach", exactSecretName);
            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret with properties {ExactSecretName}", exactSecretName);
            return (null, null);
        }
    }

    /// <summary>
    /// Gets a secret directly from Key Vault using the exact secret name (bypasses tenant name generation)
    /// </summary>
    public async Task<string?> GetSecretByExactNameAsync(string exactSecretName)
    {
        _logger.LogInformation("=== GetSecretByExactNameAsync ===");
        _logger.LogInformation("  ExactSecretName: {ExactSecretName}", exactSecretName);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogWarning("Key Vault is not available");
            return null;
        }

        try
        {
            var secret = await _secretClient.GetSecretAsync(exactSecretName);
            
            if (secret?.Value != null)
            {
                _logger.LogInformation("✅ Successfully retrieved secret by exact name: {ExactSecretName}", exactSecretName);
                return secret.Value.Value;
            }
            else
            {
                _logger.LogWarning("Secret not found: {ExactSecretName}", exactSecretName);
                return null;
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Secret {ExactSecretName} not found (404)", exactSecretName);
            return null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403 && ex.ErrorCode == "Forbidden")
        {
            _logger.LogInformation("Secret {ExactSecretName} is disabled (403 Forbidden) - cannot read disabled secrets", exactSecretName);
            return "DISABLED_SECRET"; // Special marker for disabled secrets
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret by exact name {ExactSecretName}", exactSecretName);
            return null;
        }
    }

    /// <summary>
    /// Enables a disabled secret by updating its enabled status without changing the secret value
    /// </summary>
    public async Task<(bool Success, string? NewVersionUri)> EnableDisabledSecretByUriAsync(string secretUri, string organizationId, Dictionary<string, string>? additionalTags = null)
    {
        _logger.LogInformation("=== EnableDisabledSecretByUriAsync ===");
        _logger.LogInformation("  SecretUri: {SecretUri}", secretUri);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available");
            return (false, null);
        }

        try
        {
            // Extract tenant secret name from the URI
            var uri = new Uri(secretUri);
            var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
            if (pathSegments.Length < 2 || pathSegments[0] != "secrets")
            {
                _logger.LogError("Invalid secret URI format: {SecretUri}", secretUri);
                return (false, null);
            }
            
            var tenantSecretName = pathSegments[1];
            _logger.LogInformation("  Using existing tenant secret name from URI: {TenantSecretName}", tenantSecretName);
            
            // Get the current secret properties (not the value, just properties)
            var secretProperties = await _secretClient.GetSecretAsync(tenantSecretName);
            if (secretProperties?.Value == null)
            {
                _logger.LogError("Could not retrieve secret properties for {TenantSecretName}", tenantSecretName);
                return (false, null);
            }
            
            _logger.LogInformation("  Retrieved secret properties, current enabled status: {CurrentEnabled}", secretProperties.Value.Properties.Enabled);
            
            // Create new version with same value but enabled=true
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretProperties.Value.Value);
            
            // Copy existing tags
            foreach (var tag in secretProperties.Value.Properties.Tags)
            {
                secretOptions.Properties.Tags.Add(tag.Key, tag.Value);
            }
            
            // Add/update standard tags
            secretOptions.Properties.Tags["org"] = organizationId;
            secretOptions.Properties.Tags["type"] = "tenant-secret";
            secretOptions.Properties.Tags["reactivated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            
            // Add any additional tags provided by caller
            if (additionalTags != null)
            {
                foreach (var tag in additionalTags)
                {
                    secretOptions.Properties.Tags[tag.Key] = tag.Value;
                    _logger.LogInformation("  Added additional tag: {Key} = {Value}", tag.Key, tag.Value);
                }
            }

            // Set enabled status to true - this is the key change
            secretOptions.Properties.Enabled = true;
            _logger.LogInformation("  Setting Key Vault secret enabled status to: true (keeping original value)");

            _logger.LogInformation("  Creating new enabled version with original secret value...");
            var newVersionResponse = await _secretClient.SetSecretAsync(secretOptions);
            _logger.LogInformation("  Successfully enabled secret {TenantSecretName} with original value", tenantSecretName);
            
            // Get the new version URI from the response
            var newVersionUri = newVersionResponse?.Value?.Properties?.Id?.ToString();
            _logger.LogInformation("  New version URI: {NewVersionUri}", newVersionUri);
            
            _logger.LogInformation("✅ Successfully enabled secret {TenantSecretName}, original value preserved", tenantSecretName);
            return (true, newVersionUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable disabled secret by URI {SecretUri} for organization {OrganizationId}", secretUri, organizationId);
            return (false, null);
        }
    }

    /// <summary>
    /// Tests Key Vault connectivity and permissions
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectivityAsync()
    {
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            return (false, "Key Vault SecretClient is not available. Check configuration and Azure credentials.");
        }

        try
        {
            // Try to list secrets to test connectivity and permissions
            var secretProps = _secretClient.GetPropertiesOfSecretsAsync();
            var enumerator = secretProps.GetAsyncEnumerator();
            
            // Try to get at least one secret to test connectivity
            if (await enumerator.MoveNextAsync())
            {
                // If we can enumerate at least one secret, connectivity is working
                await enumerator.DisposeAsync();
                return (true, "Key Vault connectivity and permissions verified successfully.");
            }
            
            await enumerator.DisposeAsync();
            return (true, "Key Vault connectivity verified (no secrets found, but access is working).");
        }
        catch (Azure.RequestFailedException ex)
        {
            return (false, $"Key Vault access failed: {ex.Status} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Key Vault connectivity test failed: {ex.Message}");
        }
    }
}