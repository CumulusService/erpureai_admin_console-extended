using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Service to detect and repair stale group IDs during user reactivation
/// Handles cases where Azure AD groups have been deleted/recreated but database still has old IDs
/// </summary>
public interface IGroupRepairService
{
    /// <summary>
    /// Validates and repairs Teams group references for an organization
    /// </summary>
    Task<string?> ValidateAndRepairTeamsGroupAsync(Guid organizationId);
    
    /// <summary>
    /// Validates and repairs security group references for user agent assignments
    /// </summary>
    Task<List<string>> ValidateAndRepairSecurityGroupsAsync(string userId, Guid organizationId);
}

public class GroupRepairService : IGroupRepairService
{
    private readonly AdminConsoleDbContext _context;
    private readonly IGraphService _graphService;
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<GroupRepairService> _logger;

    public GroupRepairService(
        AdminConsoleDbContext context,
        IGraphService graphService,
        IOrganizationService organizationService,
        ILogger<GroupRepairService> logger)
    {
        _context = context;
        _graphService = graphService;
        _organizationService = organizationService;
        _logger = logger;
    }

    /// <summary>
    /// Validates and repairs Teams group references for an organization
    /// Returns the correct Teams group ID to use, or null if repair failed
    /// </summary>
    public async Task<string?> ValidateAndRepairTeamsGroupAsync(Guid organizationId)
    {
        try
        {
            _logger.LogInformation("=== TEAMS GROUP REPAIR for Organization {OrganizationId} ===", organizationId);

            // Get organization details
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogError("Organization {OrganizationId} not found for Teams group repair", organizationId);
                return null;
            }

            _logger.LogInformation("Organization: Name={Name}, M365GroupId={M365GroupId}", 
                organization.Name, organization.M365GroupId);

            // Get current Teams group record from database
            var teamsGroup = await _context.OrganizationTeamsGroups
                .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsActive);

            if (teamsGroup != null)
            {
                _logger.LogInformation("Database Teams group record: Id={TeamsGroupId}, Active={IsActive}", 
                    teamsGroup.TeamsGroupId, teamsGroup.IsActive);

                // Validate if this Teams group still exists in Azure AD
                var groupExists = await _graphService.GroupExistsAsync(teamsGroup.TeamsGroupId);
                _logger.LogInformation("Teams group {GroupId} exists in Azure AD: {Exists}", 
                    teamsGroup.TeamsGroupId, groupExists);

                if (groupExists)
                {
                    // Group exists, use it
                    return teamsGroup.TeamsGroupId;
                }
                else
                {
                    _logger.LogWarning("❌ Teams group {GroupId} in database no longer exists in Azure AD - needs repair", 
                        teamsGroup.TeamsGroupId);
                }
            }

            // The Organizations table should have the correct M365GroupId
            if (!string.IsNullOrEmpty(organization.M365GroupId))
            {
                _logger.LogInformation("🔍 Organization's M365GroupId from database: {M365GroupId}", organization.M365GroupId);
                
                // Validate that the organization's M365GroupId exists in Azure AD
                var orgGroupExists = await _graphService.GroupExistsAsync(organization.M365GroupId);
                _logger.LogInformation("Organization M365Group {GroupId} exists in Azure AD: {Exists}", 
                    organization.M365GroupId, orgGroupExists);

                if (orgGroupExists)
                {
                    // The organization has the correct M365GroupId - update OrganizationTeamsGroups table
                    _logger.LogInformation("✅ Organization has correct M365GroupId {GroupId} - updating Teams group record", 
                        organization.M365GroupId);

                    if (teamsGroup != null)
                    {
                        // Update existing record with correct Teams group ID
                        _logger.LogInformation("📝 Updating stale Teams group record from {OldGroupId} to {NewGroupId}",
                            teamsGroup.TeamsGroupId, organization.M365GroupId);
                        teamsGroup.TeamsGroupId = organization.M365GroupId;
                        teamsGroup.ModifiedDate = DateTime.UtcNow;
                        _logger.LogInformation("📝 Updated existing Teams group record with correct ID");
                    }
                    else
                    {
                        // Create new Teams group record with correct ID
                        teamsGroup = new OrganizationTeamsGroup
                        {
                            OrganizationId = organizationId,
                            AgentTypeId = await GetDefaultAgentTypeIdAsync(),
                            TeamsGroupId = organization.M365GroupId,
                            TeamName = $"{organization.Name} Team",
                            Description = $"Teams workspace for {organization.Name}",
                            IsActive = true
                        };
                        
                        await _context.OrganizationTeamsGroups.AddAsync(teamsGroup);
                        _logger.LogInformation("📝 Created new Teams group record with correct ID from Organizations table");
                    }

                    await _context.SaveChangesAsync();
                    return organization.M365GroupId;
                }
                else
                {
                    _logger.LogError("❌ Organization's M365GroupId {GroupId} does not exist in Azure AD - this is unexpected!", 
                        organization.M365GroupId);
                }
            }
            else
            {
                _logger.LogError("❌ Organization {OrganizationId} has no M365GroupId in database - this should not happen", 
                    organizationId);
            }

