namespace AdminConsole.Services;

/// <summary>
/// Service for detecting orphaned resources between database and Azure services
/// Critical for maintaining clean state and preventing accumulation of stale data
/// </summary>
public interface IOrphanedResourceDetectionService
{
    /// <summary>
    /// Detect database users that no longer exist in Azure AD
    /// </summary>
    Task<OrphanedResourceResult> DetectOrphanedUsersAsync(string organizationId);
    
    /// <summary>
    /// Detect database credentials with missing Key Vault secrets
    /// </summary>
    Task<OrphanedResourceResult> DetectOrphanedCredentialsAsync(string organizationId);
    
    /// <summary>
    /// Detect agent types referencing non-existent Azure AD security groups
    /// </summary>
    Task<OrphanedResourceResult> DetectOrphanedAgentGroupsAsync();
    
    /// <summary>
    /// Detect database organization references that don't match reality
    /// </summary>
    Task<OrphanedResourceResult> DetectOrphanedOrganizationsAsync();
    
    /// <summary>
    /// Comprehensive scan for all types of orphaned resources
    /// </summary>
    Task<ComprehensiveOrphanedResourceResult> DetectAllOrphanedResourcesAsync(string organizationId);
    
    /// <summary>
    /// Get cleanup recommendations for detected orphaned resources (admin approval required)
    /// </summary>
    Task<List<CleanupRecommendation>> GenerateCleanupRecommendationsAsync(ComprehensiveOrphanedResourceResult orphanedResult);
    
    /// <summary>
    /// Start background orphaned resource detection (runs every 30 minutes)
    /// </summary>
    Task StartBackgroundDetectionAsync();
    
    /// <summary>
    /// Stop background detection service
    /// </summary>
    Task StopBackgroundDetectionAsync();
}

/// <summary>
/// Result of orphaned resource detection
/// </summary>
public class OrphanedResourceResult
{
    public string ResourceType { get; set; } = string.Empty;
    public int TotalResourcesScanned { get; set; }
    public int OrphanedResourcesFound { get; set; }
    public List<OrphanedResource> OrphanedResources { get; set; } = new();
    public DateTime DetectionTime { get; set; } = DateTime.UtcNow;
    public bool HasOrphans => OrphanedResourcesFound > 0;
}

/// <summary>
/// Details of an orphaned resource
/// </summary>
public class OrphanedResource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Comprehensive orphaned resource detection result
/// </summary>
public class ComprehensiveOrphanedResourceResult
{
    public string OrganizationId { get; set; } = string.Empty;
    public DateTime DetectionTime { get; set; } = DateTime.UtcNow;
    
    public OrphanedResourceResult OrphanedUsers { get; set; } = new();
    public OrphanedResourceResult OrphanedCredentials { get; set; } = new();
    public OrphanedResourceResult OrphanedAgentGroups { get; set; } = new();
    public OrphanedResourceResult OrphanedOrganizations { get; set; } = new();
    
    public int TotalOrphansFound => OrphanedUsers.OrphanedResourcesFound + 
                                  OrphanedCredentials.OrphanedResourcesFound +
                                  OrphanedAgentGroups.OrphanedResourcesFound +
                                  OrphanedOrganizations.OrphanedResourcesFound;
    
    public bool HasCriticalOrphans => TotalOrphansFound > 0;
}

/// <summary>
/// Cleanup recommendation for orphaned resources
/// </summary>
public class CleanupRecommendation
{
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public CleanupAction RecommendedAction { get; set; }
    public string ActionDescription { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public CleanupRisk RiskLevel { get; set; }
    public bool RequiresAdminApproval { get; set; } = true;
    public Dictionary<string, object> ActionMetadata { get; set; } = new();
}

/// <summary>
/// Types of cleanup actions
/// </summary>
public enum CleanupAction
{
    Deactivate,
    MarkAsOrphaned,
    Delete,
    UpdateReference,
    ManualReview
}

/// <summary>
/// Risk levels for cleanup operations
/// </summary>
public enum CleanupRisk
{
    Low,
    Medium,
    High,
    Critical
}