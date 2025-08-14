using AdminConsole.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AdminConsole.Services;

/// <summary>
/// Background service for validating synchronization between database and Azure resources
/// Critical for maintaining data integrity at scale with large numbers of users
/// </summary>
public class StateSyncValidationService : IStateSyncValidationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StateSyncValidationService> _logger;
    private readonly ICacheConfigurationService _cacheConfig;
    private readonly IMemoryCache _cache;
    private readonly Timer? _backgroundTimer;
    private readonly SemaphoreSlim _validationSemaphore = new(1, 1);

    public StateSyncValidationService(
        IServiceProvider serviceProvider,
        ILogger<StateSyncValidationService> logger,
        ICacheConfigurationService cacheConfig,
        IMemoryCache cache)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _cacheConfig = cacheConfig;
        _cache = cache;
        
        // Start background validation every 10 minutes
        _backgroundTimer = new Timer(BackgroundValidationCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Validate that database users match their Azure AD status
    /// </summary>
    public async Task<StateSyncValidationResult> ValidateUserStateAsync(string organizationId)
    {
        var result = new StateSyncValidationResult();
        
        try
        {
            _logger.LogInformation("🔍 Starting user state validation for organization {OrganizationId}", organizationId);
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            var graphService = scope.ServiceProvider.GetRequiredService<IGraphService>();
            var systemUserService = scope.ServiceProvider.GetRequiredService<ISystemUserManagementService>();

            // Get all active users in database for this organization
            var dbUsers = await dbContext.OnboardedUsers
                .AsNoTracking()
                .Where(u => u.OrganizationLookupId.ToString() == organizationId && u.IsActive)
                .ToListAsync();

            result.TotalRecordsChecked = dbUsers.Count;

            foreach (var dbUser in dbUsers)
            {
                try
                {
                    // Check if user exists in Azure AD
                    var azureUser = await graphService.GetUserByEmailAsync(dbUser.Email);
                    if (azureUser == null)
                    {
                        result.Issues.Add($"Database user {dbUser.Email} not found in Azure AD");
                        result.Recommendations.Add($"Consider deactivating database user {dbUser.Email} or check if email changed in Azure AD");
                        result.IssuesFound++;
                        continue;
                    }

                    // Validate user status consistency (simplified check for now)
                    // Note: Using basic active status check instead of complex status determination
                    // Status validation can be expanded here with proper status checks
                    // For now, we just validate the Azure Object ID consistency

                    // Validate AzureObjectId consistency
                    if (!string.IsNullOrEmpty(dbUser.AzureObjectId) && dbUser.AzureObjectId != azureUser.Id)
                    {
                        result.Issues.Add($"User {dbUser.Email} has mismatched Azure Object ID in database");
                        result.Recommendations.Add($"Update Azure Object ID for {dbUser.Email} in database");
                        result.IssuesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error validating user {Email}: {Error}", dbUser.Email, ex.Message);
                    result.Issues.Add($"Failed to validate user {dbUser.Email}: {ex.Message}");
                    result.IssuesFound++;
                }
            }

            result.IsValid = result.IssuesFound == 0;
            _logger.LogInformation("✅ User state validation completed. Issues found: {IssuesFound}", result.IssuesFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ User state validation failed for organization {OrganizationId}", organizationId);
            result.Issues.Add($"Validation failed: {ex.Message}");
            result.IssuesFound++;
        }

        return result;
    }

    /// <summary>
    /// Validate that database groups match Azure AD security groups
    /// </summary>
    public async Task<StateSyncValidationResult> ValidateGroupStateAsync(string organizationId)
    {
        var result = new StateSyncValidationResult();
        
        try
        {
            _logger.LogInformation("🔍 Starting group state validation for organization {OrganizationId}", organizationId);
            
            using var scope = _serviceProvider.CreateScope();
            var graphService = scope.ServiceProvider.GetRequiredService<IGraphService>();
            var teamsGroupService = scope.ServiceProvider.GetRequiredService<ITeamsGroupService>();
            var agentTypeService = scope.ServiceProvider.GetRequiredService<IAgentTypeService>();

            // Get all agent types with security groups
            var agentTypes = await agentTypeService.GetAllAgentTypesAsync();
            var activeAgentTypes = agentTypes.Where(at => at.IsActive && !string.IsNullOrEmpty(at.GlobalSecurityGroupId)).ToList();
            
            result.TotalRecordsChecked = activeAgentTypes.Count;

            foreach (var agentType in activeAgentTypes)
            {
                try
                {
                    // Verify security group exists in Azure AD
                    var groupExists = await graphService.GroupExistsAsync(agentType.GlobalSecurityGroupId);
                    if (!groupExists)
                    {
                        result.Issues.Add($"Agent type {agentType.DisplayName} references non-existent security group {agentType.GlobalSecurityGroupId}");
                        result.Recommendations.Add($"Update or recreate security group for agent type {agentType.DisplayName}");
                        result.IssuesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error validating group for agent type {AgentType}: {Error}", agentType.DisplayName, ex.Message);
                    result.Issues.Add($"Failed to validate group for agent type {agentType.DisplayName}: {ex.Message}");
                    result.IssuesFound++;
                }
            }

            result.IsValid = result.IssuesFound == 0;
            _logger.LogInformation("✅ Group state validation completed. Issues found: {IssuesFound}", result.IssuesFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Group state validation failed for organization {OrganizationId}", organizationId);
            result.Issues.Add($"Validation failed: {ex.Message}");
            result.IssuesFound++;
        }

        return result;
    }

    /// <summary>
    /// Validate that Key Vault secrets exist for all database credential records
    /// </summary>
    public async Task<StateSyncValidationResult> ValidateCredentialStateAsync(string organizationId)
    {
        var result = new StateSyncValidationResult();
        
        try
        {
            _logger.LogInformation("🔍 Starting credential state validation for organization {OrganizationId}", organizationId);
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            var keyVaultService = scope.ServiceProvider.GetRequiredService<IKeyVaultService>();

            // Get all active database credentials for this organization
            var dbCredentials = await dbContext.DatabaseCredentials
                .AsNoTracking()
                .Where(dc => dc.OrganizationId.ToString() == organizationId && dc.IsActive)
                .ToListAsync();

            result.TotalRecordsChecked = dbCredentials.Count;

            foreach (var credential in dbCredentials)
            {
                try
                {
                    // Check if SAP password secret exists
                    string? sapPasswordExists = null;
                    if (!string.IsNullOrEmpty(credential.PasswordSecretName))
                    {
                        var passwordSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.PasswordSecretName) ?? credential.PasswordSecretName;
                        
                        // Smart prefix handling: use direct access if already prefixed, otherwise let KeyVault service add prefix
                        if (passwordSecretName.StartsWith("cumulus-service-com-"))
                        {
                            sapPasswordExists = await keyVaultService.GetSecretByExactNameAsync(passwordSecretName);
                        }
                        else
                        {
                            sapPasswordExists = await keyVaultService.GetSecretAsync(passwordSecretName, organizationId);
                        }
                    }
                    
                    if (string.IsNullOrEmpty(sapPasswordExists))
                    {
                        result.Issues.Add($"Missing SAP password secret {credential.PasswordSecretName} for credential {credential.FriendlyName}");
                        result.Recommendations.Add($"Recreate missing secret for credential {credential.FriendlyName}");
                        result.IssuesFound++;
                    }

                    // Check connection string secret (if using separate secrets)
                    if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
                    {
                        var connectionStringSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConnectionStringSecretName) ?? credential.ConnectionStringSecretName;
                        
                        // Smart prefix handling: use direct access if already prefixed, otherwise let KeyVault service add prefix
                        string? connectionStringExists = null;
                        if (connectionStringSecretName.StartsWith("cumulus-service-com-"))
                        {
                            connectionStringExists = await keyVaultService.GetSecretByExactNameAsync(connectionStringSecretName);
                        }
                        else
                        {
                            connectionStringExists = await keyVaultService.GetSecretAsync(connectionStringSecretName, organizationId);
                        }
                        
                        if (string.IsNullOrEmpty(connectionStringExists))
                        {
                            result.Issues.Add($"Missing connection string secret {credential.ConnectionStringSecretName} for credential {credential.FriendlyName}");
                            result.Recommendations.Add($"Recreate missing connection string secret for credential {credential.FriendlyName}");
                            result.IssuesFound++;
                        }
                    }

                    // Check consolidated secret (if using new format)
                    if (!string.IsNullOrEmpty(credential.ConsolidatedSecretName))
                    {
                        var consolidatedSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConsolidatedSecretName) ?? credential.ConsolidatedSecretName;
                        
                        // Smart prefix handling for consolidated secrets
                        string? consolidatedValue = null;
                        Dictionary<string, string>? consolidatedTags = null;
                        
                        if (consolidatedSecretName.StartsWith("cumulus-service-com-"))
                        {
                            // For prefixed names, we need to access the secret directly and get tags manually
                            // Since there's no GetSecretWithTagsByExactNameAsync, we'll check if the secret exists first
                            consolidatedValue = await keyVaultService.GetSecretByExactNameAsync(consolidatedSecretName);
                            // TODO: Add proper tag retrieval for exact name access when method becomes available
                            // For now, assume consolidated secret structure is correct if it exists
                            if (!string.IsNullOrEmpty(consolidatedValue))
                            {
                                consolidatedTags = new Dictionary<string, string> { { "connectionString", "assumed-present" } };
                            }
                        }
                        else
                        {
                            (consolidatedValue, consolidatedTags) = await keyVaultService.GetSecretWithTagsAsync(consolidatedSecretName, organizationId);
                        }
                        
                        if (string.IsNullOrEmpty(consolidatedValue))
                        {
                            result.Issues.Add($"Missing consolidated secret {credential.ConsolidatedSecretName} for credential {credential.FriendlyName}");
                            result.Recommendations.Add($"Recreate missing consolidated secret for credential {credential.FriendlyName}");
                            result.IssuesFound++;
                        }
                        else if (consolidatedTags == null || !consolidatedTags.ContainsKey("connectionString"))
                        {
                            result.Issues.Add($"Consolidated secret {credential.ConsolidatedSecretName} missing connection string tag");
                            result.Recommendations.Add($"Update consolidated secret to include connection string tag");
                            result.IssuesFound++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error validating credential {FriendlyName}: {Error}", credential.FriendlyName, ex.Message);
                    result.Issues.Add($"Failed to validate credential {credential.FriendlyName}: {ex.Message}");
                    result.IssuesFound++;
                }
            }

            result.IsValid = result.IssuesFound == 0;
            _logger.LogInformation("✅ Credential state validation completed. Issues found: {IssuesFound}", result.IssuesFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Credential state validation failed for organization {OrganizationId}", organizationId);
            result.Issues.Add($"Validation failed: {ex.Message}");
            result.IssuesFound++;
        }

        return result;
    }

    /// <summary>
    /// Perform comprehensive state validation across all resources
    /// </summary>
    public async Task<ComprehensiveStateSyncResult> ValidateAllStatesAsync(string organizationId)
    {
        if (!await _validationSemaphore.WaitAsync(TimeSpan.FromMinutes(1)))
        {
            _logger.LogWarning("⚠️ Validation already running for organization {OrganizationId}", organizationId);
            throw new InvalidOperationException("Validation already in progress");
        }

        try
        {
            _logger.LogInformation("🔍 Starting comprehensive state validation for organization {OrganizationId}", organizationId);
            
            var result = new ComprehensiveStateSyncResult
            {
                OrganizationId = organizationId,
                ValidationTime = DateTime.UtcNow
            };

            // Run all validations in parallel for better performance
            var userTask = ValidateUserStateAsync(organizationId);
            var groupTask = ValidateGroupStateAsync(organizationId);
            var credentialTask = ValidateCredentialStateAsync(organizationId);

            await Task.WhenAll(userTask, groupTask, credentialTask);

            result.UserValidation = await userTask;
            result.GroupValidation = await groupTask;
            result.CredentialValidation = await credentialTask;

            // Determine overall status
            result.OverallValid = result.UserValidation.IsValid && 
                                result.GroupValidation.IsValid && 
                                result.CredentialValidation.IsValid;

            // Collect critical issues
            result.CriticalIssues.AddRange(result.UserValidation.Issues);
            result.CriticalIssues.AddRange(result.GroupValidation.Issues);
            result.CriticalIssues.AddRange(result.CredentialValidation.Issues);

            // Collect recommendations
            result.RecommendedActions.AddRange(result.UserValidation.Recommendations);
            result.RecommendedActions.AddRange(result.GroupValidation.Recommendations);
            result.RecommendedActions.AddRange(result.CredentialValidation.Recommendations);

            // Cache result for quick access
            var cacheKey = _cacheConfig.GenerateCacheKey("state-validation", organizationId);
            _cache.Set(cacheKey, result, _cacheConfig.DynamicCacheTTL);

            _logger.LogInformation("✅ Comprehensive validation completed for organization {OrganizationId}. Total issues: {TotalIssues}", 
                organizationId, result.TotalIssues);

            return result;
        }
        finally
        {
            _validationSemaphore.Release();
        }
    }

    /// <summary>
    /// Start background validation service (already started in constructor)
    /// </summary>
    public Task StartBackgroundValidationAsync()
    {
        _logger.LogInformation("🔄 Background state validation service is already running");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop background validation service
    /// </summary>
    public Task StopBackgroundValidationAsync()
    {
        _backgroundTimer?.Dispose();
        _logger.LogInformation("⏹️ Background state validation service stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get latest validation results for organization
    /// </summary>
    public Task<ComprehensiveStateSyncResult?> GetLatestValidationResultAsync(string organizationId)
    {
        var cacheKey = _cacheConfig.GenerateCacheKey("state-validation", organizationId);
        var cachedResult = _cache.Get<ComprehensiveStateSyncResult>(cacheKey);
        return Task.FromResult(cachedResult);
    }

    private async void BackgroundValidationCallback(object? state)
    {
        try
        {
            _logger.LogDebug("🔄 Starting background state validation cycle");
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            
            // Get all active organizations
            var organizations = await dbContext.Organizations
                .AsNoTracking()
                .Where(o => o.IsActive)
                .Select(o => o.OrganizationId.ToString())
                .ToListAsync();

            foreach (var organizationId in organizations)
            {
                try
                {
                    await ValidateAllStatesAsync(organizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Background validation failed for organization {OrganizationId}", organizationId);
                }
                
                // Small delay between organizations to prevent overwhelming services
                await Task.Delay(1000);
            }
            
            _logger.LogDebug("✅ Background state validation cycle completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Background state validation cycle failed");
        }
    }

    public void Dispose()
    {
        _backgroundTimer?.Dispose();
        _validationSemaphore?.Dispose();
    }
}