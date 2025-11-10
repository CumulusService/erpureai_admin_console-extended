using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Azure.Core;
using Azure;
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
    private readonly ICacheConfigurationService _cacheConfig;

    public KeyVaultService(
        SecretClient? secretClient,
        IOrganizationService organizationService,
        IDataIsolationService dataIsolationService,
        ITenantIsolationValidator tenantValidator,
        ILogger<KeyVaultService> logger, 
        IMemoryCache cache,
        ICacheConfigurationService cacheConfig)
    {
        _secretClient = secretClient;
        _organizationService = organizationService;
        _dataIsolationService = dataIsolationService;
        _tenantValidator = tenantValidator;
        _logger = logger;
        _cache = cache;
        _cacheConfig = cacheConfig;
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
                    _cache.Set(cacheKey, secret.Value.Value, _cacheConfig.StaticCacheTTL);
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
            
            // 🔧 CRITICAL: Get existing secret to preserve all tags
            _logger.LogInformation("  Retrieving existing secret to preserve tags...");
            KeyVaultSecret? existingSecret = null;
            try
            {
                existingSecret = await _secretClient.GetSecretAsync(tenantSecretName);
                _logger.LogInformation("  Retrieved existing secret with {TagCount} tags", existingSecret.Properties.Tags.Count);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("  Existing secret not found - creating with basic tags only");
            }
            
            // Create new version of existing secret (same secret name, new version)
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // 🔧 PRESERVE ALL EXISTING TAGS
            if (existingSecret?.Properties.Tags != null)
            {
                _logger.LogInformation("  Preserving {TagCount} existing tags from current version", existingSecret.Properties.Tags.Count);
                
                // 🔍 DETAILED TAG DEBUGGING
                _logger.LogInformation("  === EXISTING SECRET TAG DETAILS ===");
                foreach (var existingTag in existingSecret.Properties.Tags)
                {
                    var tagValue = existingTag.Value?.Length > 100 ? $"{existingTag.Value[..100]}..." : existingTag.Value;
                    _logger.LogInformation("    EXISTING TAG: {TagKey} = '{TagValue}' (Length: {Length})", 
                        existingTag.Key, tagValue, existingTag.Value?.Length ?? 0);
                    
                    secretOptions.Properties.Tags.Add(existingTag.Key, existingTag.Value);
                }
                
                _logger.LogInformation("  === NEW SECRET TAG VERIFICATION ===");
                foreach (var newTag in secretOptions.Properties.Tags)
                {
                    var tagValue = newTag.Value?.Length > 100 ? $"{newTag.Value[..100]}..." : newTag.Value;
                    _logger.LogInformation("    NEW TAG: {TagKey} = '{TagValue}' (Length: {Length})", 
                        newTag.Key, tagValue, newTag.Value?.Length ?? 0);
                }
                _logger.LogInformation("  === END TAG VERIFICATION ({NewTagCount} tags total) ===", secretOptions.Properties.Tags.Count);
            }
            else
            {
                // Fallback: Add basic organization and metadata tags only if no existing tags
                _logger.LogInformation("  No existing tags found - adding basic tags");
                secretOptions.Properties.Tags.Add("org", organizationId);
                secretOptions.Properties.Tags.Add("type", "tenant-secret");
                secretOptions.Properties.Tags.Add("secretName", secretName); // Original secret name for queries
            }
            
            // Always update the 'updated' timestamp
            secretOptions.Properties.Tags["updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            _logger.LogInformation("  Updated 'updated' timestamp tag");

            _logger.LogInformation("  Calling Azure Key Vault SetSecretAsync to create new version...");
            
            Response<KeyVaultSecret>? newVersionResponse = null;
            try
            {
                newVersionResponse = await _secretClient.SetSecretAsync(secretOptions);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "Conflict")
            {
                // If secret is deleted but recoverable, purge it and retry
                if (ex.Message.Contains("deleted but recoverable"))
                {
                    _logger.LogWarning("Secret {TenantSecretName} is in deleted but recoverable state. Auto-purging and retrying...", tenantSecretName);
                    
                    // Extract secret name from tenant secret name for the purge operation
                    var secretNameForPurge = secretName; // Use original secret name
                    var purgeSuccess = await PurgeDeletedSecretAsync(secretNameForPurge, organizationId);
                    
                    if (purgeSuccess)
                    {
                        _logger.LogInformation("Successfully purged deleted secret, retrying update with exponential backoff...");
                        
                        // Retry with exponential backoff (Azure purge can take time)
                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
                            var delay = TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1)); // 2s, 4s, 8s
                            _logger.LogInformation("Attempt {Attempt}/3: Waiting {Delay}s for Azure purge to complete...", attempt, delay.TotalSeconds);
                            await Task.Delay(delay);
                            
                            try
                            {
                                newVersionResponse = await _secretClient.SetSecretAsync(secretOptions);
                                _logger.LogInformation("Successfully updated secret after auto-purge on attempt {Attempt}", attempt);
                                break; // Success - exit retry loop
                            }
                            catch (Azure.RequestFailedException retryEx) when (retryEx.Status == 409 && retryEx.Message.Contains("currently being deleted"))
                            {
                                if (attempt == 3)
                                {
                                    _logger.LogError("Secret still being deleted after 3 attempts (total {TotalSeconds}s). Azure purge is taking longer than expected.", (2 + 4 + 8));
                                    throw new InvalidOperationException($"Secret {tenantSecretName} is still being deleted by Azure after {(2 + 4 + 8)}s. Please retry the operation in a few minutes.", retryEx);
                                }
                                _logger.LogWarning("Attempt {Attempt} failed - secret still being deleted, will retry...", attempt);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to purge deleted secret {TenantSecretName}. Manual intervention required.", tenantSecretName);
                        throw new InvalidOperationException($"Cannot update deleted secret {tenantSecretName}. Auto-purge failed. Please manually purge the secret from Key Vault.", ex);
                    }
                }
                else
                {
                    throw; // Re-throw if it's a different conflict error
                }
            }
            
            _logger.LogInformation("  Successfully created new version of secret {TenantSecretName}", tenantSecretName);

            // Get the new version URI from the response (use top-level Id property for versioned URI)
            var newVersionUri = newVersionResponse?.Value?.Id?.ToString();
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
            
            // 🔧 CRITICAL: Check if secret already exists to preserve tags
            _logger.LogInformation("  Step 3: Checking if secret already exists to preserve tags...");
            KeyVaultSecret? existingSecret = null;
            bool isUpdatingExistingSecret = false;
            try
            {
                existingSecret = await _secretClient.GetSecretAsync(tenantSecretName);
                isUpdatingExistingSecret = true;
                _logger.LogInformation("  Found existing secret with {TagCount} tags - will preserve them", existingSecret.Properties.Tags.Count);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("  Secret does not exist - creating new secret with default tags");
            }
            
            _logger.LogInformation("  Step 4: Creating Key Vault secret object...");
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // 🔧 PRESERVE ALL EXISTING TAGS if updating, otherwise create default tags
            if (isUpdatingExistingSecret && existingSecret?.Properties.Tags != null)
            {
                _logger.LogInformation("  Preserving {TagCount} existing tags from current version", existingSecret.Properties.Tags.Count);
                
                // 🔍 DETAILED TAG DEBUGGING
                _logger.LogInformation("  === EXISTING SECRET TAG DETAILS (SetSecretAsync) ===");
                foreach (var existingTag in existingSecret.Properties.Tags)
                {
                    var tagValue = existingTag.Value?.Length > 100 ? $"{existingTag.Value[..100]}..." : existingTag.Value;
                    _logger.LogInformation("    EXISTING TAG: {TagKey} = '{TagValue}' (Length: {Length})", 
                        existingTag.Key, tagValue, existingTag.Value?.Length ?? 0);
                    
                    secretOptions.Properties.Tags.Add(existingTag.Key, existingTag.Value);
                }
                
                // Update the 'updated' timestamp for existing secrets
                secretOptions.Properties.Tags["updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                _logger.LogInformation("  Updated 'updated' timestamp tag for existing secret");
                
                _logger.LogInformation("  === NEW SECRET TAG VERIFICATION (SetSecretAsync) ===");
                foreach (var newTag in secretOptions.Properties.Tags)
                {
                    var tagValue = newTag.Value?.Length > 100 ? $"{newTag.Value[..100]}..." : newTag.Value;
                    _logger.LogInformation("    NEW TAG: {TagKey} = '{TagValue}' (Length: {Length})", 
                        newTag.Key, tagValue, newTag.Value?.Length ?? 0);
                }
                _logger.LogInformation("  === END TAG VERIFICATION ({NewTagCount} tags total) ===", secretOptions.Properties.Tags.Count);
            }
            else
            {
                // Add default organization and metadata tags for new secrets
                _logger.LogInformation("  Adding default tags for new secret");
                secretOptions.Properties.Tags.Add("org", organizationId);
                secretOptions.Properties.Tags.Add("type", "tenant-secret");
                secretOptions.Properties.Tags.Add("created", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                secretOptions.Properties.Tags.Add("secretName", secretName); // Original secret name for queries
                secretOptions.Properties.Tags.Add("isActive", "true"); // New secrets are active by default
                _logger.LogInformation("  Added isActive tag: true (new secrets are active by default)");
            }

            _logger.LogInformation("  Step 5: Calling Azure Key Vault SetSecretAsync...");
            try
            {
                await _secretClient.SetSecretAsync(secretOptions);
                _logger.LogInformation("  Step 5: Azure Key Vault call succeeded");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "Conflict")
            {
                // If creating a new secret encounters a deleted secret, auto-purge and retry
                if (ex.Message.Contains("deleted but recoverable"))
                {
                    _logger.LogWarning("Secret {TenantSecretName} is in deleted but recoverable state during creation. Auto-purging and retrying...", tenantSecretName);
                    
                    var purgeSuccess = await PurgeDeletedSecretAsync(secretName, organizationId);
                    
                    if (purgeSuccess)
                    {
                        _logger.LogInformation("Successfully purged deleted secret, retrying creation with exponential backoff...");
                        
                        // Retry with exponential backoff (Azure purge can take time)
                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
                            var delay = TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1)); // 2s, 4s, 8s
                            _logger.LogInformation("Attempt {Attempt}/3: Waiting {Delay}s for Azure purge to complete...", attempt, delay.TotalSeconds);
                            await Task.Delay(delay);
                            
                            try
                            {
                                await _secretClient.SetSecretAsync(secretOptions);
                                _logger.LogInformation("Successfully created secret after auto-purge on attempt {Attempt}", attempt);
                                break; // Success - exit retry loop
                            }
                            catch (Azure.RequestFailedException retryEx) when (retryEx.Status == 409 && retryEx.Message.Contains("currently being deleted"))
                            {
                                if (attempt == 3)
                                {
                                    _logger.LogError("Secret still being deleted after 3 attempts (total {TotalSeconds}s). Azure purge is taking longer than expected.", (2 + 4 + 8));
                                    throw new InvalidOperationException($"Secret {tenantSecretName} is still being deleted by Azure after {(2 + 4 + 8)}s. Please retry the operation in a few minutes.", retryEx);
                                }
                                _logger.LogWarning("Attempt {Attempt} failed - secret still being deleted, will retry...", attempt);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to purge deleted secret {TenantSecretName}. Manual intervention required.", tenantSecretName);
                        throw new InvalidOperationException($"Cannot create secret {tenantSecretName}. Auto-purge failed. Please manually purge the secret from Key Vault.", ex);
                    }
                }
                else
                {
                    throw; // Re-throw if it's a different conflict error
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

    public async Task<bool> SetSecretWithTagsAsync(string secretName, string secretValue, string organizationId, Dictionary<string, string> additionalTags)
    {
        _logger.LogInformation("=== Key Vault SetSecretWithTagsAsync Debug ===");
        _logger.LogInformation("  SecretName: {SecretName}", secretName);
        _logger.LogInformation("  OrganizationId: {OrganizationId}", organizationId);
        _logger.LogInformation("  AdditionalTags: {TagCount}", additionalTags.Count);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("🚨 CRITICAL: Key Vault is not available - cannot store secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            _logger.LogError("🔍 Key Vault Configuration Check:");
            _logger.LogError("  - IsKeyVaultAvailable(): {IsAvailable}", IsKeyVaultAvailable());
            _logger.LogError("  - _secretClient is null: {SecretClientNull}", _secretClient == null);
            return false;
        }

        try
        {
            // Enhanced tenant isolation validation
            _logger.LogInformation("  Step 1: Validating tenant access...");
            try
            {
                await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "write");
                _logger.LogInformation("  Step 1: Tenant validation passed");
            }
            catch (Exception tenantEx)
            {
                _logger.LogError(tenantEx, "🚨 STEP 1 FAILED: Tenant validation failed for secret {SecretName}, org {OrganizationId}. Exception: {ExceptionType} - {Message}", 
                    secretName, organizationId, tenantEx.GetType().Name, tenantEx.Message);
                throw; // Re-throw to be caught by outer catch block
            }
            
            _logger.LogInformation("  Step 2: Generating tenant secret name...");
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            _logger.LogInformation("  Step 2: Generated tenant secret name: {TenantSecretName}", tenantSecretName);
            
            _logger.LogInformation("  Step 3: Creating Key Vault secret object with ALL tags...");
            var secretOptions = new KeyVaultSecret(tenantSecretName, secretValue);
            
            // Add standard organization and metadata tags
            secretOptions.Properties.Tags.Add("org", organizationId);
            secretOptions.Properties.Tags.Add("type", "tenant-secret");
            secretOptions.Properties.Tags.Add("created", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            secretOptions.Properties.Tags.Add("secretName", secretName);
            secretOptions.Properties.Tags.Add("isActive", "true");
            
            // Add ALL additional tags in the same operation
            _logger.LogInformation("  Adding {TagCount} additional tags:", additionalTags.Count);
            foreach (var tag in additionalTags)
            {
                secretOptions.Properties.Tags.Add(tag.Key, tag.Value);
                var tagValue = tag.Value?.Length > 100 ? $"{tag.Value[..100]}..." : tag.Value;
                _logger.LogInformation("    Added tag: {TagKey} = '{TagValue}' (Length: {Length})", 
                    tag.Key, tagValue, tag.Value?.Length ?? 0);
            }
            
            _logger.LogInformation("  Total tags to be set: {TotalTagCount}", secretOptions.Properties.Tags.Count);

            _logger.LogInformation("  Step 4: Calling Azure Key Vault SetSecretAsync with ALL tags...");
            try
            {
                await _secretClient.SetSecretAsync(secretOptions);
                _logger.LogInformation("  Step 4: Azure Key Vault call succeeded with all tags");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "Conflict")
            {
                // If creating a new secret encounters a deleted secret, auto-purge and retry
                if (ex.Message.Contains("deleted but recoverable"))
                {
                    _logger.LogWarning("Secret {TenantSecretName} is in deleted but recoverable state during consolidated creation. Auto-purging and retrying...", tenantSecretName);
                    
                    var purgeSuccess = await PurgeDeletedSecretAsync(secretName, organizationId);
                    
                    if (purgeSuccess)
                    {
                        _logger.LogInformation("Successfully purged deleted secret, retrying consolidated creation with exponential backoff...");
                        
                        // Retry with exponential backoff (Azure purge can take time)
                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
                            var delay = TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1)); // 2s, 4s, 8s
                            _logger.LogInformation("Attempt {Attempt}/3: Waiting {Delay}s for Azure purge to complete...", attempt, delay.TotalSeconds);
                            await Task.Delay(delay);
                            
                            try
                            {
                                await _secretClient.SetSecretAsync(secretOptions);
                                _logger.LogInformation("Successfully created consolidated secret after auto-purge on attempt {Attempt}", attempt);
                                break; // Success - exit retry loop
                            }
                            catch (Azure.RequestFailedException retryEx) when (retryEx.Status == 409 && retryEx.Message.Contains("currently being deleted"))
                            {
                                if (attempt == 3)
                                {
                                    _logger.LogError("Consolidated secret still being deleted after 3 attempts (total {TotalSeconds}s). Azure purge is taking longer than expected.", (2 + 4 + 8));
                                    throw new InvalidOperationException($"Secret {tenantSecretName} is still being deleted by Azure after {(2 + 4 + 8)}s. Please retry the operation in a few minutes.", retryEx);
                                }
                                _logger.LogWarning("Attempt {Attempt} failed - consolidated secret still being deleted, will retry...", attempt);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to purge deleted consolidated secret {TenantSecretName}. Manual intervention required.", tenantSecretName);
                        throw new InvalidOperationException($"Cannot create consolidated secret {tenantSecretName}. Auto-purge failed. Please manually purge the secret from Key Vault.", ex);
                    }
                }
                else
                {
                    throw; // Re-throw if it's a different conflict error
                }
            }
            catch (Exception kvEx)
            {
                _logger.LogError(kvEx, "🚨 STEP 4 FAILED: Azure Key Vault SetSecretAsync failed for secret {TenantSecretName}. Exception: {ExceptionType} - {Message}", 
                    tenantSecretName, kvEx.GetType().Name, kvEx.Message);
                throw; // Re-throw to be caught by outer catch block
            }

            _logger.LogInformation("🎉 Successfully created secret {SecretName} with {TagCount} total tags in SINGLE operation", 
                secretName, secretOptions.Properties.Tags.Count);
            
            // Clear any cached values for this secret
            var cacheKey = $"secret_{organizationId}_{secretName}";
            _cache.Remove(cacheKey);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 CRITICAL: Failed to store secret {SecretName} with tags for organization {OrganizationId}. Exception Type: {ExceptionType}, Message: {Message}", 
                secretName, organizationId, ex.GetType().Name, ex.Message);
            
            // Additional diagnostic logging
            _logger.LogError("🔍 Key Vault Diagnostic Info:");
            _logger.LogError("  - SecretClient Available: {SecretClientAvailable}", _secretClient != null);
            _logger.LogError("  - Organization ID: {OrganizationId}", organizationId);
            _logger.LogError("  - Secret Name: {SecretName}", secretName);
            _logger.LogError("  - Total Additional Tags: {TagCount}", additionalTags.Count);
            
            if (ex.InnerException != null)
            {
                _logger.LogError("🔍 Inner Exception: {InnerExceptionType} - {InnerExceptionMessage}", 
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
            
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
            
            _cache.Set(cacheKey, secretNames, _cacheConfig.StaticCacheTTL);
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

            // Get the new version URI from the response (use top-level Id property for versioned URI)
            var newVersionUri = newVersionResponse?.Value?.Id?.ToString();
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
    /// Purges a deleted secret completely from Key Vault (removes it from soft-delete state)
    /// </summary>
    public async Task<bool> PurgeSecretAsync(string exactSecretName)
    {
        _logger.LogInformation("=== PurgeSecretAsync ===");
        _logger.LogInformation("  ExactSecretName: {ExactSecretName}", exactSecretName);
        
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            _logger.LogError("Key Vault is not available - cannot purge secret {ExactSecretName}", exactSecretName);
            return false;
        }

        try
        {
            _logger.LogInformation("  Starting secret purge operation...");
            await _secretClient.PurgeDeletedSecretAsync(exactSecretName);
            _logger.LogInformation("✅ Successfully purged secret: {ExactSecretName}", exactSecretName);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Secret {ExactSecretName} not found for purging (404) - may not be in soft-deleted state or already purged. Response: {Response}", 
                exactSecretName, ex.Message);
            return false; // Changed to false - 404 during purge might indicate an issue
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogWarning("Insufficient permissions to purge secret {ExactSecretName} - requires 'purge' permission. Status: {Status}", 
                exactSecretName, ex.Status);
            return false;
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError("Azure request failed when purging secret {ExactSecretName}. Status: {Status}, Error: {Error}", 
                exactSecretName, ex.Status, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when purging secret {ExactSecretName}", exactSecretName);
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

            // Get the new version URI from the response (use top-level Id property for versioned URI)
            var newVersionUri = newVersionResponse?.Value?.Id?.ToString();
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

    /// <summary>
    /// Gets a secret value along with its tags for consolidated secret access
    /// </summary>
    public async Task<(string? SecretValue, Dictionary<string, string>? Tags)> GetSecretWithTagsAsync(string secretName, string organizationId)
    {
        if (!IsKeyVaultAvailable() || _secretClient == null)
        {
            return (null, null);
        }

        try
        {
            // Enhanced tenant isolation validation with security compliance
            await _tenantValidator.ValidateSecretAccessAsync(secretName, organizationId, "read");

            // Validate organization exists
            var organization = await _organizationService.GetByIdAsync(organizationId);
            if (organization == null)
            {
                _logger.LogWarning("Organization {OrganizationId} not found when retrieving secret with tags {SecretName}", 
                    organizationId, secretName);
                return (null, null);
            }

            // Generate tenant-specific secret name
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            
            var secret = await _secretClient.GetSecretAsync(tenantSecretName);
            
            if (secret?.Value != null)
            {
                // Verify organization tag matches
                if (ValidateOrganizationTag(secret.Value.Properties.Tags, organizationId))
                {
                    // Convert IDictionary<string, string> to Dictionary<string, string>
                    var tags = new Dictionary<string, string>();
                    foreach (var kvp in secret.Value.Properties.Tags)
                    {
                        tags[kvp.Key] = kvp.Value;
                    }
                    
                    return (secret.Value.Value, tags);
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
            _logger.LogError(ex, "Failed to retrieve secret with tags {SecretName} for organization {OrganizationId}", secretName, organizationId);
        }

        return (null, null);
    }

    public async Task<bool> PurgeDeletedSecretAsync(string secretName, string organizationId)
    {
        try
        {
            _logger.LogWarning("Attempting to purge deleted secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            
            if (!IsKeyVaultAvailable() || _secretClient == null)
            {
                _logger.LogError("Key Vault is not available - cannot purge deleted secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
                return false;
            }
            
            var tenantSecretName = await GenerateTenantSecretName(secretName, organizationId);
            await _secretClient.PurgeDeletedSecretAsync(tenantSecretName);
            
            _logger.LogInformation("Successfully purged deleted secret {SecretName}", tenantSecretName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge deleted secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }
    }

}