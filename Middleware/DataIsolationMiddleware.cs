using AdminConsole.Services;
using System.Security.Claims;

namespace AdminConsole.Middleware;

/// <summary>
/// Middleware that enforces data isolation by validating organization access on every request
/// Provides automatic row-level security for multi-tenant B2B platform
/// </summary>
public class DataIsolationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DataIsolationMiddleware> _logger;

    public DataIsolationMiddleware(RequestDelegate next, ILogger<DataIsolationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDataIsolationService dataIsolationService)
    {
        try
        {
            // Skip data isolation for authentication and public endpoints
            if (ShouldSkipDataIsolation(context))
            {
                await _next(context);
                return;
            }

            // Only apply data isolation to authenticated users
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                // Set organization context for the request
                await SetOrganizationContextAsync(context, dataIsolationService);
                
                // Validate organization access for specific API routes
                if (!await ValidateRouteAccessAsync(context, dataIsolationService))
                {
                    _logger.LogWarning("Data isolation violation: User {UserId} attempted to access restricted route {Path}", 
                        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, context.Request.Path);
                    
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Access denied: Organization isolation violation");
                    return;
                }
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in data isolation middleware");
            await _next(context);
        }
    }

    /// <summary>
    /// Determines if data isolation should be skipped for this request
    /// </summary>
    private static bool ShouldSkipDataIsolation(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        
        // Skip for authentication endpoints
        if (path.StartsWith("/signin") || 
            path.StartsWith("/signout") || 
            path.StartsWith("/.auth") ||
            path.StartsWith("/account"))
        {
            return true;
        }

        // Skip for static assets
        if (path.StartsWith("/_framework") ||
            path.StartsWith("/_content") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/favicon") ||
            path.Contains("."))
        {
            return true;
        }

        // Skip for health checks and monitoring
        if (path.StartsWith("/health") || path.StartsWith("/metrics"))
        {
            return true;
        }

        // Skip for home page and public pages
        if (path == "/" || path == "/home" || path == "/error")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sets organization context in HttpContext for use by downstream services
    /// </summary>
    private async Task SetOrganizationContextAsync(HttpContext context, IDataIsolationService dataIsolationService)
    {
        try
        {
            var organizationId = await dataIsolationService.GetCurrentUserOrganizationIdAsync();
            var userRole = dataIsolationService.GetCurrentUserRole();
            var isSuperAdmin = dataIsolationService.IsCurrentUserSuperAdmin();

            // Store in HttpContext.Items for access by controllers and services
            context.Items["CurrentUserOrganizationId"] = organizationId;
            context.Items["CurrentUserRole"] = userRole;
            context.Items["IsSuperAdmin"] = isSuperAdmin;

            _logger.LogDebug("Set organization context: OrgId={OrganizationId}, Role={Role}, SuperAdmin={IsSuperAdmin}", 
                organizationId, userRole, isSuperAdmin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting organization context");
        }
    }

    /// <summary>
    /// Validates organization access for specific API routes that include organization IDs
    /// </summary>
    private async Task<bool> ValidateRouteAccessAsync(HttpContext context, IDataIsolationService dataIsolationService)
    {
        try
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
            
            // Extract organization ID from route parameters
            string? organizationIdFromRoute = null;
            
            // Check for organization ID in route values
            if (context.Request.RouteValues.TryGetValue("organizationId", out var orgIdValue))
            {
                organizationIdFromRoute = orgIdValue?.ToString();
            }
            
            // Check for organization ID in query parameters
            if (string.IsNullOrEmpty(organizationIdFromRoute))
            {
                organizationIdFromRoute = context.Request.Query["organizationId"].FirstOrDefault();
            }

            // If no organization ID in route, allow the request (will be filtered at service level)
            if (string.IsNullOrEmpty(organizationIdFromRoute))
            {
                return true;
            }

            // Validate access to the specified organization
            var hasAccess = await dataIsolationService.ValidateOrganizationAccessAsync(organizationIdFromRoute);
            
            if (!hasAccess)
            {
                _logger.LogWarning("User attempted to access organization {OrganizationId} without permission", 
                    organizationIdFromRoute);
            }

            return hasAccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating route access");
            // On error, allow the request to proceed (service-level filtering will still apply)
            return true;
        }
    }
}

/// <summary>
/// Extension methods for registering the data isolation middleware
/// </summary>
public static class DataIsolationMiddlewareExtensions
{
    /// <summary>
    /// Adds data isolation middleware to the application pipeline
    /// Should be called after authentication but before authorization
    /// </summary>
    public static IApplicationBuilder UseDataIsolation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DataIsolationMiddleware>();
    }
}