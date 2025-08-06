using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing AgentType entities in the database
/// Provides CRUD operations and business logic for agent type management
/// </summary>
public interface IAgentTypeService
{
    /// <summary>
    /// Gets all active agent types available for assignment
    /// </summary>
    /// <returns>List of active agent types ordered by DisplayOrder</returns>
    Task<List<AgentTypeEntity>> GetActiveAgentTypesAsync();
    
    /// <summary>
    /// Gets all agent types (active and inactive) for developer management
    /// </summary>
    /// <returns>List of all agent types</returns>
    Task<List<AgentTypeEntity>> GetAllAgentTypesAsync();
    
    /// <summary>
    /// Gets specific agent types by their IDs
    /// </summary>
    /// <param name="agentTypeIds">List of agent type IDs to retrieve</param>
    /// <returns>List of agent types matching the provided IDs</returns>
    Task<List<AgentTypeEntity>> GetAgentTypesByIdsAsync(List<Guid> agentTypeIds);
    
    /// <summary>
    /// Gets a specific agent type by ID
    /// </summary>
    /// <param name="id">Agent type ID</param>
    /// <returns>Agent type if found, null otherwise</returns>
    Task<AgentTypeEntity?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Creates a new agent type (DevRole only)
    /// </summary>
    /// <param name="agentType">Agent type to create</param>
    /// <returns>Created agent type with assigned ID</returns>
    Task<AgentTypeEntity> CreateAsync(AgentTypeEntity agentType);
    
    /// <summary>
    /// Updates an existing agent type (DevRole only)
    /// </summary>
    /// <param name="agentType">Agent type with updated values</param>
    /// <returns>True if updated successfully</returns>
    Task<bool> UpdateAsync(AgentTypeEntity agentType);
    
    /// <summary>
    /// Soft deletes an agent type by setting IsActive to false (DevRole only)
    /// </summary>
    /// <param name="id">Agent type ID to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteAsync(Guid id);
    
    /// <summary>
    /// Reorders agent types by updating their DisplayOrder (DevRole only)
    /// </summary>
    /// <param name="orderedIds">List of agent type IDs in desired order</param>
    /// <returns>True if reordered successfully</returns>
    Task<bool> ReorderAsync(List<Guid> orderedIds);
    
    /// <summary>
    /// Gets agent types that have GlobalSecurityGroupId configured
    /// Used for security group assignment
    /// </summary>
    /// <returns>List of agent types with security groups</returns>
    Task<List<AgentTypeEntity>> GetAgentTypesWithSecurityGroupsAsync();
    
    /// <summary>
    /// Validates that a GlobalSecurityGroupId exists in Azure AD (DevRole only)
    /// </summary>
    /// <param name="groupId">Security group ID to validate</param>
    /// <returns>True if group exists and is accessible</returns>
    Task<bool> ValidateSecurityGroupAsync(string groupId);
}