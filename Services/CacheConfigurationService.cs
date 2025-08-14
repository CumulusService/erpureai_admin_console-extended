using Microsoft.Extensions.Caching.Memory;

namespace AdminConsole.Services;

/// <summary>
/// Centralized cache configuration service for consistent cache management
/// </summary>
public class CacheConfigurationService : ICacheConfigurationService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheConfigurationService> _logger;

    public CacheConfigurationService(IMemoryCache cache, ILogger<CacheConfigurationService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Cache TTL for dynamic data that changes frequently (5 minutes)
    /// Used for: user assignments, database credentials, real-time data
    /// </summary>
    public TimeSpan DynamicCacheTTL => TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Cache TTL for semi-static data (15 minutes)  
    /// Used for: organization data, agent types, key vault data
    /// </summary>
    public TimeSpan StaticCacheTTL => TimeSpan.FromMinutes(15);
    
    /// <summary>
    /// Cache TTL for user session data (30 minutes)
    /// Used for: user roles, permissions, session state
    /// </summary>
    public TimeSpan UserSessionCacheTTL => TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// Cache TTL for configuration data (1 hour)
    /// Used for: agent types, system configuration
    /// </summary>
    public TimeSpan ConfigurationCacheTTL => TimeSpan.FromHours(1);
    
    /// <summary>
    /// Generate a standardized cache key with organization isolation
    /// Format: {category}:{organizationId}:{identifier}
    /// </summary>
    public string GenerateCacheKey(string category, string identifier, string? organizationId = null)
    {
        var key = organizationId != null 
            ? $"{category}:{organizationId}:{identifier}" 
            : $"{category}:global:{identifier}";
        
        _logger.LogDebug("Generated cache key: {CacheKey}", key);
        return key;
    }
    
    /// <summary>
    /// Generate a user-specific cache key
    /// Format: user:{category}:{organizationId}:{userId}
    /// </summary>
    public string GenerateUserCacheKey(string category, string userId, string? organizationId = null)
    {
        var key = organizationId != null 
            ? $"user:{category}:{organizationId}:{userId}" 
            : $"user:{category}:global:{userId}";
        
        _logger.LogDebug("Generated user cache key: {CacheKey}", key);
        return key;
    }
    
    /// <summary>
    /// Invalidate all cache entries for a specific organization
    /// Note: MemoryCache doesn't support pattern invalidation, so this logs for future implementation
    /// </summary>
    public async Task InvalidateOrganizationCacheAsync(string organizationId)
    {
        _logger.LogInformation("🔄 Cache invalidation requested for organization: {OrganizationId}", organizationId);
        
        // Note: In-memory cache doesn't support pattern-based invalidation
        // For production, consider using Redis or implementing a custom cache key tracking system
        // For now, we'll log the invalidation request for monitoring
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Invalidate cache entries by pattern
    /// Note: MemoryCache doesn't support pattern invalidation, so this logs for future implementation
    /// </summary>
    public async Task InvalidateCachePatternAsync(string pattern)
    {
        _logger.LogInformation("🔄 Cache pattern invalidation requested: {Pattern}", pattern);
        
        // Note: In-memory cache doesn't support pattern-based invalidation
        // For production, consider using Redis or implementing a custom cache key tracking system
        // For now, we'll log the invalidation request for monitoring
        
        await Task.CompletedTask;
    }
}