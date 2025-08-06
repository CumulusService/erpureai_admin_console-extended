using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Interface for managing user database access assignments
/// Handles which databases each user can access within their organization
/// </summary>
public interface IUserDatabaseAccessService
{
    /// <summary>
    /// Assigns database access to a user
    /// </summary>
    Task<bool> AssignDatabaseToUserAsync(Guid userId, Guid databaseCredentialId, Guid organizationId, string assignedBy);
    
    /// <summary>
    /// Removes database access from a user
    /// </summary>
    Task<bool> RemoveDatabaseFromUserAsync(Guid userId, Guid databaseCredentialId, Guid organizationId);
    
    /// <summary>
    /// Gets all database credentials assigned to a user
    /// </summary>
    Task<List<DatabaseCredential>> GetUserAssignedDatabasesAsync(Guid userId, Guid organizationId);
    
    /// <summary>
    /// Gets all users assigned to a specific database
    /// </summary>
    Task<List<OnboardedUser>> GetDatabaseUsersAsync(Guid databaseCredentialId, Guid organizationId);
    
    /// <summary>
    /// Updates user's database assignments (replaces all existing assignments)
    /// </summary>
    Task<bool> UpdateUserDatabaseAssignmentsAsync(Guid userId, List<Guid> databaseCredentialIds, Guid organizationId, string assignedBy);
    
    /// <summary>
    /// Checks if a user has access to a specific database
    /// </summary>
    Task<bool> UserHasDatabaseAccessAsync(Guid userId, Guid databaseCredentialId, Guid organizationId);
    
    /// <summary>
    /// Gets all database assignments for an organization
    /// </summary>
    Task<List<UserDatabaseAssignment>> GetOrganizationDatabaseAssignmentsAsync(Guid organizationId);
}