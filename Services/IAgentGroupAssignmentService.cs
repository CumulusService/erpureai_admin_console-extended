using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing user assignments to agent-based security groups
/// This is purely additive to existing SecurityGroupService functionality
/// </summary>
public interface IAgentGroupAssignmentService
{
    /// <summary>
    /// Assigns a user to all security groups for specified agent types
    /// This works alongside existing organization-based groups
    /// </summary>
    Task<bool> AssignUserToAgentTypeGroupsAsync(string userId, List<Guid> agentTypeIds, Guid organizationId, string assignedBy);
    
    /// <summary>
    /// Removes a user from all agent-based security groups
    /// Used during user deletion - does not affect existing organization groups
    /// </summary>
    Task<bool> RemoveUserFromAgentTypeGroupsAsync(string userId, Guid organizationId);
    
    /// <summary>
    /// Gets all agent-based group assignments for a user
    /// </summary>
    Task<List<UserAgentTypeGroupAssignment>> GetUserAgentGroupAssignmentsAsync(string userId, Guid organizationId);
    
    /// <summary>
    /// Gets all users assigned to a specific agent type's security group
    /// </summary>
    Task<List<UserAgentTypeGroupAssignment>> GetUsersForAgentTypeAsync(Guid agentTypeId, Guid organizationId);
    
    /// <summary>
    /// Updates agent type assignments for a user (add/remove as needed)
    /// </summary>
    Task<bool> UpdateUserAgentTypeAssignmentsAsync(string userId, List<Guid> newAgentTypeIds, Guid organizationId, string modifiedBy);
    
    /// <summary>
    /// Reactivates all previous agent group assignments for a user (used during user restoration)
    /// </summary>
    Task<bool> ReactivateUserAgentGroupAssignmentsAsync(string userId, Guid organizationId);
    
    /// <summary>
    /// Deactivates all agent group assignments for a user (used during soft delete)
    /// </summary>
    Task<bool> DeactivateUserAgentGroupAssignmentsAsync(string userId, Guid organizationId);
    
    /// <summary>
    /// CRITICAL: Performs bidirectional sync between Azure AD and database for agent group assignments
    /// Ensures consistency between what's in Azure AD and what's tracked in the database
    /// </summary>
    Task<bool> SyncUserAgentGroupAssignmentsAsync(string userId, Guid organizationId);
    
    /// <summary>
    /// CRITICAL: Synchronizes agent group memberships for ALL users in an organization
    /// Used when organization-level agent types or global security group IDs change
    /// </summary>
    Task<bool> SyncOrganizationAgentGroupAssignmentsAsync(Guid organizationId, string modifiedBy);
    
    /// <summary>
    /// CRITICAL: Removes ALL users in an organization from a specific agent type's security group
    /// Used when SuperAdmin unassigns an agent type from organization level
    /// </summary>
    Task<bool> RemoveAllUsersFromAgentTypeAsync(Guid organizationId, Guid agentTypeId, string modifiedBy);
}