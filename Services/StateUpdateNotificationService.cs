using Microsoft.AspNetCore.SignalR;
using AdminConsole.Hubs;

namespace AdminConsole.Services;

/// <summary>
/// Service for broadcasting real-time state updates to UI clients via SignalR
/// Critical for maintaining UI-database consistency with large numbers of concurrent users
/// </summary>
public class StateUpdateNotificationService : IStateUpdateNotificationService
{
    private readonly IHubContext<StateUpdateHub> _hubContext;
    private readonly ILogger<StateUpdateNotificationService> _logger;

    public StateUpdateNotificationService(
        IHubContext<StateUpdateHub> hubContext,
        ILogger<StateUpdateNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Notify clients about user state changes (user added, removed, activated, deactivated)
    /// </summary>
    public async Task NotifyUserStateChangedAsync(string organizationId, string userId, string changeType, object? additionalData = null)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "user_state_changed",
                changeType,
                userId,
                organizationId,
                timestamp = DateTime.UtcNow,
                data = additionalData
            };

            await _hubContext.Clients.Group(groupName).SendAsync("StateUpdate", updateData);
            
            _logger.LogDebug("📢 User state change notification sent: {ChangeType} for user {UserId} in org {OrganizationId}", 
                changeType, userId, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify user state change for user {UserId} in org {OrganizationId}", 
                userId, organizationId);
        }
    }

    /// <summary>
    /// Notify clients about database credential changes (added, updated, deleted)
    /// </summary>
    public async Task NotifyCredentialStateChangedAsync(string organizationId, string credentialId, string changeType, object? additionalData = null)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "credential_state_changed",
                changeType,
                credentialId,
                organizationId,
                timestamp = DateTime.UtcNow,
                data = additionalData
            };

            await _hubContext.Clients.Group(groupName).SendAsync("StateUpdate", updateData);
            
            _logger.LogDebug("📢 Credential state change notification sent: {ChangeType} for credential {CredentialId} in org {OrganizationId}", 
                changeType, credentialId, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify credential state change for credential {CredentialId} in org {OrganizationId}", 
                credentialId, organizationId);
        }
    }

    /// <summary>
    /// Notify clients about organization changes (settings updated, status changed)
    /// </summary>
    public async Task NotifyOrganizationStateChangedAsync(string organizationId, string changeType, object? additionalData = null)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "organization_state_changed",
                changeType,
                organizationId,
                timestamp = DateTime.UtcNow,
                data = additionalData
            };

            await _hubContext.Clients.Group(groupName).SendAsync("StateUpdate", updateData);
            
            _logger.LogDebug("📢 Organization state change notification sent: {ChangeType} for org {OrganizationId}", 
                changeType, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify organization state change for org {OrganizationId}", 
                organizationId);
        }
    }

    /// <summary>
    /// Notify clients about agent type or group assignment changes
    /// </summary>
    public async Task NotifyAgentAssignmentChangedAsync(string organizationId, string userId, string changeType, object? additionalData = null)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "agent_assignment_changed",
                changeType,
                userId,
                organizationId,
                timestamp = DateTime.UtcNow,
                data = additionalData
            };

            await _hubContext.Clients.Group(groupName).SendAsync("StateUpdate", updateData);
            
            _logger.LogDebug("📢 Agent assignment change notification sent: {ChangeType} for user {UserId} in org {OrganizationId}", 
                changeType, userId, organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify agent assignment change for user {UserId} in org {OrganizationId}", 
                userId, organizationId);
        }
    }

    /// <summary>
    /// Notify clients about validation results and sync issues
    /// </summary>
    public async Task NotifyValidationResultsAsync(string organizationId, ComprehensiveStateSyncResult validationResult)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "validation_results",
                organizationId,
                timestamp = DateTime.UtcNow,
                data = new
                {
                    overallValid = validationResult.OverallValid,
                    totalIssues = validationResult.TotalIssues,
                    criticalIssues = validationResult.CriticalIssues.Take(5), // Limit for performance
                    validationTime = validationResult.ValidationTime,
                    userIssues = validationResult.UserValidation.IssuesFound,
                    groupIssues = validationResult.GroupValidation.IssuesFound,
                    credentialIssues = validationResult.CredentialValidation.IssuesFound
                }
            };

            await _hubContext.Clients.Group(groupName).SendAsync("ValidationUpdate", updateData);
            
            _logger.LogDebug("📢 Validation results notification sent for org {OrganizationId}: {TotalIssues} issues found", 
                organizationId, validationResult.TotalIssues);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify validation results for org {OrganizationId}", 
                organizationId);
        }
    }

    /// <summary>
    /// Broadcast general state refresh request to all clients in organization
    /// </summary>
    public async Task NotifyStateRefreshRequiredAsync(string organizationId, string reason)
    {
        try
        {
            var groupName = GetOrganizationGroupName(organizationId);
            var updateData = new
            {
                type = "state_refresh_required",
                organizationId,
                timestamp = DateTime.UtcNow,
                reason
            };

            await _hubContext.Clients.Group(groupName).SendAsync("RefreshRequired", updateData);
            
            _logger.LogDebug("📢 State refresh notification sent for org {OrganizationId}: {Reason}", 
                organizationId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to notify state refresh for org {OrganizationId}", 
                organizationId);
        }
    }

    /// <summary>
    /// Get standardized group name for organization isolation
    /// </summary>
    private static string GetOrganizationGroupName(string organizationId)
    {
        return $"org_{organizationId}";
    }
}