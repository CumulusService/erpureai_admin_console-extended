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
    private readonly IServiceProvider _serviceProvider;
    private readonly IStateValidationService _stateValidationService;
    private readonly IOperationStatusService _operationStatusService;

    public AgentGroupAssignmentService(
        AdminConsoleDbContext context,
        IAgentTypeService agentTypeService,
        IGraphService graphService,
        ILogger<AgentGroupAssignmentService> logger,
        IServiceProvider serviceProvider,
        IStateValidationService stateValidationService,
        IOperationStatusService operationStatusService)
    {
        _context = context;
        _agentTypeService = agentTypeService;
        _graphService = graphService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _stateValidationService = stateValidationService;
        _operationStatusService = operationStatusService;
    }

    public async Task<bool> AssignUserToAgentTypeGroupsAsync(string userId, List<Guid> agentTypeIds, Guid organizationId, string assignedBy)
    {
        var operationId = Guid.NewGuid().ToString();
        await _operationStatusService.StartOperationAsync(operationId, "AgentTypeAssignment", $"Assigning user to {agentTypeIds.Count} agent types");
        
        try
        {
            await _operationStatusService.UpdateStatusAsync(operationId, "Validating current state...");
            _logger.LogInformation("Assigning user {UserId} to agent type groups for org {OrganizationId}", userId, organizationId);
            
            // Pre-operation validation
            var preValidation = await _stateValidationService.ValidateUserStateConsistencyAsync(userId, organizationId);
            if (!preValidation.IsValid)
            {
                _logger.LogWarning("Pre-assignment validation found issues: {Issues}", string.Join(", ", preValidation.Errors));
                await _operationStatusService.UpdateStatusAsync(operationId, "Pre-validation warnings detected", string.Join(", ", preValidation.Warnings));
            }
            
            await _operationStatusService.UpdateStatusAsync(operationId, "Setting up agent type assignments...");
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

                // COMPREHENSIVE BIDIRECTIONAL GROUP ASSIGNMENT with membership validation
                _logger.LogInformation("🔄 ASSIGNMENT: Adding user {UserId} to security group {GroupId} for agent type {AgentTypeName}", 
                    userId, agentType.GlobalSecurityGroupId, agentType.Name);
                
                // DIAGNOSTIC: Log all the parameters being used
                _logger.LogInformation("🔍 DIAGNOSTIC: User ID type: {UserIdType}, Length: {UserIdLength}, Format: {UserIdFormat}", 
                    Guid.TryParse(userId, out _) ? "GUID" : "OTHER", userId.Length, userId);
                _logger.LogInformation("🔍 DIAGNOSTIC: Group ID type: {GroupIdType}, Length: {GroupIdLength}, Format: {GroupIdFormat}", 
                    Guid.TryParse(agentType.GlobalSecurityGroupId, out _) ? "GUID" : "OTHER", agentType.GlobalSecurityGroupId.Length, agentType.GlobalSecurityGroupId);
                
                // Step 1: Check if group exists
                bool groupExists = false;
                try
                {
                    groupExists = await _graphService.GroupExistsAsync(agentType.GlobalSecurityGroupId);
                    if (!groupExists)
                    {
                        _logger.LogWarning("⚠️ STALE GROUP ID: Security group {GroupId} for agent type '{AgentTypeName}' does not exist in Azure AD. Skipping group assignment but creating database record.", 
                            agentType.GlobalSecurityGroupId, agentType.Name);
                    }
                }
                catch (Exception groupCheckEx)
                {
                    _logger.LogWarning(groupCheckEx, "Could not verify group existence for {GroupId}, proceeding with assignment attempt", agentType.GlobalSecurityGroupId);
                    groupExists = true; // Assume it exists and try anyway
                }

                bool addedToGroup = false;
                if (groupExists)
                {
                    // Step 2: Check if user is already a member before attempting to add
                    var userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
                    var isAlreadyMember = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                    
                    if (isAlreadyMember)
                    {
                        _logger.LogInformation("✅ User {UserId} is already a member of security group {GroupId} - treating as successful assignment", 
                            userId, agentType.GlobalSecurityGroupId);
                        addedToGroup = true;
                    }
                    else
                    {
                        // User is not a member - proceed with addition
                        addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                        _logger.LogInformation("🔄 Add user to group result: {AddedToGroup}", addedToGroup);
                        
                        if (!addedToGroup)
                        {
                            _logger.LogError("❌ CRITICAL: Failed to add user {UserId} to security group {GroupId} for agent type {AgentTypeId}", 
                                userId, agentType.GlobalSecurityGroupId, agentTypeId);
                            continue;
                        }
                        else
                        {
                            _logger.LogInformation("✅ Successfully added user {UserId} to security group {GroupId}", userId, agentType.GlobalSecurityGroupId);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ Skipping Azure AD group assignment for agent type '{AgentTypeName}' due to non-existent group {GroupId}", 
                        agentType.Name, agentType.GlobalSecurityGroupId);
                    // Continue to create database record even if group doesn't exist
                    addedToGroup = true; // Treat as success for database record creation
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
                await _operationStatusService.UpdateStatusAsync(operationId, "Saving assignments to database...");
                _context.UserAgentTypeGroupAssignments.AddRange(assignments);
                await _context.SaveChangesAsync();
            }

            // Post-operation validation
            await _operationStatusService.UpdateStatusAsync(operationId, "Validating final state...");
            var postValidation = await _stateValidationService.ValidateUserStateConsistencyAsync(userId, organizationId);
            if (!postValidation.IsValid)
            {
                _logger.LogError("Post-assignment validation failed: {Issues}", string.Join(", ", postValidation.Errors));
                await _operationStatusService.CompleteOperationAsync(operationId, false, 
                    $"Assignment completed but validation failed: {string.Join(", ", postValidation.Errors)}");
                return false;
            }

            _logger.LogInformation("Completed agent group assignment for user {UserId}: {SuccessCount}/{TotalCount} successful", 
                userId, successCount, agentTypeIds.Count);

            await _operationStatusService.CompleteOperationAsync(operationId, successCount > 0, 
                $"Successfully assigned {successCount}/{agentTypeIds.Count} agent types");

            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning user {UserId} to agent type groups", userId);
            await _operationStatusService.CompleteOperationAsync(operationId, false, $"Assignment failed: {ex.Message}");
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
                // Remove from Azure AD security group with stale group handling
                bool removedFromGroup = false;
                try
                {
                    // Check if group exists first
                    bool groupExists = await _graphService.GroupExistsAsync(assignment.SecurityGroupId);
                    if (groupExists)
                    {
                        removedFromGroup = await _graphService.RemoveUserFromGroupAsync(userId, assignment.SecurityGroupId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ STALE GROUP ID: Security group {GroupId} does not exist in Azure AD during removal. Treating as successful.", 
                            assignment.SecurityGroupId);
                        removedFromGroup = true; // Treat as success since group doesn't exist
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Exception during group removal for {GroupId}, treating as successful and deactivating database record", 
                        assignment.SecurityGroupId);
                    removedFromGroup = true; // Continue with database deactivation
                }
                
                if (removedFromGroup)
                {
                    // Deactivate the assignment (soft delete)
                    assignment.Deactivate();
                    successCount++;
                    
                    _logger.LogInformation("✅ Removed user {UserId} from security group {GroupId} for agent type {AgentTypeId}", 
                        userId, assignment.SecurityGroupId, assignment.AgentTypeId);
                }
                else
                {
                    // CRITICAL FIX: Still deactivate the database record even if Azure removal fails
                    _logger.LogWarning("⚠️ Failed to remove user {UserId} from Azure security group {GroupId} (group may not exist), but deactivating database record anyway", 
                        userId, assignment.SecurityGroupId);
                    assignment.Deactivate();
                    successCount++;
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
            // ENHANCED DEBUGGING: Log all input parameters
            _logger.LogInformation("🔍 AGENT ASSIGNMENT DEBUG: Starting update for user {UserId}", userId);
            _logger.LogInformation("🔍 INPUT: newAgentTypeIds = [{AgentTypeIds}] (Count: {Count})", 
                string.Join(", ", newAgentTypeIds), newAgentTypeIds.Count);
            _logger.LogInformation("🔍 INPUT: organizationId = {OrganizationId}, modifiedBy = {ModifiedBy}", 
                organizationId, modifiedBy);

            // Get current assignments
            var currentAssignments = await GetUserAgentGroupAssignmentsAsync(userId, organizationId);
            var currentAgentTypeIds = currentAssignments.Select(a => a.AgentTypeId).ToList();

            // ENHANCED DEBUGGING: Log current state
            _logger.LogInformation("🔍 CURRENT STATE: Found {Count} existing assignments: [{AgentTypeIds}]", 
                currentAgentTypeIds.Count, string.Join(", ", currentAgentTypeIds));

            // Find agent types to add and remove
            var agentTypesToAdd = newAgentTypeIds.Except(currentAgentTypeIds).ToList();
            var agentTypesToRemove = currentAgentTypeIds.Except(newAgentTypeIds).ToList();

            // ENHANCED DEBUGGING: Log calculated changes
            _logger.LogInformation("🔍 CALCULATED CHANGES:");
            _logger.LogInformation("   - Agent types to ADD: [{AgentTypesToAdd}] (Count: {AddCount})", 
                string.Join(", ", agentTypesToAdd), agentTypesToAdd.Count);
            _logger.LogInformation("   - Agent types to REMOVE: [{AgentTypesToRemove}] (Count: {RemoveCount})", 
                string.Join(", ", agentTypesToRemove), agentTypesToRemove.Count);

            // CRITICAL FIX: Bidirectional sync validation
            // Even if database says user has assignments, verify they're actually in Azure AD groups
            _logger.LogInformation("🔄 BIDIRECTIONAL SYNC: Validating Azure AD membership for existing assignments");
            var azureValidationRequired = new List<Guid>();
            
            foreach (var assignment in currentAssignments)
            {
                var agentType = await _agentTypeService.GetByIdAsync(assignment.AgentTypeId);
                if (agentType != null && !string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                {
                    // Check if user is actually in the Azure AD group
                    try
                    {
                        var userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
                        var isInAzureGroup = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                        
                        if (!isInAzureGroup)
                        {
                            _logger.LogWarning("🚨 SYNC ISSUE: User has database assignment for {AgentTypeName} but is NOT in Azure AD group {GroupId}", 
                                agentType.Name, agentType.GlobalSecurityGroupId);
                            azureValidationRequired.Add(assignment.AgentTypeId);
                        }
                        else
                        {
                            _logger.LogInformation("✅ SYNC OK: User correctly in Azure AD group {GroupId} for {AgentTypeName}", 
                                agentType.GlobalSecurityGroupId, agentType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to validate Azure AD membership for {AgentTypeName}", agentType.Name);
                        azureValidationRequired.Add(assignment.AgentTypeId);
                    }
                }
            }

            var success = true;

            // CRITICAL FIX: Repair sync issues by adding users to Azure AD groups they should be in
            if (azureValidationRequired.Any())
            {
                _logger.LogInformation("🔧 SYNC REPAIR: Adding user to {Count} Azure AD groups they should already be in", azureValidationRequired.Count);
                foreach (var agentTypeId in azureValidationRequired)
                {
                    var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
                    if (agentType != null && !string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                    {
                        try
                        {
                            _logger.LogInformation("🔧 REPAIR: Adding user {UserId} to Azure AD group {GroupId} for {AgentTypeName}", 
                                userId, agentType.GlobalSecurityGroupId, agentType.Name);
                            
                            var addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                            if (addedToGroup)
                            {
                                _logger.LogInformation("✅ REPAIR SUCCESS: User {UserId} added to Azure AD group {GroupId}", userId, agentType.GlobalSecurityGroupId);
                            }
                            else
                            {
                                _logger.LogError("❌ REPAIR FAILED: Could not add user {UserId} to Azure AD group {GroupId}", userId, agentType.GlobalSecurityGroupId);
                                success = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ REPAIR EXCEPTION: Failed to add user {UserId} to Azure AD group {GroupId} for {AgentTypeName}", 
                                userId, agentType.GlobalSecurityGroupId, agentType.Name);
                            success = false;
                        }
                    }
                }
            }

            // Add new agent type assignments
            if (agentTypesToAdd.Any())
            {
                _logger.LogInformation("🔄 ADDING AGENT TYPES: Calling AssignUserToAgentTypeGroupsAsync for {Count} agent types", agentTypesToAdd.Count);
                var addResult = await AssignUserToAgentTypeGroupsAsync(userId, agentTypesToAdd, organizationId, modifiedBy);
                _logger.LogInformation("🔄 ADD RESULT: AssignUserToAgentTypeGroupsAsync returned {AddResult}", addResult);
                success &= addResult;
            }
            else
            {
                _logger.LogInformation("⏩ SKIPPING ADD: No agent types to add");
            }

            // Remove old agent type assignments
            if (agentTypesToRemove.Any())
            {
                _logger.LogInformation("🔄 REMOVING AGENT TYPES: Processing {Count} agent types for removal", agentTypesToRemove.Count);
            }
            else
            {
                _logger.LogInformation("⏩ SKIPPING REMOVE: No agent types to remove");
            }
            
            foreach (var agentTypeId in agentTypesToRemove)
            {
                var assignmentToDeactivate = currentAssignments.FirstOrDefault(a => a.AgentTypeId == agentTypeId);
                if (assignmentToDeactivate != null)
                {
                    // CRITICAL SOURCE-OF-TRUTH FIX: Use current AgentType.GlobalSecurityGroupId instead of stale database SecurityGroupId
                    var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
                    if (agentType == null || string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                    {
                        _logger.LogWarning("⚠️ SOURCE-OF-TRUTH: AgentType {AgentTypeId} not found or has no GlobalSecurityGroupId. Deactivating assignment without Azure AD removal.", agentTypeId);
                        assignmentToDeactivate.Deactivate();
                        continue;
                    }

                    // Log if we're fixing a stale group ID
                    if (assignmentToDeactivate.SecurityGroupId != agentType.GlobalSecurityGroupId)
                    {
                        _logger.LogWarning("🔧 SOURCE-OF-TRUTH REPAIR: Assignment had stale group ID {StaleGroupId}, using current ID {CurrentGroupId} from AgentType", 
                            assignmentToDeactivate.SecurityGroupId, agentType.GlobalSecurityGroupId);
                    }

                    // Check if user is actually a member of the group first
                    var userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
                    var isMember = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                    if (isMember)
                    {
                        // User is a member - proceed with removal
                        var removedFromGroup = await _graphService.RemoveUserFromGroupAsync(userId, agentType.GlobalSecurityGroupId);
                        if (removedFromGroup)
                        {
                            assignmentToDeactivate.Deactivate();
                            _logger.LogInformation("✅ Successfully removed user {UserId} from security group {GroupId} and deactivated assignment", userId, agentType.GlobalSecurityGroupId);
                        }
                        else
                        {
                            // CRITICAL: Always deactivate database record to prevent user access, even if Azure removal fails
                            assignmentToDeactivate.Deactivate();
                            _logger.LogWarning("⚠️ Azure removal failed but deactivated database assignment anyway for security - user {UserId} group {GroupId}", userId, agentType.GlobalSecurityGroupId);
                            success = false;
                        }
                    }
                    else
                    {
                        // User is not a member of the group (already removed/never was member) - just deactivate database record
                        assignmentToDeactivate.Deactivate();
                        _logger.LogInformation("✅ User {UserId} was not a member of security group {GroupId} - deactivated database assignment (user may have been previously unassigned)", userId, agentType.GlobalSecurityGroupId);
                    }
                }
            }

            // Save changes
            await _context.SaveChangesAsync();

            // ENHANCED DEBUGGING: Final summary with detailed results
            _logger.LogInformation("🎯 FINAL RESULT: Updated agent type assignments for user {UserId}: added {AddCount}, removed {RemoveCount}", 
                userId, agentTypesToAdd.Count, agentTypesToRemove.Count);
            _logger.LogInformation("🎯 SUMMARY: Operation success = {Success}", success);

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
            _logger.LogInformation("=== AGENT GROUP REACTIVATION - ENHANCED WITH MISSING ASSIGNMENT CREATION ===");
            _logger.LogInformation("Reactivating agent group assignments for User {UserId} in Org {OrganizationId}", userId, organizationId);

            // ENHANCED: Get user's original agent type selections from OnboardedUsers table
            var onboardedUser = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.AzureObjectId == userId && u.OrganizationLookupId == organizationId);
            
            if (onboardedUser?.AgentTypeIds == null || !onboardedUser.AgentTypeIds.Any())
            {
                _logger.LogWarning("No original agent type selections found for user {UserId}", userId);
                return true;
            }

            _logger.LogInformation("User originally selected {Count} agent types: {AgentTypeIds}", 
                onboardedUser.AgentTypeIds.Count, string.Join(", ", onboardedUser.AgentTypeIds));

            // Get all existing assignments (both active and inactive) for this user
            var existingAssignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId)
                .ToListAsync();

            var inactiveAssignments = existingAssignments.Where(a => !a.IsActive).ToList();
            var existingAgentTypeIds = existingAssignments.Select(a => a.AgentTypeId).ToHashSet();

            _logger.LogInformation("Found {InactiveCount} inactive assignments and {ExistingCount} total existing assignments", 
                inactiveAssignments.Count, existingAssignments.Count);

            // ENHANCED: Find missing agent type assignments that were never created due to Azure AD failures
            var missingAgentTypeIds = onboardedUser.AgentTypeIds.Except(existingAgentTypeIds).ToList();
            
            if (missingAgentTypeIds.Any())
            {
                _logger.LogInformation("🔧 REPAIR: Found {Count} missing agent type assignments that need to be created: {MissingIds}", 
                    missingAgentTypeIds.Count, string.Join(", ", missingAgentTypeIds));
            }

            var successCount = 0;
            var failureDetails = new List<string>();
            var newAssignments = new List<UserAgentTypeGroupAssignment>();

            // ENHANCED: First, create missing assignments that were never created due to Azure AD failures
            foreach (var missingAgentTypeId in missingAgentTypeIds)
            {
                _logger.LogInformation("🔧 REPAIR: Creating missing assignment for AgentType {AgentTypeId}", missingAgentTypeId);
                
                // Get the agent type to find its global security group ID
                var agentType = await _agentTypeService.GetByIdAsync(missingAgentTypeId);
                if (agentType == null)
                {
                    _logger.LogError("❌ Missing AgentType {AgentTypeId} not found during repair", missingAgentTypeId);
                    failureDetails.Add($"AgentType {missingAgentTypeId} not found");
                    continue;
                }

                _logger.LogInformation("Missing AgentType: Name={Name}, GlobalSecurityGroupId={GlobalSecurityGroupId}", 
                    agentType.Name, agentType.GlobalSecurityGroupId);

                if (string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                {
                    _logger.LogError("❌ Missing AgentType {AgentTypeId} ({Name}) has no GlobalSecurityGroupId configured", 
                        missingAgentTypeId, agentType.Name);
                    failureDetails.Add($"AgentType {agentType.Name} has no GlobalSecurityGroupId");
                    continue;
                }

                // Validate that this group exists in Azure AD
                var groupExists = await _graphService.GroupExistsAsync(agentType.GlobalSecurityGroupId);
                _logger.LogInformation("Missing assignment security group {GroupId} exists in Azure AD: {Exists}", 
                    agentType.GlobalSecurityGroupId, groupExists);

                if (!groupExists)
                {
                    _logger.LogError("❌ Security group {GroupId} for missing assignment does not exist in Azure AD", 
                        agentType.GlobalSecurityGroupId);
                    failureDetails.Add($"Security group {agentType.GlobalSecurityGroupId} does not exist in Azure AD");
                    continue;
                }

                try
                {
                    // Add user to Azure AD security group
                    _logger.LogInformation("🔧 REPAIR: Adding user {UserId} to security group {SecurityGroupId} for missing AgentType {AgentTypeName}", 
                        userId, agentType.GlobalSecurityGroupId, agentType.Name);
                    var addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                    
                    if (addedToGroup)
                    {
                        // Create new active assignment record
                        var newAssignment = UserAgentTypeGroupAssignmentExtensions.CreateAssignment(
                            userId, missingAgentTypeId, agentType.GlobalSecurityGroupId, organizationId, "system-repair");
                        
                        newAssignments.Add(newAssignment);
                        successCount++;
                        _logger.LogInformation("✅ Successfully created missing agent group assignment for AgentType {AgentTypeName}", agentType.Name);
                    }
                    else
                    {
                        _logger.LogError("❌ Failed to add user {UserId} to security group {SecurityGroupId} for missing AgentType {AgentTypeName}", 
                            userId, agentType.GlobalSecurityGroupId, agentType.Name);
                        failureDetails.Add($"Add failed for missing security group {agentType.GlobalSecurityGroupId} ({agentType.Name})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Exception adding user {UserId} to security group {SecurityGroupId} for missing AgentType {AgentTypeName}", 
                        userId, agentType.GlobalSecurityGroupId, agentType.Name);
                    failureDetails.Add($"Exception for missing AgentType {agentType.Name}: {ex.Message}");
                }
            }

            // Now process existing inactive assignments
            foreach (var assignment in inactiveAssignments)
            {
                _logger.LogInformation("Processing assignment: AgentTypeId={AgentTypeId}, Current SecurityGroupId={SecurityGroupId}", 
                    assignment.AgentTypeId, assignment.SecurityGroupId);

                // Get the current AgentType to find the correct GlobalSecurityGroupId
                var agentType = await _agentTypeService.GetByIdAsync(assignment.AgentTypeId);
                if (agentType == null)
                {
                    _logger.LogError("❌ AgentType {AgentTypeId} not found", assignment.AgentTypeId);
                    failureDetails.Add($"AgentType {assignment.AgentTypeId} not found");
                    continue;
                }

                _logger.LogInformation("AgentType: Name={Name}, GlobalSecurityGroupId={GlobalSecurityGroupId}", 
                    agentType.Name, agentType.GlobalSecurityGroupId);

                // Use the AgentType's GlobalSecurityGroupId directly (this should be the correct one)
                if (string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                {
                    _logger.LogError("❌ AgentType {AgentTypeId} ({Name}) has no GlobalSecurityGroupId configured", 
                        assignment.AgentTypeId, agentType.Name);
                    failureDetails.Add($"AgentType {agentType.Name} has no GlobalSecurityGroupId");
                    continue;
                }

                var correctSecurityGroupId = agentType.GlobalSecurityGroupId;
                _logger.LogInformation("✅ Using AgentType's GlobalSecurityGroupId directly: {GroupId}", correctSecurityGroupId);

                // Update assignment record if SecurityGroupId has changed
                if (assignment.SecurityGroupId != correctSecurityGroupId)
                {
                    _logger.LogInformation("📝 Updating assignment SecurityGroupId from {OldGroupId} to {NewGroupId}",
                        assignment.SecurityGroupId ?? "NULL", correctSecurityGroupId);
                    assignment.SecurityGroupId = correctSecurityGroupId;
                    assignment.ModifiedDate = DateTime.UtcNow;
                }

                // Validate that this group exists in Azure AD
                var groupExists = await _graphService.GroupExistsAsync(correctSecurityGroupId);
                _logger.LogInformation("Security group {GroupId} exists in Azure AD: {Exists}", correctSecurityGroupId, groupExists);

                if (!groupExists)
                {
                    _logger.LogError("❌ Security group {GroupId} does not exist in Azure AD", correctSecurityGroupId);
                    failureDetails.Add($"Security group {correctSecurityGroupId} does not exist in Azure AD");
                    continue;
                }

                try
                {
                    // Add user to Azure AD security group using correct GlobalSecurityGroupId
                    _logger.LogInformation("Adding user {UserId} to security group {SecurityGroupId} for AgentType {AgentTypeName}", 
                        userId, correctSecurityGroupId, agentType.Name);
                    var addedToGroup = await _graphService.AddUserToGroupAsync(userId, correctSecurityGroupId);
                    
                    if (addedToGroup)
                    {
                        assignment.Reactivate();
                        successCount++;
                        _logger.LogInformation("✅ Successfully reactivated agent group assignment for AgentType {AgentTypeName}", agentType.Name);
                    }
                    else
                    {
                        _logger.LogError("❌ Failed to add user {UserId} to security group {SecurityGroupId} for AgentType {AgentTypeName}", 
                            userId, correctSecurityGroupId, agentType.Name);
                        failureDetails.Add($"Add failed for security group {correctSecurityGroupId} ({agentType.Name})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Exception adding user {UserId} to security group {SecurityGroupId} for AgentType {AgentTypeName}", 
                        userId, correctSecurityGroupId, agentType.Name);
                    failureDetails.Add($"Exception for AgentType {agentType.Name}: {ex.Message}");
                }
            }

            // Save new assignments to database
            if (newAssignments.Any())
            {
                _context.UserAgentTypeGroupAssignments.AddRange(newAssignments);
                _logger.LogInformation("💾 Adding {Count} new missing assignments to database", newAssignments.Count);
            }

            // Save changes
            if (inactiveAssignments.Any() || newAssignments.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("💾 Saved agent group assignment changes to database");
            }

            var totalProcessed = inactiveAssignments.Count + missingAgentTypeIds.Count;
            var totalMissingCreated = newAssignments.Count;

            _logger.LogInformation("=== ENHANCED AGENT GROUP REACTIVATION SUMMARY ===");
            _logger.LogInformation("Missing assignments created: {MissingCreated}/{MissingTotal}", totalMissingCreated, missingAgentTypeIds.Count);
            _logger.LogInformation("Existing assignments reactivated: {Reactivated}/{InactiveTotal}", successCount - totalMissingCreated, inactiveAssignments.Count);
            _logger.LogInformation("Total successful operations: {Success}/{Total}", successCount, totalProcessed);
            _logger.LogInformation("Failed operations: {Failed}", totalProcessed - successCount);
            
            if (failureDetails.Any())
            {
                _logger.LogError("Failure details: {Failures}", string.Join("; ", failureDetails));
            }

            // Success if all operations succeeded OR if we successfully handled everything that could be handled
            var expectedSuccessCount = totalProcessed - failureDetails.Count(f => f.Contains("does not exist in Azure AD"));
            return successCount >= expectedSuccessCount;
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
                    _logger.LogInformation("✅ Successfully removed user {UserId} from security group {GroupId} and deactivated assignment", 
                        userId, assignment.SecurityGroupId);
                }
                else
                {
                    // CRITICAL FIX: Still deactivate the database record even if Azure removal fails
                    // This handles cases where the security group was deleted from Azure AD
                    _logger.LogWarning("⚠️ Failed to remove user {UserId} from Azure security group {GroupId} (group may not exist), but deactivating database record anyway", 
                        userId, assignment.SecurityGroupId);
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

    /// <summary>
    /// CRITICAL: Performs bidirectional sync between Azure AD and database for agent group assignments
    /// This ensures consistency between what's in Azure AD and what's tracked in the database
    /// </summary>
    public async Task<bool> SyncUserAgentGroupAssignmentsAsync(string userId, Guid organizationId)
    {
        try
        {
            _logger.LogInformation("=== BIDIRECTIONAL AGENT GROUP SYNC for User {UserId} in Org {OrganizationId} ===", userId, organizationId);

            // Get user's intended agent types from OnboardedUsers table
            var onboardedUser = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.AzureObjectId == userId && u.OrganizationLookupId == organizationId);

            if (onboardedUser?.AgentTypeIds == null || !onboardedUser.AgentTypeIds.Any())
            {
                _logger.LogWarning("No agent types configured for user {UserId}", userId);
                return true;
            }

            _logger.LogInformation("User should have {Count} agent types: {AgentTypeIds}", 
                onboardedUser.AgentTypeIds.Count, string.Join(", ", onboardedUser.AgentTypeIds));

            // Get current database assignments
            var dbAssignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId)
                .ToListAsync();

            var activeDbAssignments = dbAssignments.Where(a => a.IsActive).ToList();
            var activeDbAgentTypes = activeDbAssignments.Select(a => a.AgentTypeId).ToHashSet();

            _logger.LogInformation("Database has {Total} total assignments ({Active} active) for user {UserId}", 
                dbAssignments.Count, activeDbAssignments.Count, userId);

            // Get current Azure AD group memberships
            var userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
            _logger.LogInformation("User is member of {Count} Azure AD groups", userGroups.Count);

            var syncActions = new List<string>();
            var successCount = 0;
            var failureCount = 0;

            // For each intended agent type, ensure proper sync
            foreach (var agentTypeId in onboardedUser.AgentTypeIds)
            {
                var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
                if (agentType == null || string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                {
                    _logger.LogWarning("Agent type {AgentTypeId} not found or has no security group", agentTypeId);
                    continue;
                }

                var dbAssignment = dbAssignments.FirstOrDefault(a => a.AgentTypeId == agentTypeId);
                var isInAzureGroup = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                var isActiveInDb = dbAssignment?.IsActive == true;

                _logger.LogInformation("Agent {Name}: Azure={IsInAzure}, Database={IsActiveInDb}", 
                    agentType.Name, isInAzureGroup, isActiveInDb);

                if (isInAzureGroup && isActiveInDb)
                {
                    // Perfect sync - both match
                    syncActions.Add($"✅ {agentType.Name}: In sync");
                    successCount++;
                }
                else if (isInAzureGroup && !isActiveInDb)
                {
                    // User is in Azure group but not tracked in database - create/reactivate DB record
                    if (dbAssignment != null)
                    {
                        dbAssignment.Reactivate();
                        syncActions.Add($"🔧 {agentType.Name}: Reactivated database record to match Azure");
                    }
                    else
                    {
                        var newAssignment = UserAgentTypeGroupAssignmentExtensions.CreateAssignment(
                            userId, agentTypeId, agentType.GlobalSecurityGroupId, organizationId, "sync-repair");
                        _context.UserAgentTypeGroupAssignments.Add(newAssignment);
                        syncActions.Add($"🔧 {agentType.Name}: Created database record to match Azure");
                    }
                    successCount++;
                }
                else if (!isInAzureGroup && isActiveInDb)
                {
                    // User is tracked in database but not in Azure group - add to Azure
                    var addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                    if (addedToGroup)
                    {
                        syncActions.Add($"🔧 {agentType.Name}: Added to Azure group to match database");
                        successCount++;
                    }
                    else
                    {
                        syncActions.Add($"❌ {agentType.Name}: Failed to add to Azure group");
                        failureCount++;
                    }
                }
                else
                {
                    // Neither in Azure nor active in database - create both
                    var addedToGroup = await _graphService.AddUserToGroupAsync(userId, agentType.GlobalSecurityGroupId);
                    if (addedToGroup)
                    {
                        if (dbAssignment != null)
                        {
                            dbAssignment.Reactivate();
                        }
                        else
                        {
                            var newAssignment = UserAgentTypeGroupAssignmentExtensions.CreateAssignment(
                                userId, agentTypeId, agentType.GlobalSecurityGroupId, organizationId, "sync-repair");
                            _context.UserAgentTypeGroupAssignments.Add(newAssignment);
                        }
                        syncActions.Add($"🔧 {agentType.Name}: Created both Azure and database records");
                        successCount++;
                    }
                    else
                    {
                        syncActions.Add($"❌ {agentType.Name}: Failed to create Azure group membership");
                        failureCount++;
                    }
                }
            }

            // Save all database changes
            await _context.SaveChangesAsync();

            _logger.LogInformation("=== BIDIRECTIONAL SYNC SUMMARY ===");
            _logger.LogInformation("Successful syncs: {Success}", successCount);
            _logger.LogInformation("Failed syncs: {Failed}", failureCount);
            foreach (var action in syncActions)
            {
                _logger.LogInformation(action);
            }

            return failureCount == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bidirectional sync for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// CRITICAL: Synchronizes agent group memberships for ALL users in an organization
    /// Used when organization-level agent types or global security group IDs change
    /// </summary>
    public async Task<bool> SyncOrganizationAgentGroupAssignmentsAsync(Guid organizationId, string modifiedBy)
    {
        try
        {
            _logger.LogInformation("=== ORGANIZATION-WIDE AGENT GROUP SYNC for Org {OrganizationId} ===", organizationId);

            // Get all users in the organization
            var organizationUsers = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId)
                .ToListAsync();

            if (!organizationUsers.Any())
            {
                _logger.LogInformation("No users found for organization {OrganizationId}", organizationId);
                return true;
            }

            _logger.LogInformation("Processing {UserCount} users for organization-wide agent group sync", organizationUsers.Count);

            var totalUsers = organizationUsers.Count;
            var successCount = 0;
            var failureCount = 0;
            var syncResults = new List<string>();

            // Process each user individually
            foreach (var user in organizationUsers)
            {
                try
                {
                    if (string.IsNullOrEmpty(user.AzureObjectId))
                    {
                        _logger.LogWarning("User {Email} has no Azure Object ID, skipping", user.Email);
                        continue;
                    }

                    _logger.LogInformation("Syncing agent groups for user {Email} ({AzureObjectId})", 
                        user.Email, user.AzureObjectId);

                    // Perform individual user sync
                    var userSyncSuccess = await SyncUserAgentGroupAssignmentsAsync(user.AzureObjectId, organizationId);
                    
                    if (userSyncSuccess)
                    {
                        successCount++;
                        syncResults.Add($"✅ {user.Email}: Sync successful");
                        _logger.LogInformation("✅ Agent group sync successful for user {Email}", user.Email);
                    }
                    else
                    {
                        failureCount++;
                        syncResults.Add($"❌ {user.Email}: Sync failed");
                        _logger.LogWarning("❌ Agent group sync failed for user {Email}", user.Email);
                    }
                }
                catch (Exception userEx)
                {
                    failureCount++;
                    syncResults.Add($"❌ {user.Email}: Exception - {userEx.Message}");
                    _logger.LogError(userEx, "❌ Exception during agent group sync for user {Email}", user.Email);
                }
            }

            _logger.LogInformation("=== ORGANIZATION-WIDE AGENT GROUP SYNC SUMMARY ===");
            _logger.LogInformation("Total users processed: {Total}", totalUsers);
            _logger.LogInformation("Successful syncs: {Success}", successCount);
            _logger.LogInformation("Failed syncs: {Failed}", failureCount);
            _logger.LogInformation("Success rate: {SuccessRate:P2}", (double)successCount / totalUsers);

            // Log detailed results
            foreach (var result in syncResults)
            {
                _logger.LogInformation(result);
            }

            if (failureCount > 0)
            {
                _logger.LogWarning("⚠️ {FailureCount} users failed during organization-wide agent group sync. Manual verification recommended.", failureCount);
            }

            // Return true if majority succeeded (allows for some acceptable failures)
            var successRate = (double)successCount / totalUsers;
            return successRate >= 0.8; // 80% success rate threshold
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CRITICAL ERROR during organization-wide agent group sync for org {OrganizationId}", organizationId);
            return false;
        }
    }
    
    /// <summary>
    /// CRITICAL: Removes ALL users in an organization from a specific agent type's security group
    /// Used when SuperAdmin unassigns an agent type from organization level
    /// </summary>
    public async Task<bool> RemoveAllUsersFromAgentTypeAsync(Guid organizationId, Guid agentTypeId, string modifiedBy)
    {
        try
        {
            _logger.LogInformation("🔄 ORG-LEVEL REMOVAL: Starting removal of ALL users in organization {OrganizationId} from agent type {AgentTypeId}", 
                organizationId, agentTypeId);

            // Get the agent type to find its security group ID (SOURCE-OF-TRUTH)
            var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
            if (agentType == null || string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
            {
                _logger.LogWarning("⚠️ Agent type {AgentTypeId} not found or has no GlobalSecurityGroupId. Cannot remove users from security group.", agentTypeId);
                return false;
            }

            // Get all active assignments for this agent type in this organization
            var assignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.AgentTypeId == agentTypeId 
                         && a.OrganizationId == organizationId 
                         && a.IsActive)
                .ToListAsync();

            if (!assignments.Any())
            {
                _logger.LogInformation("✅ No active assignments found for agent type {AgentTypeId} in organization {OrganizationId}", 
                    agentTypeId, organizationId);
                return true;
            }

            _logger.LogInformation("🔄 Found {AssignmentCount} active assignments to process for agent type {AgentTypeName}", 
                assignments.Count, agentType.Name);

            var successCount = 0;
            var totalAssignments = assignments.Count;

            foreach (var assignment in assignments)
            {
                try
                {
                    _logger.LogInformation("🔄 Processing user {UserId} removal from agent type {AgentTypeName}", 
                        assignment.UserId, agentType.Name);

                    // Check if user is actually a member of the group first (bidirectional validation)
                    var userGroups = await _graphService.GetUserGroupMembershipsAsync(assignment.UserId);
                    var isMember = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                    
                    if (isMember)
                    {
                        // User is a member - proceed with removal
                        var removedFromGroup = await _graphService.RemoveUserFromGroupAsync(assignment.UserId, agentType.GlobalSecurityGroupId);
                        
                        // CRITICAL: Always deactivate database record for security, regardless of Azure success
                        assignment.Deactivate();
                        
                        // CRITICAL FIX: Remove agent type from user's AgentTypeIds
                        var user = await _context.OnboardedUsers
                            .FirstOrDefaultAsync(u => u.AzureObjectId == assignment.UserId && u.OrganizationLookupId == organizationId);
                        
                        if (user != null && user.AgentTypeIds.Contains(agentTypeId))
                        {
                            user.AgentTypeIds.Remove(agentTypeId);
                            user.ModifiedOn = DateTime.UtcNow;
                            user.ModifiedBy = Guid.TryParse(modifiedBy, out var modifiedByGuid) ? modifiedByGuid : Guid.NewGuid();
                            
                            _logger.LogInformation("✅ Removed agent type {AgentTypeName} from user {Email} AgentTypeIds", 
                                agentType.Name, user.Email);
                        }
                        
                        if (removedFromGroup)
                        {
                            _logger.LogInformation("✅ Successfully removed user {UserId} from security group {GroupId} for agent type {AgentTypeName}", 
                                assignment.UserId, agentType.GlobalSecurityGroupId, agentType.Name);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Azure removal failed but deactivated database assignment anyway for security - user {UserId} group {GroupId}", 
                                assignment.UserId, agentType.GlobalSecurityGroupId);
                        }
                        
                        successCount++;
                    }
                    else
                    {
                        // User is not a member - just deactivate database record
                        assignment.Deactivate();
                        
                        // CRITICAL FIX: Remove agent type from user's AgentTypeIds even if not in Azure group
                        var user = await _context.OnboardedUsers
                            .FirstOrDefaultAsync(u => u.AzureObjectId == assignment.UserId && u.OrganizationLookupId == organizationId);
                        
                        if (user != null && user.AgentTypeIds.Contains(agentTypeId))
                        {
                            user.AgentTypeIds.Remove(agentTypeId);
                            user.ModifiedOn = DateTime.UtcNow;
                            user.ModifiedBy = Guid.TryParse(modifiedBy, out var modifiedByGuid) ? modifiedByGuid : Guid.NewGuid();
                            
                            _logger.LogInformation("✅ Removed agent type {AgentTypeName} from user {Email} AgentTypeIds (user was not in Azure group)", 
                                agentType.Name, user.Email);
                        }
                        
                        _logger.LogInformation("✅ User {UserId} was not a member of security group {GroupId} - deactivated database assignment", 
                            assignment.UserId, agentType.GlobalSecurityGroupId);
                        successCount++;
                    }
                }
                catch (Exception userEx)
                {
                    _logger.LogError(userEx, "❌ Error removing user {UserId} from agent type {AgentTypeId}", 
                        assignment.UserId, agentTypeId);
                    // Continue processing other users even if one fails
                }
            }

            // Save all changes
            await _context.SaveChangesAsync();

            var successRate = (double)successCount / totalAssignments * 100;
            _logger.LogInformation("🎯 ORG-LEVEL REMOVAL COMPLETED: {SuccessCount}/{TotalCount} users removed from agent type {AgentTypeName} ({SuccessRate:F1}%)", 
                successCount, totalAssignments, agentType.Name, successRate);

            // Return true if we had reasonable success (80% threshold)
            return successRate >= 80.0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CRITICAL ERROR: Failed to remove all users from agent type {AgentTypeId} in organization {OrganizationId}", 
                agentTypeId, organizationId);
            return false;
        }
    }

    /// <summary>
    /// CRITICAL: Assigns ALL users in an organization to a specific agent type's security group
    /// Used when SuperAdmin assigns an agent type at organization level with auto-assign option
    /// </summary>
    public async Task<bool> AssignAllUsersToAgentTypeAsync(Guid organizationId, Guid agentTypeId, string modifiedBy)
    {
        try
        {
            _logger.LogInformation("🎯 ORG-LEVEL ASSIGNMENT: Starting assignment of ALL users in organization {OrganizationId} to agent type {AgentTypeId}", 
                organizationId, agentTypeId);

            // Get the agent type to find its security group ID (SOURCE-OF-TRUTH)
            var agentType = await _agentTypeService.GetByIdAsync(agentTypeId);
            if (agentType == null || string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
            {
                _logger.LogWarning("⚠️ Agent type {AgentTypeId} not found or has no GlobalSecurityGroupId. Cannot assign users to security group.", agentTypeId);
                return false;
            }

            // Get all active users in the organization
            var organizationUsers = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId && u.StateCode == StateCode.Active)
                .ToListAsync();

            if (!organizationUsers.Any())
            {
                _logger.LogInformation("✅ No active users found in organization {OrganizationId}", organizationId);
                return true;
            }

            _logger.LogInformation("🔄 Found {UserCount} active users to assign to agent type {AgentTypeName}", 
                organizationUsers.Count, agentType.Name);

            var successCount = 0;
            var totalUsers = organizationUsers.Count;

            foreach (var user in organizationUsers)
            {
                try
                {
                    _logger.LogInformation("🔄 Processing user {UserId} ({Email}) assignment to agent type {AgentTypeName}", 
                        user.OnboardedUserId, user.Email, agentType.Name);

                    // Check if user is already assigned to avoid duplicates
                    var existingAssignment = await _context.UserAgentTypeGroupAssignments
                        .FirstOrDefaultAsync(a => a.UserId == user.AzureObjectId 
                                                && a.AgentTypeId == agentTypeId 
                                                && a.OrganizationId == organizationId 
                                                && a.IsActive);

                    if (existingAssignment == null)
                    {
                        // Create new assignment in database
                        var newAssignment = new UserAgentTypeGroupAssignment
                        {
                            Id = Guid.NewGuid(),
                            UserId = user.AzureObjectId,
                            AgentTypeId = agentTypeId,
                            OrganizationId = organizationId,
                            SecurityGroupId = agentType.GlobalSecurityGroupId,
                            AssignedDate = DateTime.UtcNow,
                            AssignedBy = modifiedBy,
                            IsActive = true
                        };

                        _context.UserAgentTypeGroupAssignments.Add(newAssignment);
                        _logger.LogInformation("✅ Created database assignment for user {Email} to agent type {AgentTypeName}", 
                            user.Email, agentType.Name);
                    }
                    else
                    {
                        _logger.LogInformation("ℹ️ User {Email} already assigned to agent type {AgentTypeName} - skipping database creation", 
                            user.Email, agentType.Name);
                    }

                    // Check if user is actually a member of the Azure AD group (bidirectional validation)
                    var userGroups = await _graphService.GetUserGroupMembershipsAsync(user.AzureObjectId);
                    var isMember = userGroups.Any(g => g.Id == agentType.GlobalSecurityGroupId);
                    
                    if (!isMember)
                    {
                        // User is not a member - add them to the group
                        var addedToGroup = await _graphService.AddUserToGroupAsync(user.AzureObjectId, agentType.GlobalSecurityGroupId);
                        
                        if (addedToGroup)
                        {
                            _logger.LogInformation("✅ Added user {Email} to Azure AD security group {GroupId}", 
                                user.Email, agentType.GlobalSecurityGroupId);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to add user {Email} to Azure AD security group {GroupId}", 
                                user.Email, agentType.GlobalSecurityGroupId);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("ℹ️ User {Email} already member of Azure AD security group {GroupId}", 
                            user.Email, agentType.GlobalSecurityGroupId);
                    }

                    // Update user's assigned agent types in database
                    if (!user.AgentTypeIds.Contains(agentTypeId))
                    {
                        user.AgentTypeIds.Add(agentTypeId);
                        user.ModifiedOn = DateTime.UtcNow;
                        user.ModifiedBy = Guid.TryParse(modifiedBy, out var modifiedByGuid) ? modifiedByGuid : Guid.NewGuid();
                        
                        _logger.LogInformation("✅ Updated user {Email} AgentTypeIds to include {AgentTypeName}", 
                            user.Email, agentType.Name);
                    }

                    successCount++;
                }
                catch (Exception userEx)
                {
                    _logger.LogError(userEx, "❌ Error assigning user {UserId} ({Email}) to agent type {AgentTypeId}", 
                        user.OnboardedUserId, user.Email, agentTypeId);
                    // Continue processing other users even if one fails
                }
            }

            // Save all changes
            await _context.SaveChangesAsync();

            var successRate = (double)successCount / totalUsers * 100;
            _logger.LogInformation("🎯 ORG-LEVEL ASSIGNMENT COMPLETED: {SuccessCount}/{TotalCount} users assigned to agent type {AgentTypeName} ({SuccessRate:F1}%)", 
                successCount, totalUsers, agentType.Name, successRate);

            // Return true if we had reasonable success (80% threshold)
            return successRate >= 80.0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CRITICAL ERROR: Failed to assign all users to agent type {AgentTypeId} in organization {OrganizationId}", 
                agentTypeId, organizationId);
            return false;
        }
    }
}