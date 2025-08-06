using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing Microsoft Teams groups per organization
/// Handles Teams group creation, user management, and integration with security groups
/// </summary>
public interface ITeamsGroupService
{
    /// <summary>
    /// Creates or ensures a Teams group exists for an organization
    /// This is called when an org admin is first invited
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <param name="createdBy">User ID who is creating the group</param>
    /// <returns>Created or existing Teams group</returns>
    Task<OrganizationTeamsGroup?> CreateOrganizationTeamsGroupAsync(Guid organizationId, Guid createdBy);

    /// <summary>
    /// Adds a user to the organization's Teams group
    /// </summary>
    /// <param name="userId">User ID to add</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if added successfully</returns>
    Task<bool> AddUserToOrganizationTeamsGroupAsync(string userId, Guid organizationId);

    /// <summary>
    /// Removes a user from the organization's Teams group
    /// </summary>
    /// <param name="userId">User ID to remove</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if removed successfully</returns>
    Task<bool> RemoveUserFromOrganizationTeamsGroupAsync(string userId, Guid organizationId);

    /// <summary>
    /// Gets the Teams group for an organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>Teams group if found</returns>
    Task<OrganizationTeamsGroup?> GetOrganizationTeamsGroupAsync(Guid organizationId);

    /// <summary>
    /// Gets all members of an organization's Teams group
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>List of user IDs in the Teams group</returns>
    Task<List<string>> GetOrganizationTeamsGroupMembersAsync(Guid organizationId);

    /// <summary>
    /// Ensures organization has a Teams group setup
    /// Called during organization setup or maintenance operations
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <param name="createdBy">User ID who is creating the group</param>
    /// <returns>True if Teams group exists or was created successfully</returns>
    Task<bool> EnsureOrganizationTeamsGroupAsync(Guid organizationId, Guid createdBy);

    /// <summary>
    /// Deletes organization's Teams group (use with caution)
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteOrganizationTeamsGroupAsync(Guid organizationId);
}