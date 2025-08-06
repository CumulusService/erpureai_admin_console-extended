using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AdminConsole.Services;

/// <summary>
/// Implementation of onboarded user service using Entity Framework
/// </summary>
public class OnboardedUserService : IOnboardedUserService
{
    private readonly AdminConsoleDbContext _context;
    private readonly ILogger<OnboardedUserService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IDataIsolationService _dataIsolationService;
    private readonly ITenantIsolationValidator _tenantValidator;
    private readonly ISecurityGroupService _securityGroupService;
    private readonly ITeamsGroupService _teamsGroupService;
    private readonly IAgentTypeService _agentTypeService;
    private readonly IGraphService _graphService;

    public OnboardedUserService(
        AdminConsoleDbContext context,
        ILogger<OnboardedUserService> logger,
        IMemoryCache cache,
        IDataIsolationService dataIsolationService,
        ITenantIsolationValidator tenantValidator,
        ISecurityGroupService securityGroupService,
        ITeamsGroupService teamsGroupService,
        IAgentTypeService agentTypeService,
        IGraphService graphService)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
        _dataIsolationService = dataIsolationService;
        _tenantValidator = tenantValidator;
        _securityGroupService = securityGroupService;
        _teamsGroupService = teamsGroupService;
        _agentTypeService = agentTypeService;
        _graphService = graphService;
    }

    public async Task<List<OnboardedUser>> GetByOrganizationAsync(Guid organizationId)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "list-users");
            
            var cacheKey = $"users_org_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<OnboardedUser>? cachedUsers))
            {
                return cachedUsers ?? new List<OnboardedUser>();
            }

            var users = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId && u.StateCode == StateCode.Active)
                .OrderByDescending(u => u.CreatedOn)
                .ToListAsync();

            // Cache for 5 minutes
            _cache.Set(cacheKey, users, TimeSpan.FromMinutes(5));

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users for organization {OrganizationId}", organizationId);
            return new List<OnboardedUser>();
        }
    }

    public async Task<OnboardedUser?> GetByIdAsync(Guid userId, Guid organizationId)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "get-user");

            return await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID {UserId} for organization {OrganizationId}", userId, organizationId);
            return null;
        }
    }

    public async Task<OnboardedUser?> GetByIdAsync(Guid userId)
    {
        try
        {
            return await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID {UserId}", userId);
            return null;
        }
    }

    public async Task<OnboardedUser?> GetByEmailAsync(string email, Guid organizationId)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "get-user");

            return await _context.OnboardedUsers
                .Where(u => u.Email == email && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by email {Email} for organization {OrganizationId}", email, organizationId);
            return null;
        }
    }

    public async Task<OnboardedUser> CreateAsync(OnboardedUser user, Guid createdBy)
    {
        user.CreatedBy = createdBy;
        user.ModifiedBy = createdBy;
        return await CreateAsync(user);
    }

    public async Task<OnboardedUser> CreateAsync(OnboardedUser user)
    {
        try
        {
            user.OnboardedUserId = Guid.NewGuid();
            user.CreatedOn = DateTime.UtcNow;
            user.ModifiedOn = DateTime.UtcNow;
            user.StateCode = StateCode.Active;
            user.StatusCode = StatusCode.Active;

            _context.OnboardedUsers.Add(user);
            await _context.SaveChangesAsync();

            InvalidateCache(user.OrganizationLookupId);

            _logger.LogInformation("Created user {Email} for organization {OrganizationId}", 
                user.Email, user.OrganizationLookupId);

            // Add user to groups if they have agent types assigned
            if (user.AgentTypes.Any() && user.OrganizationLookupId.HasValue)
            {
                await UpdateUserGroupMembershipsAsync(user.OnboardedUserId.ToString(), user.OrganizationLookupId.Value, new List<LegacyAgentType>(), user.AgentTypes);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user {Email}", user.Email);
            throw;
        }
    }

    public async Task<OnboardedUser> UpdateAsync(OnboardedUser user, Guid modifiedBy)
    {
        user.ModifiedBy = modifiedBy;
        var success = await UpdateAsync(user);
        if (!success)
        {
            throw new InvalidOperationException($"Failed to update user {user.OnboardedUserId}");
        }
        return user;
    }

    public async Task<bool> UpdateAsync(OnboardedUser user)
    {
        try
        {
            var existingUser = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == user.OnboardedUserId)
                .FirstOrDefaultAsync();

            if (existingUser != null)
            {
                existingUser.Email = user.Email;
                existingUser.FullName = user.FullName;
                existingUser.Name = user.Name;
                existingUser.AssignedDatabaseIds = user.AssignedDatabaseIds;
                existingUser.AgentTypes = user.AgentTypes; // Legacy field for backward compatibility
                existingUser.AgentTypeIds = user.AgentTypeIds; // New database-driven agent type IDs
                existingUser.IsActive = user.IsActive;
                existingUser.AssignedSupervisorEmail = user.AssignedSupervisorEmail;
                existingUser.StateCode = user.StateCode;
                existingUser.StatusCode = user.StatusCode;
                existingUser.ModifiedOn = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                InvalidateCache(existingUser.OrganizationLookupId);

                _logger.LogInformation("Updated user {UserId}", user.OnboardedUserId);
                return true;
            }

            _logger.LogWarning("User {UserId} not found for update", user.OnboardedUserId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", user.OnboardedUserId);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid userId)
    {
        try
        {
            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                // Soft delete by setting state to inactive
                user.StateCode = StateCode.Inactive;
                user.StatusCode = StatusCode.Inactive;
                user.ModifiedOn = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                InvalidateCache(user.OrganizationLookupId);

                _logger.LogInformation("Deleted user {UserId}", userId);
                return true;
            }

            _logger.LogWarning("User {UserId} not found for deletion", userId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return false;
        }
    }

    public async Task<List<OnboardedUser>> SearchAsync(string searchTerm, Guid? organizationId = null)
    {
        try
        {
            var query = _context.OnboardedUsers
                .Where(u => u.StateCode == StateCode.Active);

            if (organizationId.HasValue)
            {
                // Enhanced tenant isolation validation
                await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.Value.ToString(), "search-users");
                query = query.Where(u => u.OrganizationLookupId == organizationId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(u => 
                    u.Email.Contains(searchTerm) || 
                    u.FullName.Contains(searchTerm) ||
                    u.Name.Contains(searchTerm));
            }

            return await query
                .OrderByDescending(u => u.CreatedOn)
                .Take(50) // Limit results
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users with term {SearchTerm}", searchTerm);
            return new List<OnboardedUser>();
        }
    }

    public async Task<bool> ExistsAsync(string email, Guid organizationId)
    {
        try
        {
            return await _context.OnboardedUsers
                .AnyAsync(u => u.Email == email && 
                              u.OrganizationLookupId == organizationId && 
                              u.StateCode == StateCode.Active);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if user exists {Email} for organization {OrganizationId}", 
                email, organizationId);
            return false;
        }
    }

    public async Task<int> GetCountByOrganizationAsync(Guid organizationId)
    {
        try
        {
            return await _context.OnboardedUsers
                .CountAsync(u => u.OrganizationLookupId == organizationId && u.StateCode == StateCode.Active);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user count for organization {OrganizationId}", organizationId);
            return 0;
        }
    }

    public async Task<bool> DeactivateAsync(Guid userId, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "deactivate-user");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                user.StateCode = StateCode.Inactive;
                user.StatusCode = StatusCode.Inactive;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                _logger.LogInformation("Attempting to save user deactivation changes for {UserId}", userId);
                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Successfully deactivated user {UserId} in organization {OrganizationId}", userId, organizationId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    public async Task<bool> ReactivateAsync(Guid userId, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "reactivate-user");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                user.StateCode = StateCode.Active;
                user.StatusCode = StatusCode.Active;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                _logger.LogInformation("Attempting to save user reactivation changes for {UserId}", userId);
                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Successfully reactivated user {UserId} in organization {OrganizationId}", userId, organizationId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    public async Task<bool> UpdateAgentTypesAsync(Guid userId, List<LegacyAgentType> agentTypes, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "update-user-agent-types");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                var previousAgentTypes = user.AgentTypes.ToList();
                user.AgentTypes = agentTypes;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Updated agent types for user {UserId} in organization {OrganizationId}", userId, organizationId);

                // Update security group and Teams group memberships based on agent types
                await UpdateUserGroupMembershipsAsync(userId.ToString(), organizationId, previousAgentTypes, agentTypes);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent types for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// Updates user's agent type assignments using database-driven agent type IDs
    /// This method replaces enum-based agent types with database-driven ones and manages global security group memberships
    /// </summary>
    public async Task<bool> UpdateAgentTypeIdsAsync(Guid userId, List<Guid> agentTypeIds, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "update-user-agent-types");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                var previousAgentTypeIds = user.AgentTypeIds.ToList();
                user.AgentTypeIds = agentTypeIds;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Updated agent type IDs for user {UserId} in organization {OrganizationId}. New agent types: {AgentTypeIds}", 
                    userId, organizationId, string.Join(", ", agentTypeIds));

                // Update global security group memberships based on agent type IDs
                await UpdateUserGlobalSecurityGroupMembershipsAsync(userId.ToString(), organizationId, previousAgentTypeIds, agentTypeIds);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent type IDs for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    public async Task<bool> UpdateDatabaseAssignmentsAsync(Guid userId, List<Guid> databaseIds, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Get user and validate organization
            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId);

            if (user != null)
            {
                user.AssignedDatabaseIds = databaseIds;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Updated database assignments for user {UserId} in organization {OrganizationId}. Assigned {DatabaseCount} databases", 
                    userId, organizationId, databaseIds.Count);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating database assignments for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    public async Task<bool> UpdateSupervisorAssignmentAsync(Guid userId, string supervisorEmail, Guid organizationId, Guid modifiedBy)
    {
        try
        {
            // Get user and validate organization
            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId);

            if (user != null)
            {
                user.AssignedSupervisorEmail = supervisorEmail;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;

                await _context.SaveChangesAsync();
                InvalidateCache(organizationId);

                _logger.LogInformation("Updated supervisor assignment for user {UserId} in organization {OrganizationId}. Supervisor: {SupervisorEmail}", 
                    userId, organizationId, string.IsNullOrEmpty(supervisorEmail) ? "None" : supervisorEmail);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating supervisor assignment for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    public async Task<UserStatistics> GetStatisticsAsync(Guid organizationId)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "get-user-statistics");

            var users = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId)
                .ToListAsync();

            var statistics = new UserStatistics
            {
                TotalUsers = users.Count,
                ActiveUsers = users.Count(u => u.StateCode == StateCode.Active),
                InactiveUsers = users.Count(u => u.StateCode == StateCode.Inactive),
                PendingInvitations = 0, // Would need invitation tracking
                LastUpdated = DateTime.UtcNow
            };

            // Calculate users by agent type
            foreach (var user in users.Where(u => u.StateCode == StateCode.Active))
            {
                foreach (var agentType in user.AgentTypes)
                {
                    if (statistics.UsersByAgentType.ContainsKey(agentType))
                        statistics.UsersByAgentType[agentType]++;
                    else
                        statistics.UsersByAgentType[agentType] = 1;
                }
            }

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user statistics for organization {OrganizationId}", organizationId);
            return new UserStatistics();
        }
    }

    /// <summary>
    /// Enhanced soft delete method using the IsDeleted field with full group cleanup
    /// Removes user from all Azure AD groups (organization, agent-based, and Teams)
    /// </summary>
    public async Task<bool> SoftDeleteUserAsync(Guid userId, Guid organizationId, Guid deletedBy)
    {
        try
        {
            _logger.LogInformation("Starting soft delete process for user {UserId} in organization {OrganizationId}", userId, organizationId);

            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "delete-user");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for soft delete in organization {OrganizationId}", userId, organizationId);
                return false;
            }

            // Get user's Azure AD ID for group operations
            var azureUserId = user.GetUserPrincipalName(); // This will need to be converted to actual Azure AD user ID
            
            // Step 1: Remove from organization-based security groups
            try
            {
                await _securityGroupService.RemoveUserFromOrganizationGroupAsync(azureUserId, organizationId.ToString());
                _logger.LogInformation("Removed user {UserId} from organization security groups", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove user {UserId} from organization security groups during soft delete", userId);
            }

            // Step 2: Remove from agent-based security groups (new feature)
            // TODO: Implement agent-based group cleanup when AgentGroupAssignmentService is available
            if (user.AgentTypeIds.Any())
            {
                _logger.LogInformation("Agent-based group deactivation needed for user {UserId} but service not available", userId);
            }

            // Step 3: Remove from organization Teams group
            try
            {
                await _teamsGroupService.RemoveUserFromOrganizationTeamsGroupAsync(azureUserId, organizationId);
                _logger.LogInformation("Removed user {UserId} from organization Teams group", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove user {UserId} from organization Teams group during soft delete", userId);
            }

            // Step 4: Update user record with soft delete flags
            user.IsDeleted = true; // New soft delete field
            user.StateCode = StateCode.Inactive; // Existing field for backward compatibility
            user.StatusCode = StatusCode.Inactive;
            user.ModifiedOn = DateTime.UtcNow;
            user.ModifiedBy = deletedBy;

            await _context.SaveChangesAsync();
            InvalidateCache(organizationId);

            _logger.LogInformation("Successfully soft deleted user {UserId} ({Email}) in organization {OrganizationId}", 
                userId, user.Email, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during soft delete of user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// Enhanced user restoration method that restores all group memberships
    /// Adds user back to organization, agent-based, and Teams groups
    /// </summary>
    public async Task<bool> RestoreUserAsync(Guid userId, Guid organizationId, Guid restoredBy)
    {
        try
        {
            _logger.LogInformation("Starting user restoration process for user {UserId} in organization {OrganizationId}", userId, organizationId);

            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "restore-user");

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId && u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for restoration or not deleted in organization {OrganizationId}", userId, organizationId);
                return false;
            }

            // Get user's Azure AD ID for group operations
            var azureUserId = user.GetUserPrincipalName(); // This will need to be converted to actual Azure AD user ID

            // Step 1: Restore user record
            user.IsDeleted = false; // New soft delete field
            user.StateCode = StateCode.Active; // Existing field for backward compatibility
            user.StatusCode = StatusCode.Active;
            user.ModifiedOn = DateTime.UtcNow;
            user.ModifiedBy = restoredBy;

            // Step 2: Re-add to organization-based security groups
            try
            {
                await _securityGroupService.AddUserToOrganizationGroupAsync(azureUserId, organizationId.ToString());
                _logger.LogInformation("Re-added user {UserId} to organization security groups", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-add user {UserId} to organization security groups during restoration", userId);
            }

            // Step 3: Re-add to agent-based security groups (new feature)
            // TODO: Implement agent-based group reactivation when AgentGroupAssignmentService is available
            if (user.AgentTypeIds.Any())
            {
                _logger.LogInformation("Agent-based group reactivation needed for user {UserId} but service not available", userId);
            }

            // Step 4: Re-add to organization Teams group
            try
            {
                await _teamsGroupService.AddUserToOrganizationTeamsGroupAsync(azureUserId, organizationId);
                _logger.LogInformation("Re-added user {UserId} to organization Teams group", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-add user {UserId} to organization Teams group during restoration", userId);
            }

            await _context.SaveChangesAsync();
            InvalidateCache(organizationId);

            _logger.LogInformation("Successfully restored user {UserId} ({Email}) in organization {OrganizationId}", 
                userId, user.Email, organizationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during restoration of user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// Gets all soft-deleted users for an organization
    /// </summary>
    public async Task<List<OnboardedUser>> GetDeletedUsersByOrganizationAsync(Guid organizationId)
    {
        try
        {
            // Enhanced tenant isolation validation
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "list-deleted-users");

            return await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId && u.IsDeleted)
                .OrderByDescending(u => u.ModifiedOn)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting deleted users for organization {OrganizationId}", organizationId);
            return new List<OnboardedUser>();
        }
    }

    private void InvalidateCache(Guid? organizationId)
    {
        if (organizationId.HasValue)
        {
            var cacheKey = $"users_org_{organizationId.Value}";
            _cache.Remove(cacheKey);
        }
    }

    /// <summary>
    /// Updates user's global security group memberships based on database-driven agent type IDs
    /// Each agent type has a GlobalSecurityGroupId that users are added to
    /// </summary>
    private async Task UpdateUserGlobalSecurityGroupMembershipsAsync(string userId, Guid organizationId, List<Guid> previousAgentTypeIds, List<Guid> newAgentTypeIds)
    {
        try
        {
            _logger.LogInformation("Updating global security group memberships for user {UserId} based on agent type IDs", userId);

            // Get agent type entities for both previous and new assignments
            var previousAgentTypes = previousAgentTypeIds.Any() ? await _agentTypeService.GetAgentTypesByIdsAsync(previousAgentTypeIds) : new List<AgentTypeEntity>();
            var newAgentTypes = newAgentTypeIds.Any() ? await _agentTypeService.GetAgentTypesByIdsAsync(newAgentTypeIds) : new List<AgentTypeEntity>();

            // Remove user from global security groups of agent types they no longer have
            var removedAgentTypes = previousAgentTypes.Where(prev => !newAgentTypeIds.Contains(prev.Id)).ToList();
            foreach (var removedAgentType in removedAgentTypes)
            {
                if (!string.IsNullOrEmpty(removedAgentType.GlobalSecurityGroupId))
                {
                    try
                    {
                        // Use GraphService to remove user from global security group
                        await _graphService.RemoveUserFromGroupAsync(userId, removedAgentType.GlobalSecurityGroupId);
                        _logger.LogInformation("Removed user {UserId} from global security group {GroupId} for removed agent type {AgentType}", 
                            userId, removedAgentType.GlobalSecurityGroupId, removedAgentType.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove user {UserId} from global security group {GroupId} for agent type {AgentType}", 
                            userId, removedAgentType.GlobalSecurityGroupId, removedAgentType.DisplayName);
                    }
                }
            }

            // Add user to global security groups of new agent types
            var addedAgentTypes = newAgentTypes.Where(newType => !previousAgentTypeIds.Contains(newType.Id)).ToList();
            foreach (var addedAgentType in addedAgentTypes)
            {
                if (!string.IsNullOrEmpty(addedAgentType.GlobalSecurityGroupId))
                {
                    try
                    {
                        // Use GraphService to add user to global security group
                        await _graphService.AddUserToGroupAsync(userId, addedAgentType.GlobalSecurityGroupId);
                        _logger.LogInformation("Added user {UserId} to global security group {GroupId} for new agent type {AgentType}", 
                            userId, addedAgentType.GlobalSecurityGroupId, addedAgentType.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add user {UserId} to global security group {GroupId} for agent type {AgentType}", 
                            userId, addedAgentType.GlobalSecurityGroupId, addedAgentType.DisplayName);
                    }
                }
                else
                {
                    _logger.LogWarning("Agent type {AgentType} has no GlobalSecurityGroupId configured, skipping global security group assignment", 
                        addedAgentType.DisplayName);
                }
            }

            // Always ensure user is in organization's Teams group if they have any agent types
            if (newAgentTypeIds.Any())
            {
                try
                {
                    var addedToTeamsGroup = await _teamsGroupService.AddUserToOrganizationTeamsGroupAsync(userId, organizationId);
                    if (addedToTeamsGroup)
                    {
                        _logger.LogInformation("Added user {UserId} to organization Teams group", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add user {UserId} to organization Teams group", userId);
                }
            }

            _logger.LogInformation("Completed global security group membership updates for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating global security group memberships for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Updates user's security group and Teams group memberships based on agent type changes
    /// Users are added to the organization's security group and Teams group for each agent type
    /// </summary>
    private async Task UpdateUserGroupMembershipsAsync(string userId, Guid organizationId, List<LegacyAgentType> previousAgentTypes, List<LegacyAgentType> newAgentTypes)
    {
        try
        {
            _logger.LogInformation("Updating group memberships for user {UserId} in organization {OrganizationId}", userId, organizationId);

            // For now, we'll manage organization-level groups regardless of specific agent types
            // In the future, we could create agent-specific groups if needed
            
            // Ensure user is in organization's security group if they have any agent types
            if (newAgentTypes.Any())
            {
                try
                {
                    var addedToSecurityGroup = await _securityGroupService.AddUserToOrganizationGroupAsync(userId, organizationId.ToString());
                    if (addedToSecurityGroup)
                    {
                        _logger.LogInformation("Added user {UserId} to organization security group", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add user {UserId} to organization security group", userId);
                }

                // Ensure user is in organization's Teams group if they have any agent types
                try
                {
                    var addedToTeamsGroup = await _teamsGroupService.AddUserToOrganizationTeamsGroupAsync(userId, organizationId);
                    if (addedToTeamsGroup)
                    {
                        _logger.LogInformation("Added user {UserId} to organization Teams group", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add user {UserId} to organization Teams group", userId);
                }
            }
            else if (previousAgentTypes.Any() && !newAgentTypes.Any())
            {
                // User had agent types but now has none - consider removing from groups
                // For now, we'll keep them in groups for collaboration purposes
                _logger.LogInformation("User {UserId} no longer has agent types but will remain in groups for collaboration", userId);
            }

            _logger.LogInformation("Completed group membership updates for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group memberships for user {UserId} in organization {OrganizationId}", userId, organizationId);
        }
    }
}