using Microsoft.Extensions.Diagnostics.HealthChecks;
using AdminConsole.Services;

namespace AdminConsole.HealthChecks;

/// <summary>
/// Health check for Microsoft Graph API connectivity
/// </summary>
public class GraphApiHealthCheck : IHealthCheck
{
    private readonly IGraphService _graphService;
    private readonly ILogger<GraphApiHealthCheck> _logger;

    public GraphApiHealthCheck(IGraphService graphService, ILogger<GraphApiHealthCheck> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 Performing Graph API health check");
            
            // Test basic Graph API connectivity by checking permissions
            var permissionStatus = await _graphService.CheckUserManagementPermissionsAsync();
            
            var data = new Dictionary<string, object>
            {
                { "can_disable_users", permissionStatus.CanDisableUsers },
                { "can_delete_users", permissionStatus.CanDeleteUsers },
                { "can_manage_groups", permissionStatus.CanManageGroups },
                { "missing_permissions_count", permissionStatus.MissingPermissions.Count },
                { "error_count", permissionStatus.ErrorMessages.Count }
            };

            // Determine health status based on capabilities
            if (permissionStatus.ErrorMessages.Any())
            {
                var errors = string.Join("; ", permissionStatus.ErrorMessages);
                _logger.LogWarning("⚠️ Graph API health check has errors: {Errors}", errors);
                
                return HealthCheckResult.Degraded(
                    description: $"Graph API partially functional. Errors: {errors}",
                    data: data);
            }

            if (!permissionStatus.CanDisableUsers || !permissionStatus.CanManageGroups)
            {
                var missingPermissions = string.Join(", ", permissionStatus.MissingPermissions);
                _logger.LogWarning("⚠️ Graph API missing critical permissions: {Permissions}", missingPermissions);
                
                return HealthCheckResult.Degraded(
                    description: $"Graph API missing permissions: {missingPermissions}",
                    data: data);
            }

            _logger.LogDebug("✅ Graph API health check passed");
            return HealthCheckResult.Healthy("Graph API fully functional", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Graph API health check failed");
            
            return HealthCheckResult.Unhealthy(
                description: $"Graph API unavailable: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object> { { "error", ex.Message } });
        }
    }
}