using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing onboarded users in the Dataverse
/// Handles CRUD operations and user management functionality
/// </summary>
public interface IOnboardedUserService
{
    /// <summary>
    /// Gets all users for a specific organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>List of users in the organization</returns>
    Task<List<OnboardedUser>> GetByOrganizationAsync(Guid organizationId);
    
    /// <summary>
    /// Gets a specific user by ID
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>User if found and belongs to organization</returns>
    Task<OnboardedUser?> GetByIdAsync(Guid userId, Guid organizationId);
    
    /// <summary>
    /// Gets a user by email address within an organization
    /// </summary>
    /// <param name="email">User email</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>User if found</returns>
    Task<OnboardedUser?> GetByEmailAsync(string email, Guid organizationId);
    
    /// <summary>
    /// Gets a user by their Azure AD Object ID - more reliable than email lookup
    /// </summary>
    /// <param name="azureObjectId">Azure AD Object ID</param>
    /// <returns>User if found</returns>
    Task<OnboardedUser?> GetByAzureObjectIdAsync(string azureObjectId);
    
    /// <summary>
    /// Finds a user by email, then returns their Azure Object ID for reliable Azure AD operations
    /// </summary>
    /// <param name="email">User email</param>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>Azure Object ID if found</returns>
    Task<string?> GetAzureObjectIdByEmailAsync(string email, Guid organizationId);
    
    /// <summary>
    /// Creates a new onboarded user
    /// </summary>
    /// <param name="user">User to create</param>
    /// <param name="createdBy">User ID who is creating this user</param>
    /// <returns>Created user</returns>
    Task<OnboardedUser> CreateAsync(OnboardedUser user, Guid createdBy);
    
    /// <summary>
    /// Updates an existing user
    /// </summary>
    /// <param name="user">User with updated information</param>
    /// <param name="modifiedBy">User ID who is updating this user</param>
    /// <returns>Updated user</returns>
    Task<OnboardedUser> UpdateAsync(OnboardedUser user, Guid modifiedBy);
    
    /// <summary>
    /// Deactivates a user (soft delete)
    /// </summary>
    /// <param name="userId">User ID to deactivate</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is deactivating this user</param>
    /// <returns>True if deactivated successfully</returns>
    Task<bool> DeactivateAsync(Guid userId, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Reactivates a deactivated user
    /// </summary>
    /// <param name="userId">User ID to reactivate</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is reactivating this user</param>
    /// <returns>True if reactivated successfully</returns>
    Task<bool> ReactivateAsync(Guid userId, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Updates user's agent types (legacy method for backward compatibility)
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="agentTypes">New agent types</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is updating agent types</param>
    /// <returns>True if updated successfully</returns>
    Task<bool> UpdateAgentTypesAsync(Guid userId, List<LegacyAgentType> agentTypes, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Updates user's agent type assignments using database-driven agent type IDs
    /// This method manages global security group memberships based on agent types
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="agentTypeIds">New agent type IDs from the database</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is updating agent types</param>
    /// <returns>True if updated successfully</returns>
    Task<bool> UpdateAgentTypeIdsAsync(Guid userId, List<Guid> agentTypeIds, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Updates database assignments for a user
    /// </summary>
    Task<bool> UpdateDatabaseAssignmentsAsync(Guid userId, List<Guid> databaseIds, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Updates supervisor assignment for a user
    /// </summary>
    Task<bool> UpdateSupervisorAssignmentAsync(Guid userId, string supervisorEmail, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Gets user statistics for an organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>User statistics</returns>
    Task<UserStatistics> GetStatisticsAsync(Guid organizationId);
    
    /// <summary>
    /// Enhanced soft delete method using the IsDeleted field with full group cleanup
    /// Removes user from all Azure AD groups (organization, agent-based, and Teams)
    /// </summary>
    /// <param name="userId">User ID to soft delete</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="deletedBy">User ID who is performing the deletion</param>
    /// <returns>True if soft deleted successfully</returns>
    Task<bool> SoftDeleteUserAsync(Guid userId, Guid organizationId, Guid deletedBy);
    
    /// <summary>
    /// Enhanced user restoration method that restores all group memberships
    /// Adds user back to organization, agent-based, and Teams groups
    /// </summary>
    /// <param name="userId">User ID to restore</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="restoredBy">User ID who is performing the restoration</param>
    /// <returns>True if restored successfully</returns>
    Task<bool> RestoreUserAsync(Guid userId, Guid organizationId, Guid restoredBy);
    
    /// <summary>
    /// Gets all soft-deleted users for an organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>List of soft-deleted users</returns>
    Task<List<OnboardedUser>> GetDeletedUsersByOrganizationAsync(Guid organizationId);
    
    /// <summary>
    /// Super Admin method: Gets users from any organization bypassing tenant isolation
    /// Should only be used by super admin operations for cross-organizational management
    /// </summary>
    /// <param name=\"organizationId\">Organization ID</param>
    /// <returns>List of users in the organization</returns>
    Task<List<OnboardedUser>> GetByOrganizationForSuperAdminAsync(Guid organizationId);
    
    /// <summary>
    /// Gets the current agent type assignments for a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>List of agent type entities currently assigned to the user</returns>
    Task<List<AgentTypeEntity>> GetUserAgentTypesAsync(Guid userId, Guid organizationId);
    
    /// <summary>
    /// Updates user agent type assignments with comprehensive validation and Azure AD sync
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="newAgentTypeIds">List of new agent type IDs to assign</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is making the changes</param>
    /// <returns>True if updated successfully with Azure AD sync</returns>
    Task<bool> UpdateUserAgentTypesWithSyncAsync(Guid userId, List<Guid> newAgentTypeIds, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Updates user agent type assignments with flexible Azure AD sync fallback handling
    /// This method attempts Azure AD sync but will still save database changes if sync fails (with detailed logging)
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="newAgentTypeIds">List of new agent type IDs to assign</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is making the changes</param>
    /// <returns>Result object with success status, database updated status, and Azure sync status</returns>
    Task<UserAgentUpdateResult> UpdateUserAgentTypesWithFallbackAsync(Guid userId, List<Guid> newAgentTypeIds, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Validates agent type assignment ensuring at least one agent type is always selected
    /// </summary>
    /// <param name="agentTypeIds">List of agent type IDs to validate</param>
    /// <returns>True if valid assignment (at least one agent type)</returns>
    Task<bool> ValidateAgentTypeAssignmentAsync(List<Guid> agentTypeIds);
}

/// <summary>
/// Result of user agent type update operation with detailed status information
/// </summary>
public class UserAgentUpdateResult
{
    public bool Success { get; set; }
    public bool DatabaseUpdated { get; set; }
    public bool AzureADSynced { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WarningMessage { get; set; }
}

/// <summary>
/// User statistics for reporting
/// </summary>
public class UserStatistics
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
    public int PendingInvitations { get; set; }
    public Dictionary<LegacyAgentType, int> UsersByAgentType { get; set; } = new();
    public Dictionary<DatabaseType, int> UsersByDatabaseType { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}