            _logger.LogError("❌ Could not find or repair valid Teams group for organization {OrganizationId}", 
                organizationId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Teams group repair for organization {OrganizationId}", organizationId);
            return null;
        }
    }

    /// <summary>
    /// Validates and repairs security group references for user agent assignments
    /// Returns list of valid security group IDs that can be used for reactivation
    /// </summary>
    public async Task<List<string>> ValidateAndRepairSecurityGroupsAsync(string userId, Guid organizationId)
    {
        var validSecurityGroups = new List<string>();

        try
        {
            _logger.LogInformation("=== SECURITY GROUP REPAIR for User {UserId} in Org {OrganizationId} ===", 
                userId, organizationId);

            // Get user's agent type assignments
            var assignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId)
                .ToListAsync();

            _logger.LogInformation("Found {Count} agent group assignments to validate", assignments.Count);

            foreach (var assignment in assignments)
            {
                _logger.LogInformation("Validating assignment: AgentTypeId={AgentTypeId}, SecurityGroupId={SecurityGroupId}, Active={IsActive}", 
                    assignment.AgentTypeId, assignment.SecurityGroupId, assignment.IsActive);

                if (string.IsNullOrEmpty(assignment.SecurityGroupId))
                {
                    _logger.LogWarning("❌ Assignment {AssignmentId} has null/empty SecurityGroupId - attempting repair", 
                        assignment.Id);

                    // Try to get security group ID from AgentType
                    var agentType = await _context.AgentTypes.FindAsync(assignment.AgentTypeId);
                    if (agentType != null && !string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                    {
                        _logger.LogInformation("🔧 Repairing assignment with AgentType security group: {SecurityGroupId}", 
                            agentType.GlobalSecurityGroupId);
                        
                        assignment.SecurityGroupId = agentType.GlobalSecurityGroupId;
                        assignment.ModifiedDate = DateTime.UtcNow;
                    }
                    else
                    {
                        _logger.LogError("❌ Could not repair assignment - AgentType {AgentTypeId} has no GlobalSecurityGroupId", 
                            assignment.AgentTypeId);
                        continue;
                    }
                }

                // Validate security group exists in Azure AD
                var groupExists = await _graphService.GroupExistsAsync(assignment.SecurityGroupId);
                _logger.LogInformation("Security group {SecurityGroupId} exists in Azure AD: {Exists}", 
                    assignment.SecurityGroupId, groupExists);

                if (groupExists)
                {
                    validSecurityGroups.Add(assignment.SecurityGroupId);
                    _logger.LogInformation("✅ Security group {SecurityGroupId} is valid", assignment.SecurityGroupId);
                }
                else
                {
                    _logger.LogError("❌ Security group {SecurityGroupId} does not exist in Azure AD", 
                        assignment.SecurityGroupId);
                }
            }

            // Save any repairs made
            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("💾 Saved security group assignment repairs");
            }

            _logger.LogInformation("=== SECURITY GROUP REPAIR SUMMARY ===");
            _logger.LogInformation("Total assignments: {Total}", assignments.Count);
            _logger.LogInformation("Valid security groups: {Valid}", validSecurityGroups.Count);

            return validSecurityGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during security group repair for user {UserId}", userId);
            return validSecurityGroups;
        }
    }

    private async Task<Guid> GetDefaultAgentTypeIdAsync()
    {
        var defaultAgentType = await _context.AgentTypes
            .Where(at => at.IsActive)
            .FirstOrDefaultAsync();

        return defaultAgentType?.Id ?? Guid.Empty;
    }
}