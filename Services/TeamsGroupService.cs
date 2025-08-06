using AdminConsole.Models;
using AdminConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing Microsoft Teams groups per organization and agent type
/// Handles automatic Teams group creation and user management
/// </summary>
public class TeamsGroupService : ITeamsGroupService
{
    private readonly IGraphService _graphService;
    private readonly IOrganizationService _organizationService;
    private readonly AdminConsoleDbContext _dbContext;
    private readonly ILogger<TeamsGroupService> _logger;

    public TeamsGroupService(
        IGraphService graphService,
        IOrganizationService organizationService,
        AdminConsoleDbContext dbContext,
        ILogger<TeamsGroupService> logger)
    {
        _graphService = graphService;
        _organizationService = organizationService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates or ensures a Teams group exists for an organization
    /// This is called when an org admin is first invited
    /// </summary>
    public async Task<OrganizationTeamsGroup?> CreateOrganizationTeamsGroupAsync(Guid organizationId, Guid createdBy)
    {
        try
        {
            // Get organization details
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("Organization {OrganizationId} not found when creating Teams group", organizationId);
                return null;
            }

            // Check if Teams group already exists for this organization
            var existingGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (existingGroup != null)
            {
                _logger.LogInformation("Teams group {GroupId} already exists for organization {OrganizationId}", 
                    existingGroup.TeamsGroupId, organizationId);
                return existingGroup;
            }

            // Generate team name and description
            var teamName = OrganizationTeamsGroupExtensions.GenerateTeamName(organization.Name, "General");
            var description = OrganizationTeamsGroupExtensions.GenerateDescription(organization.Name, "General");

            // Create the Teams group via Graph API
            _logger.LogInformation("DEBUG: Creating Teams group via GraphService - Name: {TeamName}, Description: {Description}", 
                teamName, description);
                
            var teamsResult = await _graphService.CreateTeamsGroupAsync(teamName, description, organizationId.ToString());
            _logger.LogInformation("DEBUG: Teams group creation result - Success: {Success}, GroupId: {GroupId}, Errors: {Errors}", 
                teamsResult.Success, teamsResult.GroupId, string.Join(", ", teamsResult.Errors));
            
            if (!teamsResult.Success || string.IsNullOrEmpty(teamsResult.GroupId))
            {
                _logger.LogError("CRITICAL: Failed to create Teams group for organization {OrganizationId}. Errors: {Errors}", 
                    organizationId, string.Join(", ", teamsResult.Errors));
                return null;
            }

            // Save the Teams group information to database
            var organizationTeamsGroup = new OrganizationTeamsGroup
            {
                OrganizationId = organizationId,
                AgentTypeId = Guid.Empty, // General organization group, not agent-specific
                TeamsGroupId = teamsResult.GroupId,
                TeamName = teamName,
                TeamUrl = teamsResult.TeamUrl,
                Description = description,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = createdBy.ToString(),
                ModifiedDate = DateTime.UtcNow
            };

            _dbContext.OrganizationTeamsGroups.Add(organizationTeamsGroup);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Created Teams group {GroupId} for organization {OrganizationId}", 
                teamsResult.GroupId, organizationId);

            return organizationTeamsGroup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Teams group for organization {OrganizationId}", organizationId);
            return null;
        }
    }

    /// <summary>
    /// Adds a user to the organization's Teams group
    /// </summary>
    public async Task<bool> AddUserToOrganizationTeamsGroupAsync(string userId, Guid organizationId)
    {
        try
        {
            // Get the organization's Teams group
            var teamsGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroup == null)
            {
                _logger.LogWarning("No Teams group found for organization {OrganizationId} when adding user {UserId}", 
                    organizationId, userId);
                return false;
            }

            // Add user to the Teams group
            var success = await _graphService.AddUserToTeamsGroupAsync(userId, teamsGroup.TeamsGroupId);
            
            if (success)
            {
                _logger.LogInformation("Added user {UserId} to organization Teams group {GroupId}", 
                    userId, teamsGroup.TeamsGroupId);
            }
            else
            {
                _logger.LogError("Failed to add user {UserId} to organization Teams group {GroupId}", 
                    userId, teamsGroup.TeamsGroupId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user {UserId} to organization {OrganizationId} Teams group", 
                userId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// Removes a user from the organization's Teams group
    /// </summary>
    public async Task<bool> RemoveUserFromOrganizationTeamsGroupAsync(string userId, Guid organizationId)
    {
        try
        {
            // Get the organization's Teams group
            var teamsGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroup == null)
            {
                _logger.LogWarning("No Teams group found for organization {OrganizationId} when removing user {UserId}", 
                    organizationId, userId);
                return false;
            }

            // Remove user from the Teams group
            var success = await _graphService.RemoveUserFromTeamsGroupAsync(userId, teamsGroup.TeamsGroupId);
            
            if (success)
            {
                _logger.LogInformation("Removed user {UserId} from organization Teams group {GroupId}", 
                    userId, teamsGroup.TeamsGroupId);
            }
            else
            {
                _logger.LogWarning("Failed to remove user {UserId} from organization Teams group {GroupId}", 
                    userId, teamsGroup.TeamsGroupId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user {UserId} from organization {OrganizationId} Teams group", 
                userId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// Gets the Teams group for an organization
    /// </summary>
    public async Task<OrganizationTeamsGroup?> GetOrganizationTeamsGroupAsync(Guid organizationId)
    {
        try
        {
            return await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Teams group for organization {OrganizationId}", organizationId);
            return null;
        }
    }

    /// <summary>
    /// Gets all members of an organization's Teams group
    /// </summary>
    public async Task<List<string>> GetOrganizationTeamsGroupMembersAsync(Guid organizationId)
    {
        try
        {
            var teamsGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroup == null)
            {
                _logger.LogWarning("No Teams group found for organization {OrganizationId}", organizationId);
                return new List<string>();
            }

            return await _graphService.GetTeamsGroupMembersAsync(teamsGroup.TeamsGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Teams group members for organization {OrganizationId}", organizationId);
            return new List<string>();
        }
    }

    /// <summary>
    /// Ensures organization has a Teams group setup
    /// Called during organization setup or maintenance operations
    /// </summary>
    public async Task<bool> EnsureOrganizationTeamsGroupAsync(Guid organizationId, Guid createdBy)
    {
        try
        {
            var existingGroup = await GetOrganizationTeamsGroupAsync(organizationId);
            
            if (existingGroup != null)
            {
                _logger.LogDebug("Teams group already exists for organization {OrganizationId}", organizationId);
                return true;
            }

            // Create the Teams group
            var teamsGroup = await CreateOrganizationTeamsGroupAsync(organizationId, createdBy);
            return teamsGroup != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring Teams group for organization {OrganizationId}", organizationId);
            return false;
        }
    }

    /// <summary>
    /// Deletes organization's Teams group (use with caution)
    /// </summary>
    public async Task<bool> DeleteOrganizationTeamsGroupAsync(Guid organizationId)
    {
        try
        {
            var teamsGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroup == null)
            {
                _logger.LogWarning("No Teams group found for organization {OrganizationId} to delete", organizationId);
                return false;
            }

            // Get members first for logging
            var members = await _graphService.GetTeamsGroupMembersAsync(teamsGroup.TeamsGroupId);
            if (members.Count > 0)
            {
                _logger.LogWarning("Deleting Teams group {GroupId} with {MemberCount} members", 
                    teamsGroup.TeamsGroupId, members.Count);
            }

            // Delete from Microsoft Teams/Graph API
            var success = await _graphService.DeleteTeamsGroupAsync(teamsGroup.TeamsGroupId);
            
            if (success)
            {
                // Mark as inactive in database
                teamsGroup.IsActive = false;
                teamsGroup.ModifiedDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Successfully deleted Teams group {GroupId} for organization {OrganizationId}", 
                    teamsGroup.TeamsGroupId, organizationId);
            }
            else
            {
                _logger.LogWarning("Failed to delete Teams group {GroupId} via Graph API", teamsGroup.TeamsGroupId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Teams group for organization {OrganizationId}", organizationId);
            return false;
        }
    }
}