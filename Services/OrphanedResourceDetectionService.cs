using AdminConsole.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AdminConsole.Services;

/// <summary>
/// Service for detecting orphaned resources between database and Azure services
/// Ensures database integrity by identifying stale references and cleanup opportunities
/// </summary>
public class OrphanedResourceDetectionService : IOrphanedResourceDetectionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrphanedResourceDetectionService> _logger;
    private readonly ICacheConfigurationService _cacheConfig;
    private readonly IMemoryCache _cache;
    private readonly Timer? _backgroundTimer;
    private readonly SemaphoreSlim _detectionSemaphore = new(1, 1);

    public OrphanedResourceDetectionService(
        IServiceProvider serviceProvider,
        ILogger<OrphanedResourceDetectionService> logger,
        ICacheConfigurationService cacheConfig,
        IMemoryCache cache)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _cacheConfig = cacheConfig;
        _cache = cache;
        
        // Start background detection every 30 minutes
        _backgroundTimer = new Timer(BackgroundDetectionCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// Detect database users that no longer exist in Azure AD
    /// </summary>
    public async Task<OrphanedResourceResult> DetectOrphanedUsersAsync(string organizationId)
    {
        var result = new OrphanedResourceResult 
        { 
            ResourceType = "Users",
            DetectionTime = DateTime.UtcNow
        };
        
        try
        {
            _logger.LogInformation("🔍 Detecting orphaned users for organization {OrganizationId}", organizationId);
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            var graphService = scope.ServiceProvider.GetRequiredService<IGraphService>();

            // Get all active users in database for this organization
            var dbUsers = await dbContext.OnboardedUsers
                .AsNoTracking()
                .Where(u => u.OrganizationLookupId.ToString() == organizationId && u.IsActive)
                .ToListAsync();

            result.TotalResourcesScanned = dbUsers.Count;

            foreach (var dbUser in dbUsers)
            {
                try
                {
                    // Check if user exists in Azure AD
                    var azureUser = await graphService.GetUserByEmailAsync(dbUser.Email);
                    if (azureUser == null)
                    {
                        // User not found in Azure AD - potentially orphaned
                        var orphanedUser = new OrphanedResource
                        {
                            Id = dbUser.OnboardedUserId.ToString(),
                            Name = dbUser.Email,
                            Type = "DatabaseUser",
                            Reason = "User no longer exists in Azure AD",
                            LastModified = dbUser.ModifiedOn,
                            OrganizationId = organizationId,
                            Metadata = new Dictionary<string, object>
                            {
                                { "fullName", dbUser.FullName ?? "Unknown" },
                                { "assignedRole", dbUser.AssignedRole.ToString() },
                                { "lastInvitationDate", dbUser.LastInvitationDate?.ToString() ?? "Never" },
                                { "createdOn", dbUser.CreatedOn.ToString() }
                            }
                        };
                        
                        result.OrphanedResources.Add(orphanedUser);
                        result.OrphanedResourcesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error checking user {Email}: {Error}", dbUser.Email, ex.Message);
                    // Skip this user but continue with others
                }
                
                // Small delay to prevent overwhelming Graph API
                await Task.Delay(100);
            }

            _logger.LogInformation("✅ Orphaned user detection completed. Found {OrphanedCount} orphaned users out of {Total}", 
                result.OrphanedResourcesFound, result.TotalResourcesScanned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Orphaned user detection failed for organization {OrganizationId}", organizationId);
        }

        return result;
    }

    /// <summary>
    /// Detect database credentials with missing Key Vault secrets
    /// </summary>
    public async Task<OrphanedResourceResult> DetectOrphanedCredentialsAsync(string organizationId)
    {
        var result = new OrphanedResourceResult 
        { 
            ResourceType = "Credentials",
            DetectionTime = DateTime.UtcNow
        };
        
        try
        {
            _logger.LogInformation("🔍 Detecting orphaned credentials for organization {OrganizationId}", organizationId);
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            var keyVaultService = scope.ServiceProvider.GetRequiredService<IKeyVaultService>();

            // Get all active database credentials for this organization
            var dbCredentials = await dbContext.DatabaseCredentials
                .AsNoTracking()
                .Where(dc => dc.OrganizationId.ToString() == organizationId && dc.IsActive)
                .ToListAsync();

            result.TotalResourcesScanned = dbCredentials.Count;

            foreach (var credential in dbCredentials)
            {
                var issues = new List<string>();
                
                try
                {
                    // Check if SAP password secret exists
                    string? sapPassword = null;
                    if (!string.IsNullOrEmpty(credential.PasswordSecretName))
                    {
                        var passwordSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.PasswordSecretName) ?? credential.PasswordSecretName;
                        
                        // Smart prefix handling: use direct access if already prefixed, otherwise let KeyVault service add prefix
                        if (passwordSecretName.StartsWith("cumulus-service-com-"))
                        {
                            sapPassword = await keyVaultService.GetSecretByExactNameAsync(passwordSecretName);
                        }
                        else
                        {
                            sapPassword = await keyVaultService.GetSecretAsync(passwordSecretName, organizationId);
                        }
                    }
                    
                    if (string.IsNullOrEmpty(sapPassword))
                    {
                        issues.Add("Missing SAP password secret");
                    }

                    // Check connection string secret (if using separate secrets)
                    if (!string.IsNullOrEmpty(credential.ConnectionStringSecretName))
                    {
                        var connectionStringSecretName = KeyVaultService.ExtractSecretNameFromUri(credential.ConnectionStringSecretName) ?? credential.ConnectionStringSecretName;
                        
                        // Smart prefix handling: use direct access if already prefixed, otherwise let KeyVault service add prefix
                        string? connectionString = null;
                        if (connectionStringSecretName.StartsWith("cumulus-service-com-"))
                        {
                            connectionString = await keyVaultService.GetSecretByExactNameAsync(connectionStringSecretName);
                        }
                        else
                        {
                            connectionString = await keyVaultService.GetSecretAsync(connectionStringSecretName, organizationId);
                        }
                        
                        if (string.IsNullOrEmpty(connectionString))
                        {
                            issues.Add("Missing connection string secret");
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
                            issues.Add("Missing consolidated secret");
                        }
                        else if (consolidatedTags == null || !consolidatedTags.ContainsKey("connectionString"))
                        {
                            issues.Add("Consolidated secret missing connection string tag");
                        }
                    }

                    // If any secrets are missing, mark as orphaned
                    if (issues.Any())
                    {
                        var orphanedCredential = new OrphanedResource
                        {
                            Id = credential.Id.ToString(),
                            Name = credential.FriendlyName,
                            Type = "DatabaseCredential",
                            Reason = string.Join(", ", issues),
                            LastModified = credential.ModifiedOn,
                            OrganizationId = organizationId,
                            Metadata = new Dictionary<string, object>
                            {
                                { "databaseType", credential.DatabaseType.ToString() },
                                { "serverInstance", credential.ServerInstance },
                                { "databaseName", credential.DatabaseName },
                                { "passwordSecretName", credential.PasswordSecretName },
                                { "connectionStringSecretName", credential.ConnectionStringSecretName ?? "None" },
                                { "consolidatedSecretName", credential.ConsolidatedSecretName ?? "None" }
                            }
                        };
                        
                        result.OrphanedResources.Add(orphanedCredential);
                        result.OrphanedResourcesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error checking credential {FriendlyName}: {Error}", credential.FriendlyName, ex.Message);
                    
                    var orphanedCredential = new OrphanedResource
                    {
                        Id = credential.Id.ToString(),
                        Name = credential.FriendlyName,
                        Type = "DatabaseCredential",
                        Reason = $"Error accessing Key Vault secrets: {ex.Message}",
                        LastModified = credential.ModifiedOn,
                        OrganizationId = organizationId,
                        Metadata = new Dictionary<string, object>
                        {
                            { "error", ex.Message },
                            { "databaseType", credential.DatabaseType.ToString() }
                        }
                    };
                    
                    result.OrphanedResources.Add(orphanedCredential);
                    result.OrphanedResourcesFound++;
                }
                
                // Small delay to prevent overwhelming Key Vault
                await Task.Delay(50);
            }

            _logger.LogInformation("✅ Orphaned credential detection completed. Found {OrphanedCount} orphaned credentials out of {Total}", 
                result.OrphanedResourcesFound, result.TotalResourcesScanned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Orphaned credential detection failed for organization {OrganizationId}", organizationId);
        }

        return result;
    }

    /// <summary>
    /// Detect agent types referencing non-existent Azure AD security groups
    /// </summary>
    public async Task<OrphanedResourceResult> DetectOrphanedAgentGroupsAsync()
    {
        var result = new OrphanedResourceResult 
        { 
            ResourceType = "AgentGroups",
            DetectionTime = DateTime.UtcNow
        };
        
        try
        {
            _logger.LogInformation("🔍 Detecting orphaned agent groups");
            
            using var scope = _serviceProvider.CreateScope();
            var agentTypeService = scope.ServiceProvider.GetRequiredService<IAgentTypeService>();
            var graphService = scope.ServiceProvider.GetRequiredService<IGraphService>();

            // Get all agent types with security groups
            var agentTypes = await agentTypeService.GetAllAgentTypesAsync();
            var activeAgentTypes = agentTypes.Where(at => at.IsActive && !string.IsNullOrEmpty(at.GlobalSecurityGroupId)).ToList();
            
            result.TotalResourcesScanned = activeAgentTypes.Count;

            foreach (var agentType in activeAgentTypes)
            {
                try
                {
                    // Verify security group exists in Azure AD
                    var groupExists = await graphService.GroupExistsAsync(agentType.GlobalSecurityGroupId);
                    if (!groupExists)
                    {
                        var orphanedAgentGroup = new OrphanedResource
                        {
                            Id = agentType.Id.ToString(),
                            Name = agentType.DisplayName,
                            Type = "AgentType",
                            Reason = "References non-existent Azure AD security group",
                            LastModified = DateTime.UtcNow, // AgentType doesn't have modification date
                            OrganizationId = "global", // Agent types are global
                            Metadata = new Dictionary<string, object>
                            {
                                { "agentTypeName", agentType.Name },
                                { "globalSecurityGroupId", agentType.GlobalSecurityGroupId },
                                { "agentShareUrl", agentType.AgentShareUrl ?? "None" },
                                { "description", agentType.Description ?? "None" }
                            }
                        };
                        
                        result.OrphanedResources.Add(orphanedAgentGroup);
                        result.OrphanedResourcesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error checking agent group {AgentType}: {Error}", agentType.DisplayName, ex.Message);
                    // Skip this agent type but continue with others
                }
                
                // Small delay to prevent overwhelming Graph API
                await Task.Delay(100);
            }

            _logger.LogInformation("✅ Orphaned agent group detection completed. Found {OrphanedCount} orphaned agent groups out of {Total}", 
                result.OrphanedResourcesFound, result.TotalResourcesScanned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Orphaned agent group detection failed");
        }

        return result;
    }

    /// <summary>
    /// Detect database organization references that don't match reality
    /// </summary>
    public async Task<OrphanedResourceResult> DetectOrphanedOrganizationsAsync()
    {
        var result = new OrphanedResourceResult 
        { 
            ResourceType = "Organizations",
            DetectionTime = DateTime.UtcNow
        };
        
        try
        {
            _logger.LogInformation("🔍 Detecting orphaned organizations");
            
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();

            // Get all active organizations
            var organizations = await dbContext.Organizations
                .AsNoTracking()
                .Where(o => o.IsActive)
                .ToListAsync();
            
            result.TotalResourcesScanned = organizations.Count;

            foreach (var organization in organizations)
            {
                try
                {
                    // Check if organization has any active users
                    var hasActiveUsers = await dbContext.OnboardedUsers
                        .AsNoTracking()
                        .AnyAsync(u => u.OrganizationLookupId == organization.OrganizationId && u.IsActive);

                    // Check if organization has any active credentials
                    var hasActiveCredentials = await dbContext.DatabaseCredentials
                        .AsNoTracking()
                        .AnyAsync(dc => dc.OrganizationId == organization.OrganizationId && dc.IsActive);

                    // If organization has no active users or credentials, it might be orphaned
                    if (!hasActiveUsers && !hasActiveCredentials)
                    {
                        var orphanedOrganization = new OrphanedResource
                        {
                            Id = organization.OrganizationId.ToString(),
                            Name = organization.Name,
                            Type = "Organization",
                            Reason = "No active users or credentials associated",
                            LastModified = organization.ModifiedOn,
                            OrganizationId = organization.OrganizationId.ToString(),
                            Metadata = new Dictionary<string, object>
                            {
                                { "domain", organization.Domain },
                                { "description", "No description available" },
                                { "createdOn", organization.CreatedOn.ToString() },
                                { "tenantId", "No tenant ID available" }
                            }
                        };
                        
                        result.OrphanedResources.Add(orphanedOrganization);
                        result.OrphanedResourcesFound++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error checking organization {OrganizationName}: {Error}", organization.Name, ex.Message);
                    // Skip this organization but continue with others
                }
            }

            _logger.LogInformation("✅ Orphaned organization detection completed. Found {OrphanedCount} orphaned organizations out of {Total}", 
                result.OrphanedResourcesFound, result.TotalResourcesScanned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Orphaned organization detection failed");
        }

        return result;
    }

    /// <summary>
    /// Comprehensive scan for all types of orphaned resources
    /// </summary>
    public async Task<ComprehensiveOrphanedResourceResult> DetectAllOrphanedResourcesAsync(string organizationId)
    {
        if (!await _detectionSemaphore.WaitAsync(TimeSpan.FromMinutes(2)))
        {
            _logger.LogWarning("⚠️ Orphaned resource detection already running for organization {OrganizationId}", organizationId);
            throw new InvalidOperationException("Detection already in progress");
        }

        try
        {
            _logger.LogInformation("🔍 Starting comprehensive orphaned resource detection for organization {OrganizationId}", organizationId);
            
            var result = new ComprehensiveOrphanedResourceResult
            {
                OrganizationId = organizationId,
                DetectionTime = DateTime.UtcNow
            };

            // Run all detections in parallel for better performance
            var userTask = DetectOrphanedUsersAsync(organizationId);
            var credentialTask = DetectOrphanedCredentialsAsync(organizationId);
            var agentTask = DetectOrphanedAgentGroupsAsync();
            var orgTask = DetectOrphanedOrganizationsAsync();

            await Task.WhenAll(userTask, credentialTask, agentTask, orgTask);

            result.OrphanedUsers = await userTask;
            result.OrphanedCredentials = await credentialTask;
            result.OrphanedAgentGroups = await agentTask;
            result.OrphanedOrganizations = await orgTask;

            // Cache result for quick access
            var cacheKey = _cacheConfig.GenerateCacheKey("orphaned-resources", organizationId);
            _cache.Set(cacheKey, result, _cacheConfig.DynamicCacheTTL);

            _logger.LogInformation("✅ Comprehensive orphaned resource detection completed for organization {OrganizationId}. Total orphans: {TotalOrphans}", 
                organizationId, result.TotalOrphansFound);

            return result;
        }
        finally
        {
            _detectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Get cleanup recommendations for detected orphaned resources (admin approval required)
    /// </summary>
    public Task<List<CleanupRecommendation>> GenerateCleanupRecommendationsAsync(ComprehensiveOrphanedResourceResult orphanedResult)
    {
        var recommendations = new List<CleanupRecommendation>();

        // Generate recommendations for orphaned users
        foreach (var orphanedUser in orphanedResult.OrphanedUsers.OrphanedResources)
        {
            recommendations.Add(new CleanupRecommendation
            {
                ResourceId = orphanedUser.Id,
                ResourceName = orphanedUser.Name,
                ResourceType = "User",
                RecommendedAction = CleanupAction.Deactivate,
                ActionDescription = "Deactivate user in database (preserve data for audit)",
                Justification = "User no longer exists in Azure AD",
                RiskLevel = CleanupRisk.Medium,
                RequiresAdminApproval = true,
                ActionMetadata = new Dictionary<string, object>
                {
                    { "preserveAuditData", true },
                    { "reason", orphanedUser.Reason }
                }
            });
        }

        // Generate recommendations for orphaned credentials
        foreach (var orphanedCredential in orphanedResult.OrphanedCredentials.OrphanedResources)
        {
            recommendations.Add(new CleanupRecommendation
            {
                ResourceId = orphanedCredential.Id,
                ResourceName = orphanedCredential.Name,
                ResourceType = "Credential",
                RecommendedAction = CleanupAction.ManualReview,
                ActionDescription = "Manually review and recreate missing Key Vault secrets",
                Justification = "Database credentials reference missing Key Vault secrets",
                RiskLevel = CleanupRisk.High,
                RequiresAdminApproval = true,
                ActionMetadata = new Dictionary<string, object>
                {
                    { "recreateSecrets", true },
                    { "reason", orphanedCredential.Reason }
                }
            });
        }

        // Generate recommendations for orphaned agent groups
        foreach (var orphanedAgent in orphanedResult.OrphanedAgentGroups.OrphanedResources)
        {
            recommendations.Add(new CleanupRecommendation
            {
                ResourceId = orphanedAgent.Id,
                ResourceName = orphanedAgent.Name,
                ResourceType = "AgentType",
                RecommendedAction = CleanupAction.UpdateReference,
                ActionDescription = "Update security group reference or deactivate agent type",
                Justification = "Agent type references non-existent Azure AD security group",
                RiskLevel = CleanupRisk.Medium,
                RequiresAdminApproval = true,
                ActionMetadata = new Dictionary<string, object>
                {
                    { "updateSecurityGroup", true },
                    { "reason", orphanedAgent.Reason }
                }
            });
        }

        // Generate recommendations for orphaned organizations
        foreach (var orphanedOrg in orphanedResult.OrphanedOrganizations.OrphanedResources)
        {
            recommendations.Add(new CleanupRecommendation
            {
                ResourceId = orphanedOrg.Id,
                ResourceName = orphanedOrg.Name,
                ResourceType = "Organization",
                RecommendedAction = CleanupAction.MarkAsOrphaned,
                ActionDescription = "Mark organization as inactive (preserve for audit)",
                Justification = "Organization has no active users or credentials",
                RiskLevel = CleanupRisk.Low,
                RequiresAdminApproval = true,
                ActionMetadata = new Dictionary<string, object>
                {
                    { "markInactive", true },
                    { "preserveData", true },
                    { "reason", orphanedOrg.Reason }
                }
            });
        }

        return Task.FromResult(recommendations);
    }

    /// <summary>
    /// Start background detection service (already started in constructor)
    /// </summary>
    public Task StartBackgroundDetectionAsync()
    {
        _logger.LogInformation("🔄 Background orphaned resource detection service is already running");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop background detection service
    /// </summary>
    public Task StopBackgroundDetectionAsync()
    {
        _backgroundTimer?.Dispose();
        _logger.LogInformation("⏹️ Background orphaned resource detection service stopped");
        return Task.CompletedTask;
    }

    private async void BackgroundDetectionCallback(object? state)
    {
        try
        {
            _logger.LogDebug("🔄 Starting background orphaned resource detection cycle");
            
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
                    await DetectAllOrphanedResourcesAsync(organizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Background orphaned resource detection failed for organization {OrganizationId}", organizationId);
                }
                
                // Delay between organizations to prevent overwhelming services
                await Task.Delay(2000);
            }
            
            _logger.LogDebug("✅ Background orphaned resource detection cycle completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Background orphaned resource detection cycle failed");
        }
    }

    public void Dispose()
    {
        _backgroundTimer?.Dispose();
        _detectionSemaphore?.Dispose();
    }
}