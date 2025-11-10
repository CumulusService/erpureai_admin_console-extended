using Microsoft.AspNetCore.Authorization;
using AdminConsole.Models;
using AdminConsole.Services;
using AdminConsole.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdminConsole.Authorization;

/// <summary>
/// Authorization requirement for database-driven role checks
/// Replaces hardcoded domain checks with proper database role validation
/// </summary>
public class DatabaseRoleRequirement : IAuthorizationRequirement
{
    public UserRole RequiredRole { get; }
    public bool AllowHigherRoles { get; }

    public DatabaseRoleRequirement(UserRole requiredRole, bool allowHigherRoles = true)
    {
        RequiredRole = requiredRole;
        AllowHigherRoles = allowHigherRoles;
    }
}

/// <summary>
/// Authorization handler that checks user roles from database instead of hardcoded domain checks
/// </summary>
public class DatabaseRoleHandler : AuthorizationHandler<DatabaseRoleRequirement>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseRoleHandler> _logger;

    public DatabaseRoleHandler(IServiceProvider serviceProvider, ILogger<DatabaseRoleHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DatabaseRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogInformation("DatabaseRoleHandler: User not authenticated");
            return;
        }

        // Extract user email from claims
        var email = GetUserEmail(context.User);
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("DatabaseRoleHandler: No email found in user claims");
            return;
        }

        try
        {
            // Check for Azure AD app roles first (preferred method)
            if (CheckAzureAdAppRoles(context.User, requirement))
            {
                _logger.LogInformation("DatabaseRoleHandler: User {Email} authorized via Azure AD app roles", email);
                context.Succeed(requirement);
                return;
            }

            // Check database role
            var userRole = await GetUserRoleFromDatabaseAsync(email);
            if (userRole == null)
            {
                _logger.LogInformation("DatabaseRoleHandler: No database record found for user {Email}", email);
                return;
            }

            // Check if user has required role or higher
            if (HasRequiredRole(userRole.Value, requirement))
            {
                _logger.LogInformation("DatabaseRoleHandler: User {Email} authorized with role {Role}", email, userRole);
                context.Succeed(requirement);
                return;
            }

            _logger.LogInformation("DatabaseRoleHandler: User {Email} has role {Role} but requires {RequiredRole}", 
                email, userRole, requirement.RequiredRole);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseRoleHandler: Error checking role for user {Email}", email);
        }
    }

    private string? GetUserEmail(System.Security.Claims.ClaimsPrincipal user)
    {
        return user.FindFirst("email")?.Value ??
               user.FindFirst("preferred_username")?.Value ??
               user.FindFirst("upn")?.Value ??
               user.FindFirst("unique_name")?.Value ??
               user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
    }

    private bool CheckAzureAdAppRoles(System.Security.Claims.ClaimsPrincipal user, DatabaseRoleRequirement requirement)
    {
        switch (requirement.RequiredRole)
        {
            case UserRole.SuperAdmin:
                return user.IsInRole("SuperAdmin");
                
            case UserRole.OrgAdmin:
                return requirement.AllowHigherRoles
                    ? (user.IsInRole("SuperAdmin") || user.IsInRole("DevRole") || user.IsInRole("OrgAdmin"))
                    : user.IsInRole("OrgAdmin");

            case UserRole.User:
                return requirement.AllowHigherRoles
                    ? (user.IsInRole("SuperAdmin") || user.IsInRole("DevRole") || user.IsInRole("OrgAdmin") || user.IsInRole("OrgUser"))
                    : user.IsInRole("OrgUser");
                    
            case UserRole.Developer:
                return user.IsInRole("DevRole"); // Fixed: Match Azure portal app role name
                
            default:
                return false;
        }
    }

    private async Task<UserRole?> GetUserRoleFromDatabaseAsync(string email)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<AdminConsoleDbContext>();
            
            var user = await dbContext.OnboardedUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

            if (user == null)
            {
                return null;
            }

            // Use the extension method to get the role (handles both new and legacy systems)
            return user.GetUserRole();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying user role from database for {Email}", email);
            return null;
        }
    }

    private bool HasRequiredRole(UserRole userRole, DatabaseRoleRequirement requirement)
    {
        // Exact role match
        if (userRole == requirement.RequiredRole)
        {
            return true;
        }

        // Higher roles allowed check
        if (!requirement.AllowHigherRoles)
        {
            return false;
        }

        // Role hierarchy check (SuperAdmin = Developer > OrgAdmin > User)
        // Developer is considered equivalent to SuperAdmin (Master Users)
        return requirement.RequiredRole switch
        {
            UserRole.User => userRole == UserRole.OrgAdmin || userRole == UserRole.SuperAdmin || userRole == UserRole.Developer,
            UserRole.OrgAdmin => userRole == UserRole.SuperAdmin || userRole == UserRole.Developer,
            UserRole.SuperAdmin => userRole == UserRole.Developer, // Developer can access all SuperAdmin features
            UserRole.Developer => userRole == UserRole.SuperAdmin, // SuperAdmin can access dev features
            _ => false
        };
    }
}

/// <summary>
/// Extension methods for easily creating database role requirements
/// </summary>
public static class DatabaseRoleExtensions
{
    public static void RequireDatabaseRole(this AuthorizationPolicyBuilder builder, UserRole role, bool allowHigherRoles = true)
    {
        builder.Requirements.Add(new DatabaseRoleRequirement(role, allowHigherRoles));
    }
}