using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing user assignments to agent-based security groups
/// This is purely additive to existing SecurityGroupService functionality
/// Existing organization-based group management remains unchanged
/// </summary>
public class AgentGroupAssignmentService : IAgentGroupAssignmentService
{
    private readonly AdminConsoleDbContext _context;
    private readonly IAgentTypeService _agentTypeService;
    private readonly IGraphService _graphService;
    private readonly ILogger<AgentGroupAssignmentService> _logger;

    public AgentGroupAssignmentService(
        AdminConsoleDbContext context,
        IAgentTypeService agentTypeService,
        IGraphService graphService,
        ILogger<AgentGroupAssignmentService> logger)
    {
        _context = context;
        _agentTypeService = agentTypeService;
        _graphService = graphService;
        _logger = logger;
    }

    public async Task<bool> AssignUserToAgentTypeGroupsAsync(string userId, List<Guid> agentTypeIds, Guid organizationId, string assignedBy)
    {
        try
        {
            _logger.LogInformation("Assigning user {UserId} to agent type groups for org {OrganizationId}", userId, organizationId);
            
            var successCount = 0;
            var assignments = new List<UserAgentTypeGroupAssignment>();

            foreach (var agentTypeId in agentTypeIds)
            {
                // Get the agent type to find its global security group ID
                var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
                if (agentType == null || string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                {
                    _logger.LogWarning("Agent type {AgentTypeId} not found or has no GlobalSecurityGroupId", agentTypeId);
                    continue;
                }

                // Check if assignment already exists
                var existingAssignment = await _context.UserAgentTypeGroupAssignments
                    .FirstOrDefaultAsync(a => a.UserId == userId 
                                            && a.AgentTypeId == agentTypeId 
                                            && a.OrganizationId == organizationId);

                if (existingAssignment != null)
                {
                    // Reactivate if it was deactivated
                    if (!existingAssignment.IsActive)
                    {
                        existingAssignment.Reactivate();
                        _logger.LogInformation("Reactivated existing assignment for user {UserId} to agent type {AgentTypeId}", userId, agentTypeId);
                    }
                    successCount++;
                    continue;
                }

                // Add user to Azure AD security group
                _logger.LogInformation("DEBUG: Attempting to add user {UserId} to security group {GroupId} (Object ID) for agent type {AgentTypeName}", 
                    userId, agentType.GlobalSecurityGroupId, agentType.Name);
                
                var addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                _logger.LogInformation("DEBUG: Add user to group result: {AddedToGroup}", addedToGroup);
                
                if (!addedToGroup)
                {
                    _logger.LogError("CRITICAL: Failed to add user {UserId} to security group {GroupId} for agent type {AgentTypeId}", 
                        userId, agentType.GlobalSecurityGroupId, agentTypeId);
                    continue;
                }

                // Create assignment record
                var assignment = UserAgentTypeGroupAssignmentExtensions.CreateAssignment(
                    userId, agentTypeId, agentType.GlobalSecurityGroupId, organizationId, assignedBy);
                
                assignments.Add(assignment);
                successCount++;
                
                _logger.LogInformation("Successfully assigned user {UserId} to agent type {AgentTypeId} security group {GroupId}", 
                    userId, agentTypeId, agentType.GlobalSecurityGroupId);
            }

            // Save all assignments to database
            if (assignments.Any())
            {
                _context.UserAgentTypeGroupAssignments.AddRange(assignments);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Completed agent group assignment for user {UserId}: {SuccessCount}/{TotalCount} successful", 
                userId, successCount, agentTypeIds.Count);

            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning user {UserId} to agent type groups", userId);
            return false;
        }
    }

    public async Task<bool> RemoveUserFromAgentTypeGroupsAsync(string userId, Guid organizationId)
    {
        try
        {
            _logger.LogInformation("Removing user {UserId} from all agent type groups for org {OrganizationId}", userId, organizationId);

            // Get all active assignments for this user in this organization
            var assignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && a.IsActive)
                .ToListAsync();

            var successCount = 0;

            foreach (var assignment in assignments)
            {
                // Remove from Azure AD security group
                var removedFromGroup = await _graphService.RemoveUserFromGroupAsync(userId, assignment.SecurityGroupId);
                if (removedFromGroup)
                {
                    // Deactivate the assignment (soft delete)
                    assignment.Deactivate();
                    successCount++;
                    
                    _logger.LogInformation("Removed user {UserId} from security group {GroupId} for agent type {AgentTypeId}", 
                        userId, assignment.SecurityGroupId, assignment.AgentTypeId);
                }
                else
                {
                    _logger.LogError("Failed to remove user {UserId} from security group {GroupId}", userId, assignment.SecurityGroupId);
                }
            }

            // Save changes to database
            if (assignments.Any())
            {
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Completed agent group removal for user {UserId}: {SuccessCount}/{TotalCount} successful", 
                userId, successCount, assignments.Count);

            return successCount == assignments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user {UserId} from agent type groups", userId);
            return false;
        }
    }

