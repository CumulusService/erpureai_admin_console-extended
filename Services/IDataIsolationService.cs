using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Interface for enforcing organization data isolation and row-level security
/// Ensures users can only access data from their own organization in a multi-tenant B2B platform
/// </summary>
public interface IDataIsolationService
{
    /// <summary>
    /// Gets the organization ID for the current authenticated user
    /// Uses caching for performance and validates access permissions
    /// </summary>
    /// <returns>Organization ID if found, null if not authenticated or not found</returns>
    Task<string?> GetCurrentUserOrganizationIdAsync();

    /// <summary>
    /// Validates that the current user has access to the specified organization
    /// Enforces row-level security by checking organization membership
    /// </summary>
    /// <param name="organizationId">Organization ID to validate access for</param>
    /// <returns>True if user has access, false otherwise</returns>
    Task<bool> ValidateOrganizationAccessAsync(string organizationId);

    /// <summary>
    /// Checks if the current user is a Super Admin with cross-organization access
    /// </summary>
    /// <returns>True if user is a Super Admin</returns>
    bool IsCurrentUserSuperAdmin();
    
    /// <summary>
    /// Checks if the current user is a Super Admin with cross-organization access (async version)
    /// </summary>
    /// <returns>True if user is a Super Admin</returns>
    Task<bool> IsCurrentUserSuperAdminAsync();

    /// <summary>
    /// Filters a list of items to only include those the current user has access to
    /// Implements row-level security filtering at the service level
    /// </summary>
    /// <typeparam name="T">Type of items to filter</typeparam>
    /// <param name="items">Items to filter</param>
    /// <param name="getOrganizationId">Function to extract organization ID from each item</param>
    /// <returns>Filtered items based on user's organization access</returns>
    Task<IEnumerable<T>> FilterByOrganizationAccessAsync<T>(IEnumerable<T> items, Func<T, string> getOrganizationId);

    /// <summary>
    /// Gets the current user's role within their organization
    /// </summary>
    /// <returns>User's role</returns>
    UserRole GetCurrentUserRole();
    
    /// <summary>
    /// Gets the current user's role within their organization (async version)
    /// </summary>
    /// <returns>User's role</returns>
    Task<UserRole> GetCurrentUserRoleAsync();

    /// <summary>
    /// Validates that the current user has the required role for an operation
    /// </summary>
    /// <param name="requiredRole">Minimum required role</param>
    /// <returns>True if user has sufficient role</returns>
    bool ValidateUserRole(UserRole requiredRole);
    
    /// <summary>
    /// Validates that the current user has the required role for an operation (async version)
    /// </summary>
    /// <param name="requiredRole">Minimum required role</param>
    /// <returns>True if user has sufficient role</returns>
    Task<bool> ValidateUserRoleAsync(UserRole requiredRole);

    /// <summary>
    /// Clears organization cache for the current user
    /// Should be called when user's organization membership changes
    /// </summary>
    void ClearUserOrganizationCache();
}