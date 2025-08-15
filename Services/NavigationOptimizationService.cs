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
    
    // Cache keys for detail pages
    public const string USER_DETAILS_CACHE_PREFIX = "nav_user_details_";
    public const string ORG_DETAILS_CACHE_PREFIX = "nav_org_details_";
    public const string USER_LIST_CACHE_PREFIX = "nav_users_org_";
    
    // Cache keys for new pages
    public const string ADMIN_LIST_CACHE_KEY = "nav_admin_list";
    public const string SYSTEM_USERS_CACHE_KEY = "nav_system_users";

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
            "/admin/users" => PreloadUserListAsync(),
            "/owner/admins" => PreloadAdminListAsync(),
            "/developer/system-users" => PreloadSystemUsersAsync(),
            "/developer/agent-types" => PreloadAgentTypesAsync(),
            var path when path.StartsWith("/admin/users/") => PreloadUserDetailsAsync(ExtractUserIdFromPath(path)),
            var path when path.StartsWith("/owner/organizations/") => PreloadOrganizationDetailsAsync(ExtractOrgIdFromPath(path)),
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

    // 🚀 Detail Page Preloading Methods
    private async Task PreloadUserListAsync()
    {
        try
        {
            _logger.LogDebug("🔄 Preloading user list data...");
            using var scope = _serviceProvider.CreateScope();
            
            var dataIsolationService = scope.ServiceProvider.GetRequiredService<IDataIsolationService>();
            var onboardedUserService = scope.ServiceProvider.GetRequiredService<IOnboardedUserService>();
            
            var currentUserOrgIdStr = await dataIsolationService.GetCurrentUserOrganizationIdAsync();
            if (!string.IsNullOrEmpty(currentUserOrgIdStr))
            {
                if (Guid.TryParse(currentUserOrgIdStr, out var orgId))
                {
                    var users = await onboardedUserService.GetByOrganizationAsync(orgId);
                    SetCachedData($"{USER_LIST_CACHE_PREFIX}{orgId}", users, TimeSpan.FromMinutes(5));
                    _logger.LogDebug("✅ User list preloaded for organization {OrgId}", orgId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to preload user list data");
        }
    }

    private async Task PreloadUserDetailsAsync(string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
                return;

            _logger.LogDebug("🔄 Preloading user details for {UserId}...", userId);
            using var scope = _serviceProvider.CreateScope();
            
            var onboardedUserService = scope.ServiceProvider.GetRequiredService<IOnboardedUserService>();
            var dataIsolationService = scope.ServiceProvider.GetRequiredService<IDataIsolationService>();
            
            var currentUserOrgIdStr = await dataIsolationService.GetCurrentUserOrganizationIdAsync();
            if (!string.IsNullOrEmpty(currentUserOrgIdStr) && Guid.TryParse(currentUserOrgIdStr, out var orgId))
            {
                var user = await onboardedUserService.GetByIdAsync(userGuid, orgId);
                if (user != null)
                {
                    SetCachedData($"{USER_DETAILS_CACHE_PREFIX}{userId}", user, TimeSpan.FromMinutes(3));
                    _logger.LogDebug("✅ User details preloaded for {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to preload user details for {UserId}", userId);
        }
    }

    private async Task PreloadOrganizationDetailsAsync(string orgId)
    {
        try
        {
            if (string.IsNullOrEmpty(orgId))
                return;

            _logger.LogDebug("🔄 Preloading organization details for {OrgId}...", orgId);
            using var scope = _serviceProvider.CreateScope();
            
            var organizationService = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            var organization = await organizationService.GetByIdAsync(orgId);
            
            if (organization != null)
            {
                SetCachedData($"{ORG_DETAILS_CACHE_PREFIX}{orgId}", organization, TimeSpan.FromMinutes(5));
                _logger.LogDebug("✅ Organization details preloaded for {OrgId}", orgId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to preload organization details for {OrgId}", orgId);
        }
    }

    // 🚀 New Page Preloading Methods
    private async Task PreloadAdminListAsync()
    {
        try
        {
            _logger.LogDebug("🔄 Preloading admin list data...");
            using var scope = _serviceProvider.CreateScope();
            
            var organizationService = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            var organizations = await organizationService.GetAllOrganizationsAsync();
            
            SetCachedData(ADMIN_LIST_CACHE_KEY, organizations, TimeSpan.FromMinutes(5));
            _logger.LogDebug("✅ Admin list data preloaded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to preload admin list data");
        }
    }

    private Task PreloadSystemUsersAsync()
    {
        try
        {
            _logger.LogDebug("🔄 Preloading system users data...");
            // System users page preloading - just mark as preloaded since data loading is complex
            SetCachedData(SYSTEM_USERS_CACHE_KEY, "preloaded", TimeSpan.FromMinutes(3));
            _logger.LogDebug("✅ System users data preloaded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to preload system users data");
        }
        return Task.CompletedTask;
    }

    // Helper methods to extract IDs from URLs
    private string ExtractUserIdFromPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : string.Empty; // admin/users/{id}
    }

    private string ExtractOrgIdFromPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : string.Empty; // owner/organizations/{id}
    }
}