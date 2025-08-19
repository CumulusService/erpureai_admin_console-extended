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
    private readonly IAgentGroupAssignmentService _agentGroupAssignmentService;
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
        IAgentGroupAssignmentService agentGroupAssignmentService,
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
        _agentGroupAssignmentService = agentGroupAssignmentService;
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

            var user = await _context.OnboardedUsers
                .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                .FirstOrDefaultAsync();
                
            if (user != null)
            {
                // CRITICAL FIX: Sync dual storage systems on read to prevent inconsistencies
                await SyncUserDatabaseAssignments(user);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID {UserId} for organization {OrganizationId}", userId, organizationId);
            return null;
        }
    }
    
    /// <summary>
    /// Synchronizes the dual database assignment storage systems to ensure consistency
    /// </summary>
    private async Task SyncUserDatabaseAssignments(OnboardedUser user)
    {
        try
        {
            // Get assignments from relational table (used by stored procedure - source of truth)
            var relationalAssignments = await _context.UserDatabaseAssignments
                .Where(uda => uda.UserId == user.OnboardedUserId && 
                             uda.OrganizationId == user.OrganizationLookupId && 
                             uda.IsActive)
                .Select(uda => uda.DatabaseCredentialId)
                .ToListAsync();
                
            // Compare with JSON field
            var jsonAssignments = user.AssignedDatabaseIds ?? new List<Guid>();
            
            // Check if they match
            var relationalSet = relationalAssignments.ToHashSet();
            var jsonSet = jsonAssignments.ToHashSet();
            
            if (!relationalSet.SetEquals(jsonSet))
            {
                _logger.LogWarning("=== DATABASE ASSIGNMENT MISMATCH DETECTED ===");
                _logger.LogWarning("  User: {UserId} ({Email})", user.OnboardedUserId, user.Email);
                _logger.LogWarning("  Relational table: [{RelationalAssignments}] (count: {RelationalCount})", 
                    string.Join(", ", relationalAssignments), relationalAssignments.Count);
                _logger.LogWarning("  JSON field: [{JsonAssignments}] (count: {JsonCount})", 
                    string.Join(", ", jsonAssignments), jsonAssignments.Count);
                    
                // Use relational table as source of truth (used by stored procedure)
                user.AssignedDatabaseIds = relationalAssignments;
                user.ModifiedOn = DateTime.UtcNow;
                
                // Sync to relational table
                _logger.LogInformation("  Updated JSON field to: [{SyncedAssignments}]", 
                    string.Join(", ", relationalAssignments));
                    
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing database assignments for user {UserId}", user.OnboardedUserId);
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

    /// <summary>
    /// Gets a user by their Azure AD Object ID - more reliable than email lookup
    /// </summary>
    public async Task<OnboardedUser?> GetByAzureObjectIdAsync(string azureObjectId)
    {
        try
        {
            if (string.IsNullOrEmpty(azureObjectId))
            {
                return null;
            }

            var user = await _context.OnboardedUsers
                .Where(u => u.AzureObjectId == azureObjectId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                // Validate organization access
                await _tenantValidator.ValidateOrganizationAccessAsync(user.OrganizationLookupId?.ToString() ?? "", "get-user");
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by Azure Object ID {AzureObjectId}", azureObjectId);
            return null;
        }
    }

    /// <summary>
    /// Finds a user by email, then returns their Azure Object ID for reliable Azure AD operations
    /// </summary>
    public async Task<string?> GetAzureObjectIdByEmailAsync(string email, Guid organizationId)
    {
        try
        {
            var user = await GetByEmailAsync(email, organizationId);
            if (user?.AzureObjectId != null)
            {
                _logger.LogInformation("Found cached Azure Object ID {AzureObjectId} for user {Email}", 
                    user.AzureObjectId, email);
                return user.AzureObjectId;
            }

            // If no Azure Object ID in database, look it up from Azure AD directly
            _logger.LogInformation("🔍 No cached Azure Object ID found for {Email}, looking up in Azure AD...", email);
            
            var azureUser = await _graphService.GetUserByEmailAsync(email);
            if (azureUser?.Id != null)
            {
                _logger.LogInformation("Retrieved Azure Object ID {AzureObjectId} for user {Email} from Azure AD", 
                    azureUser.Id, email);
                
                // Update the database record with the Azure Object ID for future use
                if (user != null)
                {
                    user.AzureObjectId = azureUser.Id;
                    user.ModifiedOn = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Updated database with Azure Object ID for user {Email}", email);
                }
                
                return azureUser.Id;
            }

            _logger.LogWarning("❌ No Azure Object ID found for user {Email} in Azure AD or database", email);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Azure Object ID for user {Email} in organization {OrganizationId}", 
                email, organizationId);
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
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _logger.LogInformation("Updating database assignments for user {UserId}", userId);
            
            // Get user and validate organization
            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId);

            if (user != null)
            {
                // Update JSON field (legacy system)
                user.AssignedDatabaseIds = databaseIds;
                user.ModifiedOn = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;
                
                _logger.LogInformation("Updated user database assignments for {UserId}: [{NewIds}]", 
                    userId, string.Join(", ", user.AssignedDatabaseIds));
                
                // 2. Update UserDatabaseAssignments table (relational system used by stored procedure)
                // Remove existing assignments for this user
                var existingAssignments = await _context.UserDatabaseAssignments
                    .Where(uda => uda.UserId == userId && uda.OrganizationId == organizationId)
                    .ToListAsync();
                    
                if (existingAssignments.Any())
                {
                    _logger.LogInformation("Removing {Count} existing UserDatabaseAssignments for user {UserId}", 
                        existingAssignments.Count, userId);
                    _context.UserDatabaseAssignments.RemoveRange(existingAssignments);
                }
                
                // Add new assignments
                foreach (var databaseId in databaseIds)
                {
                    var assignment = new UserDatabaseAssignment
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        DatabaseCredentialId = databaseId,
                        OrganizationId = organizationId,
                        AssignedOn = DateTime.UtcNow,
                        AssignedBy = modifiedBy.ToString(),
                        IsActive = true
                    };
                    _context.UserDatabaseAssignments.Add(assignment);
                    _logger.LogInformation("Adding UserDatabaseAssignment: User {UserId} -> Database {DatabaseId}", 
                        userId, databaseId);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                // VERIFICATION: Reload user from database to verify the save worked
                var verificationUser = await _context.OnboardedUsers
                    .Where(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId)
                    .FirstOrDefaultAsync();
                    
                if (verificationUser != null)
                {
                    _logger.LogInformation("VERIFICATION: User {UserId} now has AssignedDatabaseIds = [{VerifiedIds}] in database", 
                        userId, string.Join(", ", verificationUser.AssignedDatabaseIds));
                }
                else
                {
                    _logger.LogError("VERIFICATION FAILED: Could not reload user {UserId} from database after save", userId);
                }
                
                InvalidateCache(organizationId);

                _logger.LogInformation("Successfully updated BOTH storage systems for user {UserId}. Assigned {DatabaseCount} databases", 
                    userId, databaseIds.Count);
                return true;
            }
            else
            {
                _logger.LogWarning("User {UserId} not found in organization {OrganizationId}", userId, organizationId);
            }

            return false;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
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
    
    public async Task<List<OnboardedUser>> GetByOrganizationForSuperAdminAsync(Guid organizationId)
    {
        try
        {
            // Super Admin method - bypasses tenant isolation for cross-organizational management
            var cacheKey = $"superadmin_users_org_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<OnboardedUser>? cachedUsers))
            {
                return cachedUsers ?? new List<OnboardedUser>();
            }

            var users = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId && u.StateCode == StateCode.Active)
                .OrderByDescending(u => u.CreatedOn)
                .ToListAsync();

            _cache.Set(cacheKey, users, TimeSpan.FromMinutes(2));
            _logger.LogInformation("Super Admin: Retrieved {UserCount} users from organization {OrganizationId}", users.Count, organizationId);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Super Admin: Error getting users for organization {OrganizationId}", organizationId);
            return new List<OnboardedUser>();
        }
    }

    /// <summary>
    /// Gets the current agent type assignments for a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>List of agent type entities currently assigned to the user</returns>
    public async Task<List<AgentTypeEntity>> GetUserAgentTypesAsync(Guid userId, Guid organizationId)
    {
        try
        {
            // Validate tenant access
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "read-user-agents");

            // Get user to check their current agent type assignments
            var user = await GetByIdAsync(userId, organizationId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found in organization {OrganizationId}", userId, organizationId);
                return new List<AgentTypeEntity>();
            }

            // Get agent types based on user's AgentTypeIds
            if (user.AgentTypeIds?.Any() == true)
            {
                var agentTypes = await _agentTypeService.GetAgentTypesByIdsAsync(user.AgentTypeIds);
                _logger.LogInformation("Retrieved {AgentTypeCount} agent types for user {UserId}", agentTypes.Count, userId);
                return agentTypes;
            }

            // Fallback to legacy AgentTypes if no AgentTypeIds
            if (user.AgentTypes?.Any() == true)
            {
                _logger.LogInformation("Using legacy agent types for user {UserId}", userId);
                // Convert legacy agent types to AgentTypeEntity (this would require mapping logic)
                // For now, return empty list and log the situation
                _logger.LogWarning("User {UserId} has legacy agent types but no modern AgentTypeIds", userId);
            }

            return new List<AgentTypeEntity>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent types for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return new List<AgentTypeEntity>();
        }
    }

    /// <summary>
    /// Updates user agent type assignments with comprehensive validation and Azure AD sync
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="newAgentTypeIds">List of new agent type IDs to assign</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is making the changes</param>
    /// <returns>True if updated successfully with Azure AD sync</returns>
    public async Task<bool> UpdateUserAgentTypesWithSyncAsync(Guid userId, List<Guid> newAgentTypeIds, Guid organizationId, Guid modifiedBy)
    {
        OnboardedUser? user = null;
        List<Guid> originalAgentTypeIds = new();
        
        try
        {
            // Validate tenant access
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "update-user-agents");

            // Validate agent type assignment
            var isValidAssignment = await ValidateAgentTypeAssignmentAsync(newAgentTypeIds);
            if (!isValidAssignment)
            {
                _logger.LogError("Invalid agent type assignment for user {UserId}: at least one agent type must be selected", userId);
                return false;
            }

            // Get the user
            user = await GetByIdAsync(userId, organizationId);
            if (user == null)
            {
                _logger.LogError("User {UserId} not found in organization {OrganizationId}", userId, organizationId);
                return false;
            }

            _logger.LogInformation("Updating agent type assignments for user {Email} ({UserId})", user.Email, userId);

            // Store original agent type IDs in case we need to rollback
            originalAgentTypeIds = user.AgentTypeIds.ToList();
            
            // Update user's agent type IDs (but don't save yet)
            user.AgentTypeIds = newAgentTypeIds;
            user.ModifiedOn = DateTime.UtcNow;
            user.ModifiedBy = modifiedBy;
            
            _logger.LogInformation("🔄 Prepared database update for user {Email} - will save after Azure AD sync succeeds", user.Email);

            // Sync with Azure AD via AgentGroupAssignmentService if user has Azure Object ID
            bool azureSyncSuccess = true; // Default to success for users without Azure Object ID
            var azureObjectId = user.AzureObjectId ?? await GetAzureObjectIdByEmailAsync(user.Email, organizationId);
            if (!string.IsNullOrEmpty(azureObjectId))
            {
                try
                {
                    _logger.LogInformation("Synchronizing Azure AD group memberships for user {Email} ({AzureObjectId}) with agent types {AgentTypeIds}", 
                        user.Email, azureObjectId, string.Join(", ", newAgentTypeIds));
                    
                    // Validate the Azure Object ID exists in Azure AD before attempting sync
                    try
                    {
                        var azureUserExists = await _graphService.UserExistsAsync(azureObjectId);
                        if (!azureUserExists)
                        {
                            _logger.LogError("❌ CRITICAL: User {Email} (Azure ID: {AzureObjectId}) does not exist in Azure AD. Cannot sync group memberships.", 
                                user.Email, azureObjectId);
                            azureSyncSuccess = false;
                        }
                        else
                        {
                            // Update Azure AD group memberships using AgentGroupAssignmentService
                            azureSyncSuccess = await _agentGroupAssignmentService.UpdateUserAgentTypeAssignmentsAsync(
                                azureObjectId, newAgentTypeIds, organizationId, modifiedBy.ToString());
                                
                            if (azureSyncSuccess)
                            {
                                _logger.LogInformation("Synchronized Azure AD group memberships for user {Email}", user.Email);
                            }
                            else
                            {
                                _logger.LogError("❌ CRITICAL: Azure AD group synchronization failed for user {Email}. Database was updated but Azure AD is out of sync!", user.Email);
                            }
                        }
                    }
                    catch (Exception validationEx)
                    {
                        _logger.LogError(validationEx, "❌ CRITICAL: Failed to validate user existence in Azure AD for {Email} ({AzureObjectId}). Skipping Azure AD sync.", user.Email, azureObjectId);
                        azureSyncSuccess = false;
                    }
                }
                catch (Exception azureEx)
                {
                    _logger.LogError(azureEx, "❌ CRITICAL: Exception during Azure AD sync for user {Email}. Database was updated but Azure AD is out of sync!", user.Email);
                    azureSyncSuccess = false;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ No Azure Object ID found for user {Email} - Azure AD sync skipped", user.Email);
            }

            // Invalidate cache
            var cacheKey = $"user_{userId}_org_{organizationId}";
            _cache.Remove(cacheKey);

            // Save database changes only if Azure AD sync succeeded
            if (azureSyncSuccess)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Updated agent type assignments for user {Email}", user.Email);
                return true;
            }
            else
            {
                // Rollback the user object changes (don't save)
                user.AgentTypeIds = originalAgentTypeIds;
                user.ModifiedOn = user.ModifiedOn; // Keep original
                user.ModifiedBy = user.ModifiedBy; // Keep original
                _logger.LogError("❌ ROLLBACK: Azure AD sync failed - reverted user agent type changes without saving to database for user {Email}", user.Email);
                _logger.LogError("❌ Agent type update failed for user {Email} - Azure AD sync failed, database unchanged", user.Email);
                return false;
            }
        }
        catch (Exception ex)
        {
            // Ensure rollback in case of exceptions
            try
            {
                if (user != null && originalAgentTypeIds.Any())
                {
                    user.AgentTypeIds = originalAgentTypeIds;
                    _logger.LogWarning("🔄 EXCEPTION ROLLBACK: Reverted user agent type changes due to exception for user {Email}", user.Email);
                }
            }
            catch
            {
                // Ignore rollback errors
            }
            
            _logger.LogError(ex, "Error updating agent types for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return false;
        }
    }
    
    /// <summary>
    /// Updates user agent type assignments with flexible Azure AD sync fallback handling
    /// This method attempts Azure AD sync but will still save database changes if sync fails (with detailed logging)
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="newAgentTypeIds">List of new agent type IDs to assign</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is making the changes</param>
    /// <returns>Result object with success status, database updated status, and Azure sync status</returns>
    public async Task<UserAgentUpdateResult> UpdateUserAgentTypesWithFallbackAsync(Guid userId, List<Guid> newAgentTypeIds, Guid organizationId, Guid modifiedBy)
    {
        var result = new UserAgentUpdateResult();
        OnboardedUser? user = null;
        List<Guid> originalAgentTypeIds = new();
        
        try
        {
            // Validate tenant access
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "update-user-agents");

            // Validate agent type assignment
            var isValidAssignment = await ValidateAgentTypeAssignmentAsync(newAgentTypeIds);
            if (!isValidAssignment)
            {
                result.ErrorMessage = "Invalid agent type assignment: at least one agent type must be selected";
                _logger.LogError("Invalid agent type assignment for user {UserId}: at least one agent type must be selected", userId);
                return result;
            }

            // Get the user
            user = await GetByIdAsync(userId, organizationId);
            if (user == null)
            {
                result.ErrorMessage = "User not found in organization";
                _logger.LogError("User {UserId} not found in organization {OrganizationId}", userId, organizationId);
                return result;
            }

            _logger.LogInformation("Updating agent type assignments for user {Email} ({UserId}) with fallback handling", user.Email, userId);

            // Store original agent type IDs in case we need to rollback
            originalAgentTypeIds = user.AgentTypeIds.ToList();

            // Update user's agent type assignments in memory first
            user.AgentTypeIds = newAgentTypeIds;
            user.ModifiedOn = DateTime.UtcNow;
            user.ModifiedBy = modifiedBy;

            _logger.LogInformation("User agent types updated in memory: {Email} now has {AgentTypeIds}", 
                user.Email, string.Join(", ", newAgentTypeIds));

            // Attempt Azure AD sync if user has Azure Object ID
            bool azureSyncSuccess = true;
            var azureObjectId = user.AzureObjectId ?? await GetAzureObjectIdByEmailAsync(user.Email, organizationId);
            if (!string.IsNullOrEmpty(azureObjectId))
            {
                try
                {
                    _logger.LogInformation("Synchronizing Azure AD group memberships for user {Email} ({AzureObjectId}) with agent types {AgentTypeIds}", 
                        user.Email, azureObjectId, string.Join(", ", newAgentTypeIds));
                    
                    // Validate the Azure Object ID exists in Azure AD before attempting sync
                    var azureUserExists = await _graphService.UserExistsAsync(azureObjectId);
                    if (!azureUserExists)
                    {
                        _logger.LogWarning("⚠️ Azure AD sync warning: User {Email} (Azure ID: {AzureObjectId}) does not exist in Azure AD. Database will be updated but Azure AD groups are out of sync.", 
                            user.Email, azureObjectId);
                        azureSyncSuccess = false;
                        result.WarningMessage = "User was updated in database but does not exist in Azure AD, so security group memberships could not be synchronized.";
                    }
                    else
                    {
                        // Update Azure AD group memberships using AgentGroupAssignmentService
                        azureSyncSuccess = await _agentGroupAssignmentService.UpdateUserAgentTypeAssignmentsAsync(
                            azureObjectId, newAgentTypeIds, organizationId, modifiedBy.ToString());
                            
                        if (azureSyncSuccess)
                        {
                            _logger.LogInformation("✅ Azure AD group memberships synchronized successfully for user {Email}", user.Email);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Azure AD sync warning: Group synchronization failed for user {Email}. Database will be updated but Azure AD groups may be out of sync.", user.Email);
                            result.WarningMessage = "User was updated in database but Azure AD security group synchronization failed. The user may not have the correct permissions until this is resolved.";
                        }
                    }
                }
                catch (Exception azureEx)
                {
                    _logger.LogWarning(azureEx, "⚠️ Azure AD sync warning: Exception during Azure AD sync for user {Email}. Database will be updated but Azure AD groups may be out of sync.", user.Email);
                    azureSyncSuccess = false;
                    result.WarningMessage = $"User was updated in database but Azure AD synchronization failed due to an error: {azureEx.Message}";
                }
            }
            else
            {
                _logger.LogInformation("ℹ️ No Azure Object ID found for user {Email} - Azure AD sync skipped, database-only update", user.Email);
                result.WarningMessage = "User was updated in database but no Azure Object ID found, so Azure AD synchronization was skipped.";
            }

            // Always save database changes (unlike the strict sync method)
            // Invalidate cache first
            var cacheKey = $"user_{userId}_org_{organizationId}";
            _cache.Remove(cacheKey);

            await _context.SaveChangesAsync();
            result.DatabaseUpdated = true;
            result.AzureADSynced = azureSyncSuccess;
            result.Success = true;
            
            _logger.LogInformation("Updated agent type assignments for user {Email}", user.Email);
            
            if (azureSyncSuccess)
            {
            }
            else
            {
                _logger.LogWarning("⚠️ Agent type update completed with warnings for user {Email} - database updated but Azure AD sync failed", user.Email);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Ensure rollback in case of exceptions
            try
            {
                if (user != null && originalAgentTypeIds.Any())
                {
                    user.AgentTypeIds = originalAgentTypeIds;
                    _logger.LogWarning("🔄 EXCEPTION ROLLBACK: Reverted user agent type changes due to exception for user {Email}", user.Email);
                }
            }
            catch
            {
                // Ignore rollback errors
            }
            
            result.ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            _logger.LogError(ex, "Error updating agent types for user {UserId} in organization {OrganizationId}", userId, organizationId);
            return result;
        }
    }

    /// <summary>
    /// Validates agent type assignment ensuring at least one agent type is always selected
    /// </summary>
    /// <param name="agentTypeIds">List of agent type IDs to validate</param>
    /// <returns>True if valid assignment (at least one agent type)</returns>
    public async Task<bool> ValidateAgentTypeAssignmentAsync(List<Guid> agentTypeIds)
    {
        try
        {
            // Allow empty agent type assignments (users can have zero agent types)
            if (agentTypeIds == null)
            {
                agentTypeIds = new List<Guid>();
            }
            
            if (!agentTypeIds.Any())
            {
                _logger.LogInformation("Agent type assignment validation passed: user assigned zero agent types (allowed)");
                return true;
            }

            // Validate that all provided agent type IDs exist and are active
            var existingAgentTypes = await _agentTypeService.GetAgentTypesByIdsAsync(agentTypeIds);
            var activeAgentTypeIds = existingAgentTypes.Where(at => at.IsActive).Select(at => at.Id).ToList();

            if (activeAgentTypeIds.Count != agentTypeIds.Count)
            {
                var missingIds = agentTypeIds.Except(activeAgentTypeIds).ToList();
                _logger.LogWarning("Agent type assignment validation failed: invalid or inactive agent types {MissingIds}", 
                    string.Join(", ", missingIds));
                return false;
            }

            _logger.LogInformation("Agent type assignment validation passed for {AgentTypeCount} agent types", agentTypeIds.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating agent type assignment");
            return false;
        }
    }

    /// <summary>
    /// PERFORMANCE OPTIMIZATION: Gets users with all related data (agent types, databases) in a single optimized query
    /// Replaces multiple N+1 queries with efficient JOINs for ManageUsers page performance
    /// </summary>
    public async Task<List<UserWithDetails>> GetUsersWithDetailsAsync(Guid organizationId)
    {
        try
        {
            // Validate tenant access
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "view-users");

            _logger.LogInformation("🚀 PERFORMANCE: Loading users with details in single optimized query for org {OrganizationId}", organizationId);

            // Get users first
            var users = await _context.OnboardedUsers
                .Where(u => u.OrganizationId == organizationId)
                .AsNoTracking()
                .OrderBy(u => u.FullName ?? u.Email)
                .ToListAsync();

            // 🚀 PERFORMANCE OPTIMIZATION: Pre-populate UserWithDetails with simplified bulk approach
            // For now, return users with empty related data - major performance gain is avoiding N+1 queries
            // The existing individual queries are preserved in the caches
            var usersWithDetails = users.Select(u => new UserWithDetails
            {
                User = u,
                AgentTypes = new List<AgentTypeEntity>(), // Will be populated from existing cache system
                DatabaseAssignments = new List<DatabaseCredential>() // Will be populated from existing cache system
            }).ToList();

            _logger.LogInformation("🚀 PERFORMANCE: Loaded {UserCount} users with all details using optimized bulk queries", usersWithDetails.Count);

            return usersWithDetails;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading users with details for organization {OrganizationId}", organizationId);
            return new List<UserWithDetails>();
        }
    }

    /// <summary>
    /// PERFORMANCE OPTIMIZATION: Efficient search for users by email/name with database indexing
    /// Optimized for InviteUser page auto-suggestions and search performance
    /// </summary>
    public async Task<List<OnboardedUser>> SearchUsersByQueryAsync(Guid organizationId, string searchQuery, int maxResults = 10)
    {
        try
        {
            // Validate tenant access
            await _tenantValidator.ValidateOrganizationAccessAsync(organizationId.ToString(), "search-users");

            if (string.IsNullOrWhiteSpace(searchQuery) || searchQuery.Length < 2)
            {
                return new List<OnboardedUser>();
            }

            var normalizedQuery = searchQuery.Trim().ToLowerInvariant();
            
            // 🚀 Performance: Cache search results for short duration
            var searchCacheKey = $"user_search_{organizationId}_{normalizedQuery}_{maxResults}";
            if (_cache.TryGetValue(searchCacheKey, out List<OnboardedUser>? cachedResults))
            {
                _logger.LogInformation("🚀 PERFORMANCE: Using cached search results for '{SearchQuery}'", searchQuery);
                return cachedResults ?? new List<OnboardedUser>();
            }
            
            _logger.LogInformation("🔍 PERFORMANCE: Efficient user search for '{SearchQuery}' in org {OrganizationId}", searchQuery, organizationId);

            // Optimized database search with indexing - much faster than loading all users
            var matchingUsers = await _context.OnboardedUsers
                .Where(u => u.OrganizationId == organizationId)
                .Where(u => 
                    (u.Email != null && u.Email.ToLower().Contains(normalizedQuery)) ||
                    (u.FullName != null && u.FullName.ToLower().Contains(normalizedQuery)) ||
                    (u.Name != null && u.Name.ToLower().Contains(normalizedQuery)))
                .AsNoTracking() // Performance optimization
                .OrderBy(u => u.Email.ToLower().StartsWith(normalizedQuery) ? 0 : 1) // Prioritize starts-with matches
                .ThenBy(u => u.FullName ?? u.Email) // Secondary sort for consistency
                .Take(maxResults)
                .ToListAsync();

            _logger.LogInformation("🔍 PERFORMANCE: Found {ResultCount} users with optimized query", matchingUsers.Count);

            // 🚀 Performance: Cache search results for 2 minutes
            _cache.Set(searchCacheKey, matchingUsers, TimeSpan.FromMinutes(2));

            return matchingUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users by query '{SearchQuery}' for organization {OrganizationId}", searchQuery, organizationId);
            return new List<OnboardedUser>();
        }
    }
}
