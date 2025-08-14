using AdminConsole.Services;
using System.Security.Claims;

namespace AdminConsole.Middleware;

public class UserAccessValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserAccessValidationMiddleware> _logger;

    public UserAccessValidationMiddleware(RequestDelegate next, ILogger<UserAccessValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserAccessValidationService accessValidationService)
    {
        // Skip validation for certain paths
        if (ShouldSkipValidation(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Only validate for authenticated users
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = GetUserId(context.User);
            var email = GetUserEmail(context.User);

            if (!string.IsNullOrEmpty(userId) || !string.IsNullOrEmpty(email))
            {
                var accessResult = await accessValidationService.ValidateUserAccessAsync(userId ?? string.Empty, email ?? string.Empty);

                if (!accessResult.HasAccess)
                {
                    _logger.LogWarning("Access denied for user {UserId} ({Email}): {Reason}", 
                        userId, email, accessResult.Reason);

                    // Redirect to access denied page with reason
                    var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                    var accessDeniedUrl = $"/Account/AccessDenied?reason={Uri.EscapeDataString(accessResult.Reason)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
                    
                    context.Response.Redirect(accessDeniedUrl);
                    return;
                }
            }
        }

        await _next(context);
    }

    private static bool ShouldSkipValidation(PathString path)
    {
        var pathValue = path.Value?.ToLowerInvariant();
        
        // Skip validation for these paths
        var skipPaths = new[]
        {
            "/account/signin",
            "/account/signout", 
            "/account/accessdenied",
            "/signin-oidc",
            "/signout-callback-oidc",
            "/.well-known",
            "/css",
            "/js", 
            "/lib",
            "/favicon.ico",
            "/images",
            "/fonts",
            "/_framework",
            "/_blazor",
            "/error"
        };

        return skipPaths.Any(skipPath => pathValue?.StartsWith(skipPath) == true);
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst("oid")?.Value ?? // Object identifier (Azure AD)
               user.FindFirst("sub")?.Value ?? // Subject (standard claim) 
               user.FindFirst(ClaimTypes.NameIdentifier)?.Value; // Name identifier
    }

    private static string? GetUserEmail(ClaimsPrincipal user)
    {
        return user.FindFirst("email")?.Value ?? 
               user.FindFirst("preferred_username")?.Value ?? 
               user.FindFirst("upn")?.Value ?? 
               user.FindFirst("unique_name")?.Value ??
               user.FindFirst(ClaimTypes.Email)?.Value ??
               user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
    }
}