namespace AdminConsole.Services;

/// <summary>
/// Service for broadcasting real-time state updates to UI clients
/// Ensures immediate UI refresh when database changes occur
/// </summary>
public interface IStateUpdateNotificationService
{
    /// <summary>
    /// Notify clients about user state changes (user added, removed, activated, deactivated)
    /// </summary>
    Task NotifyUserStateChangedAsync(string organizationId, string userId, string changeType, object? additionalData = null);
    
    /// <summary>
    /// Notify clients about database credential changes (added, updated, deleted)
    /// </summary>
    Task NotifyCredentialStateChangedAsync(string organizationId, string credentialId, string changeType, object? additionalData = null);
    
    /// <summary>
    /// Notify clients about organization changes (settings updated, status changed)
    /// </summary>
    Task NotifyOrganizationStateChangedAsync(string organizationId, string changeType, object? additionalData = null);
    
    /// <summary>
    /// Notify clients about agent type or group assignment changes
    /// </summary>
    Task NotifyAgentAssignmentChangedAsync(string organizationId, string userId, string changeType, object? additionalData = null);
    
    /// <summary>
    /// Notify clients about validation results and sync issues
    /// </summary>
    Task NotifyValidationResultsAsync(string organizationId, ComprehensiveStateSyncResult validationResult);
    
    /// <summary>
    /// Broadcast general state refresh request to all clients in organization
    /// </summary>
    Task NotifyStateRefreshRequiredAsync(string organizationId, string reason);
}