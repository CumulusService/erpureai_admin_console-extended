namespace AdminConsole.Services;

/// <summary>
/// Service for standardized cache configuration across the application
/// </summary>
public interface ICacheConfigurationService
{
    /// <summary>
    /// Cache TTL for dynamic data that changes frequently (5 minutes)
    /// </summary>
    TimeSpan DynamicCacheTTL { get; }
    
    /// <summary>
    /// Cache TTL for semi-static data (15 minutes)
    /// </summary>
    TimeSpan StaticCacheTTL { get; }
    
    /// <summary>
    /// Cache TTL for user session data (30 minutes)
    /// </summary>
    TimeSpan UserSessionCacheTTL { get; }
    
    /// <summary>
    /// Cache TTL for configuration data (1 hour)
    /// </summary>
    TimeSpan ConfigurationCacheTTL { get; }
    
    /// <summary>
    /// Generate a standardized cache key with organization isolation
    /// </summary>
    string GenerateCacheKey(string category, string identifier, string? organizationId = null);
    
    /// <summary>
    /// Generate a user-specific cache key
    /// </summary>
    string GenerateUserCacheKey(string category, string userId, string? organizationId = null);
    
    /// <summary>
    /// Invalidate all cache entries for a specific organization
    /// </summary>
    Task InvalidateOrganizationCacheAsync(string organizationId);
    
    /// <summary>
    /// Invalidate cache entries by pattern
    /// </summary>
    Task InvalidateCachePatternAsync(string pattern);
}