using Polly;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing resilience policies for external service calls
/// </summary>
public interface IResilienceService
{
    /// <summary>
    /// Get retry policy for Graph API calls
    /// </summary>
    ResiliencePipeline GetGraphApiRetryPolicy();
    
    /// <summary>
    /// Get retry policy for Key Vault operations
    /// </summary>
    ResiliencePipeline GetKeyVaultRetryPolicy();
    
    /// <summary>
    /// Get retry policy for database operations
    /// </summary>
    ResiliencePipeline GetDatabaseRetryPolicy();
    
    /// <summary>
    /// Get circuit breaker policy for external services
    /// </summary>
    ResiliencePipeline GetCircuitBreakerPolicy(string serviceName);
    
    /// <summary>
    /// Execute an operation with retry policy
    /// </summary>
    Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, ResiliencePipeline policy);
    
    /// <summary>
    /// Execute an operation with retry policy (void return)
    /// </summary>
    Task ExecuteWithRetryAsync(Func<Task> operation, ResiliencePipeline policy);
}