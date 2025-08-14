using AdminConsole.Models;
using AdminConsole.Data;
using Microsoft.EntityFrameworkCore;
using System.Security;

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
    private readonly IServiceProvider _serviceProvider;

    public TeamsGroupService(
        IGraphService graphService,
        IOrganizationService organizationService,
        AdminConsoleDbContext dbContext,
        ILogger<TeamsGroupService> logger,
        IServiceProvider serviceProvider)
    {
        _graphService = graphService;
        _organizationService = organizationService;
        _dbContext = dbContext;
        _logger = logger;
        _serviceProvider = serviceProvider;
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

            // Check if organization already has an M365GroupId (source of truth)
            if (!string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogInformation("✅ Organization already has M365GroupId: {GroupId} - validating it exists in Azure AD", 
                    organization.M365GroupId);
                
                // Validate that this group still exists in Azure AD
                var groupExists = await _graphService.GroupExistsAsync(organization.M365GroupId);
                if (groupExists)
                {
                    _logger.LogInformation("✅ Organization's M365GroupId is valid - ensuring database record exists");
                    
                    // Ensure we have a corresponding OrganizationTeamsGroups record
                    var existingRecord = await _dbContext.OrganizationTeamsGroups
                        .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);
                    
                    if (existingRecord == null)
                    {
                        // Create database record for existing Teams group
                        existingRecord = new OrganizationTeamsGroup
                        {
                            OrganizationId = organizationId,
                            AgentTypeId = Guid.Empty,
                            TeamsGroupId = organization.M365GroupId,
                            TeamName = OrganizationTeamsGroupExtensions.GenerateTeamName(organization.Name, "General"),
                            Description = OrganizationTeamsGroupExtensions.GenerateDescription(organization.Name, "General"),
                            IsActive = true,
                            CreatedDate = DateTime.UtcNow,
                            CreatedBy = createdBy.ToString(),
                            ModifiedDate = DateTime.UtcNow
                        };
                        
                        _dbContext.OrganizationTeamsGroups.Add(existingRecord);
                        await _dbContext.SaveChangesAsync();
                        
                        _logger.LogInformation("📝 Created database record for existing Teams group {GroupId}", organization.M365GroupId);
                    }
                    else
                    {
                        // Update record if Teams group ID is different (repair stale data)
                        if (existingRecord.TeamsGroupId != organization.M365GroupId)
                        {
                            _logger.LogWarning("🔧 Updating stale Teams group ID from {StaleId} to {CorrectId}",
                                existingRecord.TeamsGroupId, organization.M365GroupId);
                            existingRecord.TeamsGroupId = organization.M365GroupId;
                            existingRecord.ModifiedDate = DateTime.UtcNow;
                            await _dbContext.SaveChangesAsync();
                        }
                    }
                    
                    return existingRecord;
                }
                else
                {
                    _logger.LogWarning("❌ Organization's M365GroupId {GroupId} no longer exists in Azure AD - will create new group", 
                        organization.M365GroupId);
                }
            }
            
            // Check if we have an existing OrganizationTeamsGroups record that might be stale
            var existingGroup = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (existingGroup != null)
            {
                _logger.LogWarning("Found existing Teams group record {GroupId} but organization has no/invalid M365GroupId - will validate existing record", 
                    existingGroup.TeamsGroupId);
                
                var existingGroupStillExists = await _graphService.GroupExistsAsync(existingGroup.TeamsGroupId);
                if (existingGroupStillExists)
                {
                    _logger.LogInformation("✅ Existing Teams group record is valid - updating organization with M365GroupId");
                    
                    // Update organization with the correct M365GroupId (repair missing data)
                    organization.M365GroupId = existingGroup.TeamsGroupId;
                    // Note: Organization update would happen in calling service
                    
                    return existingGroup;
                }
                else
                {
                    _logger.LogWarning("❌ Existing Teams group record {GroupId} no longer exists in Azure AD - will create new group", 
                        existingGroup.TeamsGroupId);
                }
            }

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
            _logger.LogInformation("=== TEAMS GROUP ASSIGNMENT - USING ORGANIZATION M365GroupId DIRECTLY ===");
            _logger.LogInformation("Adding user {UserId} to organization {OrganizationId} Teams group", userId, organizationId);

            // Get organization details - this should have the correct M365GroupId
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("❌ Organization {OrganizationId} not found", organizationId);
                return false;
            }

            _logger.LogInformation("Organization: Name={Name}, M365GroupId={M365GroupId}", 
                organization.Name, organization.M365GroupId);

            // Use the organization's M365GroupId directly (this should be the correct one)
            if (string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogError("❌ Organization {OrganizationId} has no M365GroupId configured", organizationId);
                return false;
            }

            var teamsGroupId = organization.M365GroupId;
            _logger.LogInformation("✅ Using organization's M365GroupId directly: {GroupId}", teamsGroupId);

            // Validate that this group exists in Azure AD
            var groupExists = await _graphService.GroupExistsAsync(teamsGroupId);
            _logger.LogInformation("Teams group {GroupId} exists in Azure AD: {Exists}", teamsGroupId, groupExists);

            if (!groupExists)
            {
                _logger.LogError("❌ Teams group {GroupId} does not exist in Azure AD", teamsGroupId);
                return false;
            }

            // Add user to the Teams group using organization's M365GroupId
            _logger.LogInformation("Calling GraphService.AddUserToTeamsGroupAsync with UserId={UserId}, GroupId={GroupId}", 
                userId, teamsGroupId);
                
            var success = await _graphService.AddUserToTeamsGroupAsync(userId, teamsGroupId);
            
            if (success)
            {
                _logger.LogInformation("✅ Successfully added user {UserId} to organization Teams group {GroupId}", 
                    userId, teamsGroupId);
            }
            else
            {
                _logger.LogError("❌ Failed to add user {UserId} to organization Teams group {GroupId}", 
                    userId, teamsGroupId);
            }

            _logger.LogInformation("=== TEAMS GROUP ASSIGNMENT RESULT: {Success} ===", success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exception adding user {UserId} to organization {OrganizationId} Teams group", 
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
            _logger.LogInformation("=== TEAMS GROUP REMOVAL - USING ORGANIZATION M365GroupId DIRECTLY ===");
            _logger.LogInformation("Removing user {UserId} from organization {OrganizationId} Teams group", userId, organizationId);

            // Get organization details - use M365GroupId directly
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("❌ Organization {OrganizationId} not found", organizationId);
                return false;
            }

            // Use the organization's M365GroupId directly
            if (string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogError("❌ Organization {OrganizationId} has no M365GroupId configured", organizationId);
                return false;
            }

            var teamsGroupId = organization.M365GroupId;
            _logger.LogInformation("✅ Using organization's M365GroupId directly: {GroupId}", teamsGroupId);

            // Check if the group exists before trying to remove user (handle stale group IDs)
            var groupExists = await _graphService.GroupExistsAsync(teamsGroupId);
            _logger.LogInformation("Teams group {GroupId} exists in Azure AD: {Exists}", teamsGroupId, groupExists);

            bool success = false;
            if (!groupExists)
            {
                _logger.LogWarning("⚠️ STALE GROUP ID: Teams group {GroupId} does not exist in Azure AD. Treating removal as successful since group is already gone.", teamsGroupId);
                success = true; // Treat as success since group doesn't exist
            }
            else
            {
                // Remove user from the Teams group using organization's M365GroupId
                success = await _graphService.RemoveUserFromTeamsGroupAsync(userId, teamsGroupId);
                
                if (success)
                {
                    _logger.LogInformation("✅ Removed user {UserId} from organization Teams group {GroupId}", 
                        userId, teamsGroupId);
                }
                else
                {
                    _logger.LogError("❌ Failed to remove user {UserId} from organization Teams group {GroupId}", 
                        userId, teamsGroupId);
                }
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
    /// Gets the Teams group for an organization - Updated to use Organization.M365GroupId as source of truth
    /// </summary>
    public async Task<OrganizationTeamsGroup?> GetOrganizationTeamsGroupAsync(Guid organizationId)
    {
        try
        {
            _logger.LogInformation("=== GET TEAMS GROUP - USING ORGANIZATION M365GroupId AS SOURCE OF TRUTH ===");
            _logger.LogInformation("Getting Teams group for organization {OrganizationId}", organizationId);

            // Get organization details - this is the source of truth for M365GroupId
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("❌ Organization {OrganizationId} not found", organizationId);
                return null;
            }

            _logger.LogInformation("Organization: Name={Name}, M365GroupId={M365GroupId}", 
                organization.Name, organization.M365GroupId);

            if (string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogWarning("❌ Organization {OrganizationId} has no M365GroupId configured", organizationId);
                return null;
            }

            var teamsGroupId = organization.M365GroupId;
            _logger.LogInformation("✅ Using organization's M365GroupId as Teams group ID: {GroupId}", teamsGroupId);

            // Get existing Teams group record (for metadata) but validate against Organization.M365GroupId
            var teamsGroupRecord = await _dbContext.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroupRecord != null)
            {
                // Validate and repair if needed
                if (teamsGroupRecord.TeamsGroupId != teamsGroupId)
                {
                    _logger.LogWarning("🔧 Teams group record has stale ID: {StaleId}, updating to source of truth: {CorrectId}",
                        teamsGroupRecord.TeamsGroupId, teamsGroupId);
                    teamsGroupRecord.TeamsGroupId = teamsGroupId;
                    teamsGroupRecord.ModifiedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
                return teamsGroupRecord;
            }
            else
            {
                _logger.LogInformation("No existing Teams group record found, creating minimal record with correct M365GroupId");
                
                // Create a minimal Teams group record with the correct M365GroupId
                var newTeamsGroup = new OrganizationTeamsGroup
                {
                    OrganizationId = organizationId,
                    AgentTypeId = Guid.Empty,
                    TeamsGroupId = teamsGroupId,
                    TeamName = $"{organization.Name} Team",
                    Description = $"Teams workspace for {organization.Name}",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = "system",
                    ModifiedDate = DateTime.UtcNow
                };

                _dbContext.OrganizationTeamsGroups.Add(newTeamsGroup);
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("✅ Created Teams group record with source-of-truth M365GroupId");
                return newTeamsGroup;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Teams group for organization {OrganizationId}", organizationId);
            return null;
        }
    }

    /// <summary>
    /// Gets all members of an organization's Teams group - Updated to use Organization.M365GroupId as source of truth
    /// </summary>
    public async Task<List<string>> GetOrganizationTeamsGroupMembersAsync(Guid organizationId)
    {
        try
        {
            _logger.LogInformation("=== GET TEAMS GROUP MEMBERS - USING ORGANIZATION M365GroupId AS SOURCE OF TRUTH ===");
            _logger.LogInformation("Getting Teams group members for organization {OrganizationId}", organizationId);

            // Get organization details - this is the source of truth for M365GroupId
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("❌ Organization {OrganizationId} not found", organizationId);
                return new List<string>();
            }

            _logger.LogInformation("Organization: Name={Name}, M365GroupId={M365GroupId}", 
                organization.Name, organization.M365GroupId);

            if (string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogWarning("❌ Organization {OrganizationId} has no M365GroupId configured", organizationId);
                return new List<string>();
            }

            var teamsGroupId = organization.M365GroupId;
            _logger.LogInformation("✅ Using organization's M365GroupId to get members: {GroupId}", teamsGroupId);

            // Get members directly using the organization's M365GroupId
            var members = await _graphService.GetTeamsGroupMembersAsync(teamsGroupId);
            _logger.LogInformation("Found {MemberCount} members in Teams group {GroupId}", members.Count, teamsGroupId);
            
            return members;
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
    /// 🚨 SECURITY LOCKDOWN: Teams group deletion disabled for security
    /// This method is PERMANENTLY DISABLED to prevent accidental group deletion
    /// </summary>
    public async Task<bool> DeleteOrganizationTeamsGroupAsync(Guid organizationId)
    {
        // 🚨 CRITICAL SECURITY BLOCK: Prevent any Teams group deletion at service level
        _logger.LogCritical("🚨 SECURITY ALERT: DeleteOrganizationTeamsGroupAsync called for org {OrganizationId} - OPERATION BLOCKED", organizationId);
        _logger.LogCritical("🔒 SECURITY: Teams group deletion is PERMANENTLY DISABLED to prevent accidental deletion");
        _logger.LogCritical("📋 MANUAL ACTION REQUIRED: If you need to delete organization's Teams group, do it manually in Azure Portal");
        
        var stackTrace = System.Environment.StackTrace;
        _logger.LogCritical("🕵️ CALL STACK for blocked Teams group deletion:\n{StackTrace}", stackTrace);
        
        throw new SecurityException($"🚨 SECURITY LOCKDOWN: Teams group deletion for organization {organizationId} is PERMANENTLY BLOCKED for security reasons. All group deletions must be performed manually in Azure Portal.");
        
        /* ORIGINAL DANGEROUS CODE - PERMANENTLY DISABLED
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
        */
    }
}