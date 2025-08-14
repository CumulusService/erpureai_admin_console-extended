using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing resilience policies for external service calls
/// Provides retry, circuit breaker, and timeout policies for improved reliability
/// </summary>
public class ResilienceService : IResilienceService
{
    private readonly ILogger<ResilienceService> _logger;
    
    // Pre-built policies for reuse
    private readonly ResiliencePipeline _graphApiPolicy;
    private readonly ResiliencePipeline _keyVaultPolicy;
    private readonly ResiliencePipeline _databasePolicy;
    private readonly Dictionary<string, ResiliencePipeline> _circuitBreakerPolicies;

    public ResilienceService(ILogger<ResilienceService> logger)
    {
        _logger = logger;
        _circuitBreakerPolicies = new Dictionary<string, ResiliencePipeline>();
        
        // Initialize pre-built policies
        _graphApiPolicy = CreateGraphApiPolicy();
        _keyVaultPolicy = CreateKeyVaultPolicy();
        _databasePolicy = CreateDatabasePolicy();
    }

    /// <summary>
    /// Get retry policy for Graph API calls
    /// 3 retries with exponential backoff, handling specific Graph API errors
    /// </summary>
    public ResiliencePipeline GetGraphApiRetryPolicy() => _graphApiPolicy;

    /// <summary>
    /// Get retry policy for Key Vault operations  
    /// 2 retries with linear backoff for Azure services
    /// </summary>
    public ResiliencePipeline GetKeyVaultRetryPolicy() => _keyVaultPolicy;

    /// <summary>
    /// Get retry policy for database operations
    /// 3 retries with exponential backoff for transient database errors
    /// </summary>
    public ResiliencePipeline GetDatabaseRetryPolicy() => _databasePolicy;

    /// <summary>
    /// Get circuit breaker policy for external services
    /// Creates service-specific circuit breakers with appropriate thresholds
    /// </summary>
    public ResiliencePipeline GetCircuitBreakerPolicy(string serviceName)
    {
        if (_circuitBreakerPolicies.TryGetValue(serviceName, out var existingPolicy))
        {
            return existingPolicy;
        }

        var policy = CreateCircuitBreakerPolicy(serviceName);
        _circuitBreakerPolicies[serviceName] = policy;
        return policy;
    }

    /// <summary>
    /// Execute an operation with retry policy
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, ResiliencePipeline policy)
    {
        return await policy.ExecuteAsync(async _ =>
        {
            return await operation();
        });
    }

    /// <summary>
    /// Execute an operation with retry policy (void return)
    /// </summary>
    public async Task ExecuteWithRetryAsync(Func<Task> operation, ResiliencePipeline policy)
    {
        await policy.ExecuteAsync(async _ =>
        {
            await operation();
        });
    }

    private ResiliencePipeline CreateGraphApiPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(30),
                OnRetry = args =>
                {
                    _logger.LogWarning("🔄 Graph API retry attempt {Attempt} after {Delay}ms. Exception: {Exception}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(45))  // Graph API timeout
            .Build();
    }

    private ResiliencePipeline CreateKeyVaultPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Linear,
                OnRetry = args =>
                {
                    _logger.LogWarning("🔐 Key Vault retry attempt {Attempt} after {Delay}ms. Exception: {Exception}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(30))  // Key Vault timeout
            .Build();
    }

    private ResiliencePipeline CreateDatabasePolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<System.Data.Common.DbException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(ex => ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(10),
                OnRetry = args =>
                {
                    _logger.LogWarning("🗄️ Database retry attempt {Attempt} after {Delay}ms. Exception: {Exception}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private ResiliencePipeline CreateCircuitBreakerPolicy(string serviceName)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                FailureRatio = 0.5,  // 50% failure ratio
                SamplingDuration = TimeSpan.FromSeconds(30),  // Sample window
                MinimumThroughput = 5,  // Minimum calls before opening
                BreakDuration = TimeSpan.FromMinutes(1),  // Stay open for 1 minute
                OnOpened = args =>
                {
                    _logger.LogError("⚡ Circuit breaker OPENED for service {ServiceName}. Exception: {Exception}",
                        serviceName, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("✅ Circuit breaker CLOSED for service {ServiceName}", serviceName);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("🔸 Circuit breaker HALF-OPEN for service {ServiceName}", serviceName);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}