using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Interface for Azure AD Security Group management per organization
/// Provides enterprise-scale multi-tenant user isolation through security groups
/// </summary>
public interface ISecurityGroupService
{
    /// <summary>
    /// Creates Azure AD Security Group for a new organization
    /// Group name format: "Partner-{domain}-Users"
    /// </summary>
    /// <param name="organization">Organization to create security group for</param>
    /// <returns>Group ID if successful, null if failed</returns>
    Task<string?> CreateOrganizationSecurityGroupAsync(Organization organization);

    /// <summary>
    /// Adds user to organization's security group after successful invitation
    /// </summary>
    /// <param name="userId">Azure AD user ID</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if successful</returns>
    Task<bool> AddUserToOrganizationGroupAsync(string userId, string organizationId);

    /// <summary>
    /// Removes user from organization's security group
    /// </summary>
    /// <param name="userId">Azure AD user ID</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if successful</returns>
    Task<bool> RemoveUserFromOrganizationGroupAsync(string userId, string organizationId);

    /// <summary>
    /// Gets all members of organization's security group
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>List of user IDs in the group</returns>
    Task<List<string>> GetOrganizationGroupMembersAsync(string organizationId);

    /// <summary>
    /// Deletes organization's security group (use with caution)
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteOrganizationSecurityGroupAsync(string organizationId);

    /// <summary>
    /// Ensures organization has proper security group setup
    /// Called during organization setup or maintenance operations
    /// </summary>
    /// <param name="organization">Organization to ensure security group for</param>
    /// <returns>True if group exists or was created successfully</returns>
    Task<bool> EnsureOrganizationSecurityGroupAsync(Organization organization);

    /// <summary>
    /// Ensures organization has proper security group setup using organization ID
    /// Avoids EF Core entity tracking conflicts by loading organization fresh
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if group exists or was created successfully</returns>
    Task<bool> EnsureOrganizationSecurityGroupByIdAsync(Guid organizationId);
}