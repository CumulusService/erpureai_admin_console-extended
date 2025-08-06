using AdminConsole.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace AdminConsole.Services;

/// <summary>
/// Service responsible for enforcing organization data isolation and row-level security
/// Ensures users can only access data from their own organization
/// </summary>
public class DataIsolationService : IDataIsolationService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGraphService _graphService;
    private readonly ILogger<DataIsolationService> _logger;
    private readonly IMemoryCache _cache;

    public DataIsolationService(
        IHttpContextAccessor httpContextAccessor,
        IGraphService graphService,
        ILogger<DataIsolationService> logger,
        IMemoryCache cache)
    {
        _httpContextAccessor = httpContextAccessor;
        _graphService = graphService;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Gets the organization ID for the current authenticated user
    /// Uses caching for performance and validates access permissions
    /// </summary>
    public async Task<string?> GetCurrentUserOrganizationIdAsync()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("Attempted to get organization ID for unauthenticated user");
                return null;
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? 
                        user.FindFirst("oid")?.Value ?? 
                        user.FindFirst("sub")?.Value;
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? 
                       user.FindFirst("email")?.Value ?? 
                       user.FindFirst("preferred_username")?.Value ?? 
                       user.FindFirst("upn")?.Value;
            
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("User {UserId} missing required claims for organization lookup", userId);
                return null;
            }

            // Check cache first for performance
            var cacheKey = $"user_org_{userId}";
            if (_cache.TryGetValue(cacheKey, out string? cachedOrgId))
            {
                return cachedOrgId;
            }

            // Get user's organization from Graph service
            _logger.LogInformation("=== DataIsolationService.GetCurrentUserOrganizationIdAsync ===");
            _logger.LogInformation("  User email: {Email}", email);
            
            var currentUser = await _graphService.GetCurrentUserAsync();
            _logger.LogInformation("  GraphService.GetCurrentUserAsync returned: {HasUser}", currentUser != null);
            
            if (currentUser != null && !string.IsNullOrEmpty(currentUser.OrganizationId))
            {
                _logger.LogInformation("  Using organization ID from GraphService: {OrganizationId}", currentUser.OrganizationId);
                // Cache for 15 minutes to improve performance
                _cache.Set(cacheKey, currentUser.OrganizationId, TimeSpan.FromMinutes(15));
                return currentUser.OrganizationId;
            }

            // Fallback: extract from email domain
            var domain = email.Split('@').LastOrDefault();
            _logger.LogInformation("  Extracted domain from email: {Domain}", domain);
            
            if (!string.IsNullOrEmpty(domain))
            {
                var domainBasedOrgId = domain.Replace(".", "_").ToLowerInvariant();
                _logger.LogInformation("Using domain-based organization ID {OrganizationId} for user {Email}", 
                    domainBasedOrgId, email);
                
                // Cache the result
                _cache.Set(cacheKey, domainBasedOrgId, TimeSpan.FromMinutes(5));
                return domainBasedOrgId;
            }

            _logger.LogError("Could not determine organization ID for user {UserId} with email {Email}", userId, email);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user organization ID");
            return null;
        }
    }

    /// <summary>
    /// Validates that the current user has access to the specified organization
    /// Enforces row-level security by checking organization membership
    /// </summary>
    public async Task<bool> ValidateOrganizationAccessAsync(string organizationId)
    {
        try
        {
            if (string.IsNullOrEmpty(organizationId))
            {
                return false;
            }

            // Check if user is Super Admin (can access all organizations)
            if (IsCurrentUserSuperAdmin())
            {
                _logger.LogDebug("Super admin access granted to organization {OrganizationId}", organizationId);
                return true;
            }

            // Get current user's organization
            var userOrgId = await GetCurrentUserOrganizationIdAsync();
            if (string.IsNullOrEmpty(userOrgId))
            {
                _logger.LogWarning("User organization ID not found, denying access to organization {OrganizationId}", 
                    organizationId);
                return false;
            }

            // Check if user's organization matches the requested organization
            // Need to normalize both IDs to handle string vs GUID format differences
            var normalizedUserOrgId = NormalizeOrganizationId(userOrgId);
            var normalizedRequestedOrgId = NormalizeOrganizationId(organizationId);
            
            var hasAccess = string.Equals(normalizedUserOrgId, normalizedRequestedOrgId, StringComparison.OrdinalIgnoreCase);
            
            if (!hasAccess)
            {
                _logger.LogWarning("User from organization {UserOrgId} attempted to access organization {RequestedOrgId}", 
                    userOrgId, organizationId);
            }

            return hasAccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating organization access for organization {OrganizationId}", organizationId);
            return false;
        }
    }

    /// <summary>
    /// Checks if the current user is a Super Admin with cross-organization access
    /// </summary>
    public bool IsCurrentUserSuperAdmin()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? 
                       user.FindFirst("email")?.Value ?? 
                       user.FindFirst("preferred_username")?.Value ?? 
                       user.FindFirst("upn")?.Value;
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            // Check if user email belongs to the main domain (erpure.ai)
            var isErpureDomain = email.EndsWith("@erpure.ai", StringComparison.OrdinalIgnoreCase);
            
            // Check for Super Admin role claim
            var hasAdminRole = user.HasClaim("extension_UserRole", "SuperAdmin") ||
                              user.HasClaim("roles", "SuperAdmin") ||
                              user.IsInRole("SuperAdmin");

            var isSuperAdmin = isErpureDomain && hasAdminRole;
            
            if (isSuperAdmin)
            {
                _logger.LogInformation("Super admin access confirmed for user {Email}", email);
            }

            return isSuperAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking super admin status");
            return false;
        }
    }

    /// <summary>
    /// Filters a list of organizations to only include those the current user has access to
    /// Implements row-level security filtering at the service level
    /// </summary>
    public async Task<IEnumerable<T>> FilterByOrganizationAccessAsync<T>(IEnumerable<T> items, Func<T, string> getOrganizationId)
    {
        try
        {
            // Super admins can see all data
            if (IsCurrentUserSuperAdmin())
            {
                return items;
            }

            var userOrgId = await GetCurrentUserOrganizationIdAsync();
            if (string.IsNullOrEmpty(userOrgId))
            {
                _logger.LogWarning("User organization ID not found, returning empty results");
                return Enumerable.Empty<T>();
            }

            // Filter items to only include those from user's organization
            var filteredItems = items.Where(item => 
            {
                var itemOrgId = getOrganizationId(item);
                return string.Equals(itemOrgId, userOrgId, StringComparison.OrdinalIgnoreCase);
            });

            return filteredItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering items by organization access");
            return Enumerable.Empty<T>();
        }
    }

    /// <summary>
    /// Gets the current user's role within their organization
    /// </summary>
    public UserRole GetCurrentUserRole()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return UserRole.User;
            }

            // Check for Azure AD app roles first (preferred method)
            if (user.IsInRole("SuperAdmin"))
            {
                _logger.LogDebug("User has SuperAdmin app role");
                return UserRole.SuperAdmin;
            }
            if (user.IsInRole("OrgAdmin"))
            {
                _logger.LogDebug("User has OrgAdmin app role");
                return UserRole.OrgAdmin;
            }
            
            // Fallback: Check email domain for SuperAdmin access
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? 
                       user.FindFirst("email")?.Value ?? 
                       user.FindFirst("preferred_username")?.Value ?? 
                       user.FindFirst("upn")?.Value;
            
            if (!string.IsNullOrEmpty(email) && email.EndsWith("@erpure.ai", StringComparison.OrdinalIgnoreCase))
            {
                return UserRole.SuperAdmin;
            }
            
            // Legacy claim checks (for backward compatibility)
            if (user.HasClaim("extension_UserRole", "SuperAdmin"))
            {
                return UserRole.SuperAdmin;
            }
            if (user.HasClaim("extension_UserRole", "OrgAdmin") || user.HasClaim("extension_IsAdmin", "true"))
            {
                return UserRole.OrgAdmin;
            }

            return UserRole.User;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user role");
            return UserRole.User;
        }
    }

    /// <summary>
    /// Validates that the current user has the required role for an operation
    /// </summary>
    public bool ValidateUserRole(UserRole requiredRole)
    {
        var currentRole = GetCurrentUserRole();
        
        // Role hierarchy: SuperAdmin > OrgAdmin > User
        switch (requiredRole)
        {
            case UserRole.SuperAdmin:
                return currentRole == UserRole.SuperAdmin;
            
            case UserRole.OrgAdmin:
                return currentRole == UserRole.SuperAdmin || currentRole == UserRole.OrgAdmin;
            
            case UserRole.User:
                return true; // All authenticated users have User level access
            
            default:
                return false;
        }
    }

    /// <summary>
    /// Clears organization cache for the current user
    /// Should be called when user's organization membership changes
    /// </summary>
    public void ClearUserOrganizationCache()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    var cacheKey = $"user_org_{userId}";
                    _cache.Remove(cacheKey);
                    _logger.LogDebug("Cleared organization cache for user {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing user organization cache");
        }
    }

    /// <summary>
    /// Normalizes organization ID to GUID format for consistent comparison
    /// Handles both string format (domain_based) and GUID format
    /// </summary>
    private string NormalizeOrganizationId(string organizationId)
    {
        if (string.IsNullOrEmpty(organizationId))
        {
            return string.Empty;
        }

        // If it's already a valid GUID, return as-is
        if (Guid.TryParse(organizationId, out var existingGuid))
        {
            return existingGuid.ToString().ToLowerInvariant();
        }

        // If it's a domain-based string, convert to deterministic GUID
        // This uses the same logic as OrganizationService.GetByIdAsync
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(organizationId));
        var generatedGuid = new Guid(hash);
        
        return generatedGuid.ToString().ToLowerInvariant();
    }
}