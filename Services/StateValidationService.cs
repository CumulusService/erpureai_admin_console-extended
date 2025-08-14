using AdminConsole.Data;
using AdminConsole.Models;
using AdminConsole.Services;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

public class StateValidationService : IStateValidationService
{
    private readonly AdminConsoleDbContext _context;
    private readonly IGraphService _graphService;
    private readonly IAgentTypeService _agentTypeService;
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<StateValidationService> _logger;

    public StateValidationService(
        AdminConsoleDbContext context,
        IGraphService graphService,
        IAgentTypeService agentTypeService,
        IOrganizationService organizationService,
        ILogger<StateValidationService> logger)
    {
        _context = context;
        _graphService = graphService;
        _agentTypeService = agentTypeService;
        _organizationService = organizationService;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateUserAgentAssignmentsAsync(string userId, Guid organizationId)
    {
        var result = new ValidationResult();
        
        try
        {
            _logger.LogInformation("Validating user agent assignments for user {UserId} in organization {OrganizationId}", 
                userId, organizationId);

            // Get database assignments (without Include since AgentType navigation is ignored in EF config)
            var dbAssignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && a.IsActive)
                .ToListAsync();
                
            // Manually load AgentType data for assignments
            var agentTypeIds = dbAssignments.Select(a => a.AgentTypeId).Distinct().ToList();
            var agentTypes = await _context.AgentTypes
                .Where(at => agentTypeIds.Contains(at.Id))
                .ToDictionaryAsync(at => at.Id, at => at);

            // Get user's intended agent types from OnboardedUsers
            var onboardedUser = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.AzureObjectId == userId && u.OrganizationLookupId == organizationId);

            if (onboardedUser == null)
            {
                result.Errors.Add($"OnboardedUser not found for userId {userId}");
                return result;
            }

            // Validate assignments match intended configuration
            var intendedAgentTypeIds = onboardedUser.AgentTypeIds ?? new List<Guid>();
            var actualAssignmentIds = dbAssignments.Select(a => a.AgentTypeId).ToHashSet();

            // Check for missing assignments
            var missingAssignments = intendedAgentTypeIds.Except(actualAssignmentIds).ToList();
            foreach (var missingId in missingAssignments)
            {
                result.Errors.Add($"Missing assignment for agent type {missingId}");
            }

            // Check for extra assignments
            var extraAssignments = actualAssignmentIds.Except(intendedAgentTypeIds).ToList();
            foreach (var extraId in extraAssignments)
            {
                result.Warnings.Add($"Extra assignment for agent type {extraId}");
            }

            // Validate each assignment has correct SecurityGroupId
            foreach (var assignment in dbAssignments)
            {
                if (agentTypes.TryGetValue(assignment.AgentTypeId, out var agentType))
                {
                    if (assignment.SecurityGroupId != agentType.GlobalSecurityGroupId)
                    {
                        result.Errors.Add($"Assignment {assignment.Id} has stale SecurityGroupId. Expected: {agentType.GlobalSecurityGroupId}, Actual: {assignment.SecurityGroupId}");
                    }
                }
            }

            result.ValidationData["DatabaseAssignments"] = dbAssignments.Count;
            result.ValidationData["IntendedAssignments"] = intendedAgentTypeIds.Count;
            result.ValidationData["MissingCount"] = missingAssignments.Count;
            result.ValidationData["ExtraCount"] = extraAssignments.Count;

            result.IsValid = !result.Errors.Any();
            result.Summary = $"Database assignments validation: {(result.IsValid ? "VALID" : "INVALID")} - {dbAssignments.Count} assignments, {result.Errors.Count} errors, {result.Warnings.Count} warnings";

            _logger.LogInformation("Database assignments validation complete for user {UserId}: {Summary}", userId, result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user agent assignments for user {UserId}", userId);
            result.Errors.Add($"Validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateOnboardedUserStateAsync(string azureObjectId)
    {
        var result = new ValidationResult();
        
        try
        {
            _logger.LogInformation("Validating OnboardedUser state for Azure Object ID {AzureObjectId}", azureObjectId);

            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.AzureObjectId == azureObjectId);

            if (user == null)
            {
                result.Errors.Add($"OnboardedUser not found for Azure Object ID {azureObjectId}");
                return result;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(user.Email))
            {
                result.Errors.Add("User email is null or empty");
            }

            if (user.OrganizationLookupId == Guid.Empty)
            {
                result.Errors.Add("User organization lookup ID is empty");
            }

            if (!user.IsActive)
            {
                result.Warnings.Add("User is not active");
            }

            if (user.StateCode != StateCode.Active)
            {
                result.Warnings.Add($"User StateCode is {user.StateCode}, expected Active");
            }

            // Validate agent type consistency
            var legacyCount = user.AgentTypes?.Count ?? 0;
            var newCount = user.AgentTypeIds?.Count ?? 0;
            
            if (legacyCount > 0 && newCount == 0)
            {
                result.Warnings.Add("User has legacy agent types but no new AgentTypeIds");
            }

            result.ValidationData["UserId"] = user.OnboardedUserId;
            result.ValidationData["Email"] = user.Email;
            result.ValidationData["IsActive"] = user.IsActive;
            result.ValidationData["StateCode"] = user.StateCode.ToString();
            result.ValidationData["LegacyAgentTypes"] = legacyCount;
            result.ValidationData["NewAgentTypeIds"] = newCount;

            result.IsValid = !result.Errors.Any();
            result.Summary = $"OnboardedUser validation: {(result.IsValid ? "VALID" : "INVALID")} - {result.Errors.Count} errors, {result.Warnings.Count} warnings";

            _logger.LogInformation("OnboardedUser validation complete for {AzureObjectId}: {Summary}", azureObjectId, result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OnboardedUser state for {AzureObjectId}", azureObjectId);
            result.Errors.Add($"Validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateAzureGroupMembershipsAsync(string userId, List<string> expectedGroupIds)
    {
        var result = new ValidationResult();
        
        try
        {
            _logger.LogInformation("Validating Azure group memberships for user {UserId}", userId);

            // Get user's actual group memberships from Azure AD  
            var userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
            var actualGroupIds = userGroups.Select(g => g.Id).Where(id => id != null).ToHashSet();

            var missingMemberships = expectedGroupIds.Except(actualGroupIds).ToList();
            
            // If there are missing memberships, retry a few times to handle Azure AD propagation delays
            if (missingMemberships.Any())
            {
                const int maxRetries = 3;
                const int retryDelayMs = 2000; // 2 second delay between retries
                
                for (int attempt = 1; attempt <= maxRetries && missingMemberships.Any(); attempt++)
                {
                    _logger.LogWarning("Missing group memberships detected for user {UserId}, retry attempt {Attempt}/{MaxRetries}. Missing: {MissingGroups}", 
                        userId, attempt, maxRetries, string.Join(", ", missingMemberships));
                    
                    await Task.Delay(retryDelayMs);
                    
                    // Retry the group membership check
                    userGroups = await _graphService.GetUserGroupMembershipsAsync(userId);
                    actualGroupIds = userGroups.Select(g => g.Id).Where(id => id != null).ToHashSet();
                    missingMemberships = expectedGroupIds.Except(actualGroupIds).ToList();
                    
                    if (!missingMemberships.Any())
                    {
                        _logger.LogInformation("Group memberships validated successfully for user {UserId} after {Attempt} retries", userId, attempt);
                        break;
                    }
                }
            }

            // Add remaining missing memberships as errors, but also double-check from group perspective
            foreach (var missingId in missingMemberships)
            {
                _logger.LogWarning("Group membership missing for user {UserId} in group {GroupId}. Performing reverse lookup...", userId, missingId);
                
                // Double-check by querying the group's members directly
                var isActuallyMember = await _graphService.IsUserMemberOfGroupAsync(userId, missingId);
                
                if (isActuallyMember)
                {
                    _logger.LogWarning("🔄 REVERSE LOOKUP SUCCESS: User {UserId} IS actually a member of group {GroupId}, but MemberOf query didn't find it. This indicates a Graph API consistency issue.", userId, missingId);
                    result.Warnings.Add($"Group membership inconsistency detected for group {missingId} - user is member but MemberOf query failed");
                }
                else
                {
                    _logger.LogError("❌ REVERSE LOOKUP CONFIRMED: User {UserId} is NOT a member of group {GroupId} from either perspective", userId, missingId);
                    result.Errors.Add($"User not member of expected group {missingId}");
                }
            }

            // Check for unexpected memberships (in our managed groups)
            var unexpectedMemberships = actualGroupIds.Intersect(expectedGroupIds).Except(expectedGroupIds).ToList();
            foreach (var unexpectedId in unexpectedMemberships)
            {
                result.Warnings.Add($"User has unexpected membership in group {unexpectedId}");
            }

            result.ValidationData["ExpectedGroups"] = expectedGroupIds.Count;
            result.ValidationData["ActualGroups"] = actualGroupIds.Count;
            result.ValidationData["MissingMemberships"] = missingMemberships.Count;
            result.ValidationData["UnexpectedMemberships"] = unexpectedMemberships.Count;

            result.IsValid = !result.Errors.Any();
            result.Summary = $"Azure group memberships validation: {(result.IsValid ? "VALID" : "INVALID")} - {missingMemberships.Count} missing, {unexpectedMemberships.Count} unexpected";

            _logger.LogInformation("Azure group memberships validation complete for user {UserId}: {Summary}", userId, result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Azure group memberships for user {UserId}", userId);
            result.Errors.Add($"Azure validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateGroupExistenceAsync(List<string> groupIds)
    {
        var result = new ValidationResult();
        
        try
        {
            _logger.LogInformation("Validating existence of {Count} groups in Azure AD", groupIds.Count);

            var nonExistentGroups = new List<string>();
            
            foreach (var groupId in groupIds)
            {
                try
                {
                    var exists = await _graphService.GroupExistsAsync(groupId);
                    if (!exists)
                    {
                        nonExistentGroups.Add(groupId);
                        result.Errors.Add($"Group {groupId} does not exist in Azure AD");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not verify existence of group {groupId}: {ex.Message}");
                }
            }

            result.ValidationData["TotalGroups"] = groupIds.Count;
            result.ValidationData["NonExistentGroups"] = nonExistentGroups.Count;
            result.ValidationData["NonExistentGroupIds"] = nonExistentGroups;

            result.IsValid = !result.Errors.Any();
            result.Summary = $"Group existence validation: {(result.IsValid ? "VALID" : "INVALID")} - {nonExistentGroups.Count} non-existent groups";

            _logger.LogInformation("Group existence validation complete: {Summary}", result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating group existence");
            result.Errors.Add($"Group existence validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateDatabaseConsistencyAsync(string userId, Guid organizationId)
    {
        var result = new ValidationResult();
        
        try
        {
            // Combine multiple database validations
            var userValidation = await ValidateOnboardedUserStateAsync(userId);
            var assignmentValidation = await ValidateUserAgentAssignmentsAsync(userId, organizationId);

            result.Errors.AddRange(userValidation.Errors);
            result.Errors.AddRange(assignmentValidation.Errors);
            result.Warnings.AddRange(userValidation.Warnings);
            result.Warnings.AddRange(assignmentValidation.Warnings);

            // Merge validation data
            foreach (var kvp in userValidation.ValidationData)
            {
                result.ValidationData[$"User_{kvp.Key}"] = kvp.Value;
            }
            foreach (var kvp in assignmentValidation.ValidationData)
            {
                result.ValidationData[$"Assignment_{kvp.Key}"] = kvp.Value;
            }

            result.IsValid = !result.Errors.Any();
            result.Summary = $"Database consistency: {(result.IsValid ? "VALID" : "INVALID")} - {result.Errors.Count} errors, {result.Warnings.Count} warnings";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating database consistency for user {UserId}", userId);
            result.Errors.Add($"Database consistency validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateUserStateConsistencyAsync(string userId, Guid organizationId)
    {
        var result = new ValidationResult();
        
        try
        {
            _logger.LogInformation("Validating complete user state consistency for {UserId} in organization {OrganizationId}", 
                userId, organizationId);

            // Get database state
            var dbValidation = await ValidateDatabaseConsistencyAsync(userId, organizationId);
            result.Errors.AddRange(dbValidation.Errors);
            result.Warnings.AddRange(dbValidation.Warnings);

            // Get expected group IDs from database assignments
            var assignments = await _context.UserAgentTypeGroupAssignments
                .Where(a => a.UserId == userId && a.OrganizationId == organizationId && a.IsActive)
                .ToListAsync();
                
            // Manually load AgentType data for validation
            var assignmentAgentTypeIds = assignments.Select(a => a.AgentTypeId).Distinct().ToList();
            var assignmentAgentTypes = await _context.AgentTypes
                .Where(at => assignmentAgentTypeIds.Contains(at.Id))
                .ToDictionaryAsync(at => at.Id, at => at);

            var expectedGroupIds = assignments
                .Where(a => assignmentAgentTypes.ContainsKey(a.AgentTypeId))
                .Select(a => assignmentAgentTypes[a.AgentTypeId].GlobalSecurityGroupId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!) // Assert non-null since we filtered out nulls
                .ToList();

            // Validate Azure state
            if (expectedGroupIds.Any())
            {
                var azureValidation = await ValidateAzureGroupMembershipsAsync(userId, expectedGroupIds);
                result.Errors.AddRange(azureValidation.Errors);
                result.Warnings.AddRange(azureValidation.Warnings);

                // Merge validation data
                foreach (var kvp in azureValidation.ValidationData)
                {
                    result.ValidationData[$"Azure_{kvp.Key}"] = kvp.Value;
                }
            }

            // Merge database validation data
            foreach (var kvp in dbValidation.ValidationData)
            {
                result.ValidationData[kvp.Key] = kvp.Value;
            }

            result.IsValid = !result.Errors.Any();
            result.Summary = $"Complete user state consistency: {(result.IsValid ? "VALID" : "INVALID")} - {result.Errors.Count} errors, {result.Warnings.Count} warnings";

            _logger.LogInformation("Complete user state validation finished for {UserId}: {Summary}", userId, result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user state consistency for {UserId}", userId);
            result.Errors.Add($"State consistency validation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateAndRepairStateAsync(string userId, Guid organizationId, bool performRepair = false)
    {
        var result = await ValidateUserStateConsistencyAsync(userId, organizationId);
        
        if (performRepair && result.Errors.Any())
        {
            _logger.LogInformation("Attempting to repair state inconsistencies for user {UserId}", userId);
            
            // TODO: Implement repair logic based on specific error types
            // This would be done in coordination with AgentGroupAssignmentService
            
            result.Repairs.Add("State repair functionality would be implemented here");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateCompleteUserStateAsync(string userId, Guid organizationId)
    {
        return await ValidateUserStateConsistencyAsync(userId, organizationId);
    }
}