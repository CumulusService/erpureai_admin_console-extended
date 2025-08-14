using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using AdminConsole.Services;

namespace AdminConsole.Hubs;

/// <summary>
/// SignalR hub for real-time UI state updates
/// Ensures UI immediately reflects database changes for all connected users
/// </summary>
[Authorize]
public class StateUpdateHub : Hub
{
    private readonly ILogger<StateUpdateHub> _logger;
    private readonly IDataIsolationService _dataIsolationService;

    public StateUpdateHub(ILogger<StateUpdateHub> logger, IDataIsolationService dataIsolationService)
    {
        _logger = logger;
        _dataIsolationService = dataIsolationService;
    }

    /// <summary>
    /// Handle client connection - join organization-specific group
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        try
        {
            // Get user's organization ID for proper group isolation
            var organizationId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            if (!string.IsNullOrEmpty(organizationId))
            {
                var groupName = GetOrganizationGroupName(organizationId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                
                _logger.LogDebug("🔗 User connected to SignalR group {GroupName} (Connection: {ConnectionId})", 
                    groupName, Context.ConnectionId);
            }
            else
            {
                _logger.LogWarning("⚠️ User connected without valid organization ID (Connection: {ConnectionId})", 
                    Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling SignalR connection {ConnectionId}", Context.ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// Handle client disconnection
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var organizationId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            if (!string.IsNullOrEmpty(organizationId))
            {
                var groupName = GetOrganizationGroupName(organizationId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                
                _logger.LogDebug("🔌 User disconnected from SignalR group {GroupName} (Connection: {ConnectionId})", 
                    groupName, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling SignalR disconnection {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Client can request to join a specific organization group (with validation)
    /// </summary>
    public async Task JoinOrganizationGroup(string organizationId)
    {
        try
        {
            // Validate user has access to this organization
            var currentOrgId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            if (currentOrgId != organizationId)
            {
                _logger.LogWarning("🚫 User attempted to join unauthorized organization group {OrganizationId} (Connection: {ConnectionId})", 
                    organizationId, Context.ConnectionId);
                return;
            }

            var groupName = GetOrganizationGroupName(organizationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            
            _logger.LogDebug("✅ User manually joined SignalR group {GroupName} (Connection: {ConnectionId})", 
                groupName, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error joining organization group {OrganizationId} for connection {ConnectionId}", 
                organizationId, Context.ConnectionId);
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