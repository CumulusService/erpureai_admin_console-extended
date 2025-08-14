using Microsoft.Extensions.Diagnostics.HealthChecks;
using AdminConsole.Services;

namespace AdminConsole.HealthChecks;

/// <summary>
/// Health check for Azure Key Vault connectivity
/// </summary>
public class KeyVaultHealthCheck : IHealthCheck
{
    private readonly IKeyVaultService _keyVaultService;
    private readonly ILogger<KeyVaultHealthCheck> _logger;

    public KeyVaultHealthCheck(IKeyVaultService keyVaultService, ILogger<KeyVaultHealthCheck> logger)
    {
        _keyVaultService = keyVaultService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 Performing Key Vault health check");
            
            // Test Key Vault connectivity
            var (success, message) = await _keyVaultService.TestConnectivityAsync();
            
            var data = new Dictionary<string, object>
            {
                { "connectivity_test", success },
                { "test_message", message ?? "No message" },
                { "timestamp", DateTime.UtcNow }
            };

            if (success)
            {
                _logger.LogDebug("✅ Key Vault health check passed");
                return HealthCheckResult.Healthy("Key Vault accessible", data);
            }
            else
            {
                _logger.LogWarning("⚠️ Key Vault health check failed: {Message}", message);
                return HealthCheckResult.Unhealthy($"Key Vault connectivity issue: {message}", data: data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Key Vault health check failed with exception");
            
            return HealthCheckResult.Unhealthy(
                description: $"Key Vault health check failed: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object> 
                { 
                    { "error", ex.Message },
                    { "error_type", ex.GetType().Name }
                });
        }
    }
}