    public async Task<List<UserAgentTypeGroupAssignment>> GetUserAgentGroupAssignmentsAsync(string userId, Guid organizationId)
    {
        try
        {
            return await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && a.IsActive)
                .OrderBy(a => a.AssignedDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent group assignments for user {UserId}", userId);
            return new List<UserAgentTypeGroupAssignment>();
        }
    }

    public async Task<List<UserAgentTypeGroupAssignment>> GetUsersForAgentTypeAsync(Guid agentTypeId, Guid organizationId)
    {
        try
        {
            return await _context.UserAgentTypeGroupAssignments
                .Where(a => a.AgentTypeId == agentTypeId && a.OrganizationId == organizationId && a.IsActive)
                .OrderBy(a => a.AssignedDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users for agent type {AgentTypeId}", agentTypeId);
            return new List<UserAgentTypeGroupAssignment>();
        }
    }

    public async Task<bool> UpdateUserAgentTypeAssignmentsAsync(string userId, List<Guid> newAgentTypeIds, Guid organizationId, string modifiedBy)
    {
        try
        {
            // Get current assignments
            var currentAssignments = await GetUserAgentGroupAssignmentsAsync(userId, organizationId);
            var currentAgentTypeIds = currentAssignments.Select(a => a.AgentTypeId).ToList();

            // Find agent types to add and remove
            var agentTypesToAdd = newAgentTypeIds.Except(currentAgentTypeIds).ToList();
            var agentTypesToRemove = currentAgentTypeIds.Except(newAgentTypeIds).ToList();

            var success = true;

            // Add new agent type assignments
            if (agentTypesToAdd.Any())
            {
                success &= await AssignUserToAgentTypeGroupsAsync(userId, agentTypesToAdd, organizationId, modifiedBy);
            }

            // Remove old agent type assignments
            foreach (var agentTypeId in agentTypesToRemove)
            {
                var assignmentToDeactivate = currentAssignments.FirstOrDefault(a => a.AgentTypeId == agentTypeId);
                if (assignmentToDeactivate != null)
                {
                    // Remove from Azure AD group
                    var removedFromGroup = await _graphService.RemoveUserFromGroupAsync(userId, assignmentToDeactivate.SecurityGroupId);
                    if (removedFromGroup)
                    {
                        assignmentToDeactivate.Deactivate();
                    }
                    else
                    {
                        success = false;
                    }
                }
            }

            // Save changes
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated agent type assignments for user {UserId}: added {AddCount}, removed {RemoveCount}", 
                userId, agentTypesToAdd.Count, agentTypesToRemove.Count);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent type assignments for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ReactivateUserAgentGroupAssignmentsAsync(string userId, Guid organizationId)
    {
        try
        {
            // Get all inactive assignments for this user
            var inactiveAssignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && !a.IsActive)
                .ToListAsync();

            var successCount = 0;

            foreach (var assignment in inactiveAssignments)
            {
                // Re-add to Azure AD security group
                var addedToGroup = await _graphService.AddUserToGroupAsync(userId, assignment.SecurityGroupId);
                if (addedToGroup)
                {
                    assignment.Reactivate();
                    successCount++;
                }
            }

            // Save changes
            if (inactiveAssignments.Any())
            {
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Reactivated {SuccessCount}/{TotalCount} agent group assignments for user {UserId}", 
                successCount, inactiveAssignments.Count, userId);

            return successCount == inactiveAssignments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating agent group assignments for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> DeactivateUserAgentGroupAssignmentsAsync(string userId, Guid organizationId)
    {
        try
        {
            // Get all active assignments for this user
            var activeAssignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && a.IsActive)
                .ToListAsync();

            var successCount = 0;

            foreach (var assignment in activeAssignments)
            {
                // Remove from Azure AD security group
                var removedFromGroup = await _graphService.RemoveUserFromGroupAsync(userId, assignment.SecurityGroupId);
                if (removedFromGroup)
                {
                    assignment.Deactivate();
                    successCount++;
                }
            }

            // Save changes
            if (activeAssignments.Any())
            {
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Deactivated {SuccessCount}/{TotalCount} agent group assignments for user {UserId}", 
                successCount, activeAssignments.Count, userId);

            return successCount == activeAssignments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating agent group assignments for user {UserId}", userId);
            return false;
        }
    }
}