namespace AdminConsole.Services;

/// <summary>
/// Service for validating synchronization between database and Azure resources
/// Ensures database always reflects the true state of Azure/UI resources
/// </summary>
public interface IStateSyncValidationService
{
    /// <summary>
    /// Validate that database users match their Azure AD status
    /// </summary>
    Task<StateSyncValidationResult> ValidateUserStateAsync(string organizationId);
    
    /// <summary>
    /// Validate that database groups match Azure AD security groups
    /// </summary>
    Task<StateSyncValidationResult> ValidateGroupStateAsync(string organizationId);
    
    /// <summary>
    /// Validate that Key Vault secrets exist for all database credential records
    /// </summary>
    Task<StateSyncValidationResult> ValidateCredentialStateAsync(string organizationId);
    
    /// <summary>
    /// Perform comprehensive state validation across all resources
    /// </summary>
    Task<ComprehensiveStateSyncResult> ValidateAllStatesAsync(string organizationId);
    
    /// <summary>
    /// Start background validation service (runs every 10 minutes)
    /// </summary>
    Task StartBackgroundValidationAsync();
    
    /// <summary>
    /// Stop background validation service
    /// </summary>
    Task StopBackgroundValidationAsync();
    
    /// <summary>
    /// Get latest validation results for organization
    /// </summary>
    Task<ComprehensiveStateSyncResult?> GetLatestValidationResultAsync(string organizationId);
}

/// <summary>
/// Result of state synchronization validation
/// </summary>
public class StateSyncValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
    public int TotalRecordsChecked { get; set; }
    public int IssuesFound { get; set; }
}

/// <summary>
/// Comprehensive state sync validation result
/// </summary>
public class ComprehensiveStateSyncResult
{
    public string OrganizationId { get; set; } = string.Empty;
    public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
    public bool OverallValid { get; set; }
    
    public StateSyncValidationResult UserValidation { get; set; } = new();
    public StateSyncValidationResult GroupValidation { get; set; } = new();
    public StateSyncValidationResult CredentialValidation { get; set; } = new();
    
    public List<string> CriticalIssues { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    
    public int TotalIssues => UserValidation.IssuesFound + GroupValidation.IssuesFound + CredentialValidation.IssuesFound;
}