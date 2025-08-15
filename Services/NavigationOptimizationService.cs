using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AdminConsole.Services;

public interface INavigationOptimizationService
{
    Task PreloadCommonDataAsync();
    T? GetCachedData<T>(string cacheKey) where T : class;
    void SetCachedData<T>(string cacheKey, T data, TimeSpan? expiration = null) where T : class;
    Task PreloadPageDataAsync(string pagePath);
}

public class NavigationOptimizationService : INavigationOptimizationService
{
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationOptimizationService> _logger;
    private readonly Dictionary<string, Task> _preloadTasks = new();

    // Cache keys for common data
    public const string AGENT_TYPES_CACHE_KEY = "nav_agent_types";
    public const string USER_ROLES_CACHE_KEY = "nav_user_roles";
    public const string ORGANIZATIONS_CACHE_KEY = "nav_organizations";

    public NavigationOptimizationService(
        IMemoryCache cache,
        IServiceProvider serviceProvider,
        ILogger<NavigationOptimizationService> logger)
    {
        _cache = cache;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PreloadCommonDataAsync()
    {
        try
        {
            _logger.LogInformation("🚀 Preloading common navigation data...");

            // Start all preload tasks in parallel
            var tasks = new List<Task>
            {
                PreloadAgentTypesAsync(),
                PreloadOrganizationsAsync()
            };

            await Task.WhenAll(tasks);
            _logger.LogInformation("✅ Navigation data preloading completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Navigation data preloading failed, will load on-demand");
        }
    }

    public T? GetCachedData<T>(string cacheKey) where T : class
    {
        return _cache.Get<T>(cacheKey);
    }

    public void SetCachedData<T>(string cacheKey, T data, TimeSpan? expiration = null) where T : class
    {
        _cache.Set(cacheKey, data, expiration ?? TimeSpan.FromMinutes(5));
    }

    public async Task PreloadPageDataAsync(string pagePath)
    {
        // Prevent duplicate preloading
        if (_preloadTasks.ContainsKey(pagePath))
        {
            return;
        }

        var preloadTask = pagePath.ToLowerInvariant() switch
        {
            "/developer/dashboard" or "/developer" => PreloadDeveloperDashboardAsync(),
            "/admin/dashboard" => PreloadAdminDashboardAsync(),
            "/owner/dashboard" => PreloadOwnerDashboardAsync(),
            _ => Task.CompletedTask
        };

        _preloadTasks[pagePath] = preloadTask;
        await preloadTask;
    }

    private async Task PreloadAgentTypesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var agentTypeService = scope.ServiceProvider.GetRequiredService<IAgentTypeService>();
            
            var agentTypes = await agentTypeService.GetAllAgentTypesAsync();
            SetCachedData(AGENT_TYPES_CACHE_KEY, agentTypes, TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preload agent types");
        }
    }

    private async Task PreloadOrganizationsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var organizationService = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            
            var organizations = await organizationService.GetAllOrganizationsAsync();
            SetCachedData(ORGANIZATIONS_CACHE_KEY, organizations, TimeSpan.FromMinutes(15));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preload organizations");
        }
    }

    private async Task PreloadDeveloperDashboardAsync()
    {
        // Preload specific data for developer dashboard
        await PreloadAgentTypesAsync();
    }

    private async Task PreloadAdminDashboardAsync()
    {
        // Preload specific data for admin dashboard
        await Task.CompletedTask; // Placeholder for admin-specific data
    }

    private async Task PreloadOwnerDashboardAsync()
    {
        // Preload specific data for owner dashboard
        await PreloadOrganizationsAsync();
    }
}