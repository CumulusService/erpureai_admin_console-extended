using AdminConsole.Models;
using AdminConsole.Data;
using Microsoft.Graph;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Implementation of secure, domain-based invitation service
/// </summary>
public class InvitationService : IInvitationService
{
    private readonly IOrganizationService _organizationService;
    private readonly IGraphService _graphService;
    private readonly ISecurityGroupService _securityGroupService;
    private readonly AdminConsoleDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InvitationService> _logger;
    private readonly IAgentGroupAssignmentService _agentGroupAssignmentService;
    private readonly ITeamsGroupService _teamsGroupService;
    private readonly IAgentTypeService _agentTypeService;
    private readonly IStateValidationService _stateValidationService;
    private readonly IOperationStatusService _operationStatusService;
    private readonly IUserDatabaseAccessService _userDatabaseAccessService;
    private readonly IConfiguration _configuration;
    private readonly IDataIsolationService _dataIsolationService;

    public InvitationService(
        IOrganizationService organizationService,
        IGraphService graphService,
        ISecurityGroupService securityGroupService,
        AdminConsoleDbContext dbContext,
        IMemoryCache cache,
        ILogger<InvitationService> logger,
        IAgentGroupAssignmentService agentGroupAssignmentService,
        ITeamsGroupService teamsGroupService,
        IAgentTypeService agentTypeService,
        IStateValidationService stateValidationService,
        IOperationStatusService operationStatusService,
        IUserDatabaseAccessService userDatabaseAccessService,
        IConfiguration configuration,
        IDataIsolationService dataIsolationService)
    {
        _organizationService = organizationService;
        _graphService = graphService;
        _securityGroupService = securityGroupService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
        _agentGroupAssignmentService = agentGroupAssignmentService;
        _teamsGroupService = teamsGroupService;
        _agentTypeService = agentTypeService;
        _stateValidationService = stateValidationService;
        _operationStatusService = operationStatusService;
        _userDatabaseAccessService = userDatabaseAccessService;
        _configuration = configuration;
        _dataIsolationService = dataIsolationService;
    }

    private bool IsDataverseAvailable()
    {
        // Always return false since we migrated to SQL Server
        _logger.LogInformation("Dataverse not used - migrated to SQL Server");
        return false;
    }

    public async Task<bool> CanInviteEmailAsync(Guid organizationId, string emailToInvite)
    {
        try
        {
            if (string.IsNullOrEmpty(emailToInvite) || !emailToInvite.Contains("@"))
            {
                return false;
            }

            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                _logger.LogWarning("Organization not found: {OrganizationId}", organizationId);
                return false;
            }

            // Use the extension method to validate domain
            return organization.CanInviteEmail(emailToInvite);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating email invitation for org {OrganizationId}: {Email}", 
                organizationId, emailToInvite);
            return false;
        }
    }

    public async Task<InvitationResult> InviteUserAsync(Guid organizationId, string emailToInvite, Guid invitedBy, List<LegacyAgentType> agentTypes)
    {
        // Call the enhanced method with empty agent type IDs and database IDs for backward compatibility
        // Default role based on legacy agent types: Admin agents -> OrgAdmin, others -> User
        var role = agentTypes.Contains(LegacyAgentType.Admin) ? UserRole.OrgAdmin : UserRole.User;
        var displayName = emailToInvite.Split('@')[0]; // Use email prefix as default display name for legacy calls
        return await InviteUserAsync(organizationId, emailToInvite, displayName, invitedBy, agentTypes, new List<Guid>(), new List<Guid>(), role);
    }

    /// <summary>
    /// Enhanced invitation method that supports both legacy and new agent-based group assignment
    /// </summary>
    public async Task<InvitationResult> InviteUserAsync(Guid organizationId, string emailToInvite, string displayName, Guid invitedBy, List<LegacyAgentType> agentTypes, List<Guid> agentTypeIds, List<Guid> selectedDatabaseIds, UserRole assignedRole = UserRole.User, string? currentUserEmail = null)
    {
        var operationId = Guid.NewGuid().ToString();
        await _operationStatusService.StartOperationAsync(operationId, "UserInvitation", $"Inviting {emailToInvite} to join organization");
        
        try
        {
            // Step 0: SECURITY - Prevent self-invitation
            if (!string.IsNullOrEmpty(currentUserEmail) && 
                string.Equals(currentUserEmail, emailToInvite, StringComparison.OrdinalIgnoreCase))
            {
                await _operationStatusService.CompleteOperationAsync(operationId, false, "Self-invitation not allowed");
                _logger.LogWarning("SECURITY: Self-invitation attempt blocked - user {CurrentUser} tried to invite themselves", currentUserEmail);
                return new InvitationResult
                {
                    Success = false,
                    Message = "You cannot invite yourself",
                    Errors = { "Self-invitation is not allowed for security reasons" }
                };
            }

            // Step 1: Validate domain permission
            await _operationStatusService.UpdateStatusAsync(operationId, "Validating invitation permissions...");
            if (!await CanInviteEmailAsync(organizationId, emailToInvite))
            {
                await _operationStatusService.CompleteOperationAsync(operationId, false, "Domain validation failed");
                return new InvitationResult
                {
                    Success = false,
                    Message = "Cannot invite users from outside your organization's domain",
                    Errors = { $"Email {emailToInvite} does not belong to your organization's domain" }
                };
            }

            await _operationStatusService.UpdateStatusAsync(operationId, "Loading organization details...");
            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                await _operationStatusService.CompleteOperationAsync(operationId, false, "Organization not found");
                return new InvitationResult
                {
                    Success = false,
                    Message = "Organization not found",
                    Errors = { "Invalid organization ID" }
                };
            }

            // Step 1.5: CRITICAL - Check organization user invitation permissions
            // This prevents restricted org admins from inviting users, but allows Super Admins full access
            var currentUserRole = _dataIsolationService.GetCurrentUserRole();
            var isSuperAdminOrHigher = currentUserRole == UserRole.SuperAdmin || currentUserRole == UserRole.Developer;
            
            if (!organization.AllowUserInvitations && !isSuperAdminOrHigher)
            {
                await _operationStatusService.CompleteOperationAsync(operationId, false, "User invitations disabled for this organization");
                _logger.LogWarning("🚫 USER INVITATION BLOCKED: Organization {OrganizationName} ({OrganizationId}) has user invitations disabled. Org Admin (role: {UserRole}) attempted to invite {EmailToInvite}", 
                    organization.Name, organizationId, currentUserRole, emailToInvite);
                return new InvitationResult
                {
                    Success = false,
                    Message = "User invitations are disabled for your organization",
                    Errors = { "Contact your Super Administrator to enable user invitation permissions for your organization" }
                };
            }
            else if (!organization.AllowUserInvitations && isSuperAdminOrHigher)
            {
                _logger.LogInformation("✅ SUPER ADMIN OVERRIDE: User {UserRole} bypassing organization invitation restrictions for {OrganizationName} to invite {EmailToInvite}", 
                    currentUserRole, organization.Name, emailToInvite);
            }

            // Step 2: Determine redirect URI and collect agent share URLs
            await _operationStatusService.UpdateStatusAsync(operationId, "Configuring user access settings...");
            var isAdminUser = agentTypes.Contains(LegacyAgentType.Admin);
            
            // Production vs Development redirect logic
            string baseUrl;
            string redirectUri;
            if (_configuration["ASPNETCORE_ENVIRONMENT"] == "Production")
            {
                baseUrl = _configuration["Production:BaseUrl"] ?? "https://adminconsole.erpure.ai";
                // In production, regular users redirect to external site, admins to admin console
                redirectUri = isAdminUser ? $"{baseUrl}/admin" : _configuration["Production:UserRedirectUrl"] ?? "https://www.erpure.ai";
            }
            else
            {
                // Development - all users redirect to localhost admin console
                baseUrl = "http://localhost:5243";
                redirectUri = isAdminUser ? $"{baseUrl}/admin" : $"{baseUrl}/user";
            }
            
            // Collect agent share URLs from database-driven agent types
            var agentShareUrls = new List<string>();
            if (agentTypeIds.Any())
            {
                var assignedAgentTypes = await _agentTypeService.GetAgentTypesByIdsAsync(agentTypeIds);
                agentShareUrls = assignedAgentTypes
                    .Where(at => !string.IsNullOrEmpty(at.AgentShareUrl))
                    .Select(at => at.AgentShareUrl!)
                    .ToList();
            }

            // Step 3: Send enhanced Azure AD B2B invitation via Graph API
            await _operationStatusService.UpdateStatusAsync(operationId, "Creating user in directory...");
            var graphInvitation = await _graphService.InviteGuestUserAsync(
                emailToInvite, 
                displayName, 
                organization.Name, 
                redirectUri, 
                agentShareUrls, 
                isAdminUser);
            if (!graphInvitation.Success)
            {
                await _operationStatusService.CompleteOperationAsync(operationId, false, "Azure AD invitation failed");
                return new InvitationResult
                {
                    Success = false,
                    Message = "Failed to send Azure AD invitation",
                    Errors = graphInvitation.Errors
                };
            }

            // Step 4: Create or Update OnboardedUser record in SQL Server - CHECK FOR EXISTING FIRST
            _logger.LogInformation("💾 Checking for existing user {Email} with Azure Object ID: {AzureObjectId}", 
                emailToInvite, graphInvitation.UserId);

            // CRITICAL FIX: Check for existing OnboardedUser by AzureObjectId first
            var existingUser = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.AzureObjectId == graphInvitation.UserId);

            OnboardedUser onboardedUser;
            bool userWasCreated = false;

            if (existingUser != null)
            {
                _logger.LogInformation("Found existing OnboardedUser {UserId} with AzureObjectId {AzureObjectId} - updating record instead of creating duplicate", 
                    existingUser.OnboardedUserId, graphInvitation.UserId);
                
                // Update existing user with new invitation details
                existingUser.Email = emailToInvite; // Update email in case it changed
                existingUser.AgentTypes = agentTypes; // Update legacy field
                existingUser.AgentTypeIds = agentTypeIds; // Update agent type IDs
                existingUser.RedirectUri = redirectUri; // Update redirect URI
                existingUser.LastInvitationDate = DateTime.UtcNow; // Update last invitation date
                existingUser.ModifiedOn = DateTime.UtcNow;
                existingUser.ModifiedBy = invitedBy;
                existingUser.IsActive = true; // Ensure user is active
                existingUser.StateCode = StateCode.Active;
                existingUser.StatusCode = StatusCode.Active;
                
                onboardedUser = existingUser;
            }
            else
            {
                _logger.LogInformation("No existing OnboardedUser found with AzureObjectId {AzureObjectId} - creating new record", 
                    graphInvitation.UserId);
                
                // Create new user record
                onboardedUser = new OnboardedUser
                {
                    OnboardedUserId = Guid.NewGuid(),
                    Name = displayName, // Use provided display name
                    Email = emailToInvite,
                    AzureObjectId = graphInvitation.UserId, // 🔑 CRITICAL: Store Azure AD Object ID for reliable lookups
                    OrganizationLookupId = organizationId,
                    OrganizationId = organizationId, // Set the foreign key property
                    AgentTypes = agentTypes, // Legacy field for backward compatibility
                    AgentTypeIds = agentTypeIds, // New database-driven agent type IDs
                    AssignedRole = assignedRole, // 🔑 NEW: Set the user role based on invitation flow
                    RedirectUri = redirectUri, // Custom redirect URI for this user type
                    LastInvitationDate = DateTime.UtcNow, // Track when invitation was sent
                    StateCode = StateCode.Active,
                    StatusCode = StatusCode.Active,
                    IsActive = true, // Ensure user is active for access validation
                    CreatedBy = invitedBy,
                    CreatedOn = DateTime.UtcNow,
                    ModifiedOn = DateTime.UtcNow,
                    ModifiedBy = invitedBy,
                    // Set required fields with defaults
                    FullName = displayName, // Use provided display name
                    AssignedDatabaseIds = new List<Guid>(), // Will be assigned by admin later
                    OwnerId = invitedBy, // Set the required OwnerId field
                    OwningUser = invitedBy // Set the owning user as well
                };
                
                userWasCreated = true;
            }

            // Save to SQL Server (create or update)
            await SaveOrUpdateOnboardedUserToSqlServer(onboardedUser, userWasCreated);
            
            // CRITICAL SECURITY ENHANCEMENT: Assign app roles to newly created users
            if (userWasCreated)
            {
                await AssignAppRoleToInvitedUserAsync(onboardedUser, assignedRole);
            }

            // Step 4.5: Assign selected databases to the user
            if (selectedDatabaseIds.Any())
            {
                await _operationStatusService.UpdateStatusAsync(operationId, "Assigning database access...");
                _logger.LogInformation("Assigning {DatabaseCount} databases to user {Email}", selectedDatabaseIds.Count, emailToInvite);
                
                var databaseAssignmentSuccess = await _userDatabaseAccessService.UpdateUserDatabaseAssignmentsAsync(
                    onboardedUser.OnboardedUserId, 
                    selectedDatabaseIds, 
                    organizationId, 
                    invitedBy.ToString()
                );
                
                if (databaseAssignmentSuccess)
                {
                    _logger.LogInformation("✅ Successfully assigned {DatabaseCount} databases to user {Email}", selectedDatabaseIds.Count, emailToInvite);
                    
                    // Update the OnboardedUser record with assigned database IDs
                    onboardedUser.AssignedDatabaseIds = selectedDatabaseIds;
                    await SaveOrUpdateOnboardedUserToSqlServer(onboardedUser, false); // Update existing record
                }
                else
                {
                    _logger.LogWarning("❌ Failed to assign databases to user {Email} - continuing with invitation", emailToInvite);
                }
            }
            else
            {
                _logger.LogInformation("No databases selected for user {Email} - skipping database assignment", emailToInvite);
            }

            // Step 5: Skip organization-based security groups - using agent-based Global Security Groups instead
            _logger.LogInformation("Skipping organization-specific security group assignment for user {Email} - using agent-based Global Security Groups", 
                emailToInvite);

            // Step 5.5: Assign user to agent-based security groups using AgentGroupAssignmentService
            await _operationStatusService.UpdateStatusAsync(operationId, "Granting permissions...");
            _logger.LogInformation("Processing agent type assignments for user {Email}", emailToInvite);
            _logger.LogInformation("Agent type IDs count: {Count}, IDs: {IDs}", agentTypeIds.Count, string.Join(", ", agentTypeIds));
            
            // Collect Teams App IDs from selected agent types for automatic installation
            List<string> teamsAppIds = new();
            
            if (agentTypeIds.Any())
            {
                await _operationStatusService.UpdateStatusAsync(operationId, "Setting up user groups...", 
                    $"Configuring access to {agentTypeIds.Count} agent types");
                _logger.LogInformation("Starting security group assignment for user {Email} with UserId {UserId}", emailToInvite, graphInvitation.UserId);
                
                // Get selected agent types for Teams App collection
                var selectedAgentTypes = await _agentTypeService.GetAgentTypesByIdsAsync(agentTypeIds);
                
                // Collect Teams App IDs from selected agent types for automatic installation
                teamsAppIds = selectedAgentTypes
                    .Where(agentType => !string.IsNullOrEmpty(agentType.TeamsAppId))
                    .Select(agentType => agentType.TeamsAppId!)
                    .ToList();
                
                _logger.LogInformation("Found {Count} Teams App IDs to install: {TeamsAppIds}", teamsAppIds.Count, string.Join(", ", teamsAppIds));
                
                // CRITICAL FIX: Use AgentGroupAssignmentService to properly create database records
                var assignmentSuccess = await _agentGroupAssignmentService.AssignUserToAgentTypeGroupsAsync(
                    graphInvitation.UserId, 
                    agentTypeIds, 
                    organizationId, 
                    invitedBy.ToString()
                );
                
                if (assignmentSuccess)
                {
                    _logger.LogInformation("✅ Successfully assigned user {UserId} to all requested agent type security groups", graphInvitation.UserId);
                    await _operationStatusService.UpdateStatusAsync(operationId, "Permissions granted successfully");
                }
                else
                {
                    _logger.LogError("❌ Failed to assign user {UserId} to some agent type security groups - check logs for details", graphInvitation.UserId);
                    await _operationStatusService.UpdateStatusAsync(operationId, "Warning: Some permissions could not be granted");
                }
            }
            else
            {
                _logger.LogInformation("No agent type IDs provided - skipping security group assignment");
            }

            // Step 5.6: FIXED M365 Teams group creation and assignment - CHECK FOR EXISTING GROUP FIRST
            _logger.LogInformation("🚀 Starting M365 Teams group creation/assignment for organization {OrganizationId}", organizationId);
            
            var orgForTeams = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (orgForTeams != null)
            {
                _logger.LogInformation("🔍 Organization found: Name='{Name}', M365GroupId='{M365GroupId}'", 
                    orgForTeams.Name, orgForTeams.M365GroupId ?? "null");
                    
                string? groupIdToUse = null;
                bool groupWasCreated = false;
                
                // CHECK FOR EXISTING M365 GROUP - WITH VALIDATION
                if (!string.IsNullOrEmpty(orgForTeams.M365GroupId))
                {
                    _logger.LogInformation("🔍 Organization {OrganizationId} has M365GroupId {GroupId} - validating if it still exists", 
                        organizationId, orgForTeams.M365GroupId);
                    
                    // Validate that the Teams group still exists in Azure AD
                    bool groupExists = false;
                    try
                    {
                        groupExists = await _graphService.GroupExistsAsync(orgForTeams.M365GroupId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error validating existing Teams group {GroupId} - treating as non-existent", orgForTeams.M365GroupId);
                        groupExists = false;
                    }
                    
                    if (groupExists)
                    {
                        _logger.LogInformation("✅ Existing Teams group {GroupId} verified - reusing", orgForTeams.M365GroupId);
                        groupIdToUse = orgForTeams.M365GroupId;
                    }
                    else
                    {
                        _logger.LogWarning("🚫 Existing M365GroupId {GroupId} no longer exists in Azure AD - will create new Teams group and update organization", 
                            orgForTeams.M365GroupId);
                        // Clear the invalid M365GroupId so we create a new one
                        var oldGroupId = orgForTeams.M365GroupId;
                        orgForTeams.M365GroupId = null;
                        var updateResult = await _organizationService.UpdateOrganizationAsync(orgForTeams);
                        _logger.LogInformation("📝 Cleared invalid M365GroupId {OldGroupId} from organization - update result: {UpdateResult}", 
                            oldGroupId, updateResult);
                    }
                }
                
                // Create new Teams group if we don't have a valid existing one
                if (string.IsNullOrEmpty(groupIdToUse))
                {
                    // Only create new group if organization doesn't have a valid one
                    var teamName = $"{orgForTeams.Name} - Team";
                    var description = $"Microsoft Teams collaboration space for {orgForTeams.Name}";
                    
                    _logger.LogInformation("🆕 Organization has no M365GroupId - creating new Teams group: {TeamName} for organization {OrganizationId}", 
                        teamName, organizationId);
                    
                    try
                    {
                        var teamsResult = await _graphService.CreateTeamsGroupAsync(teamName, description, organizationId.ToString(), teamsAppIds);
                    
                        _logger.LogInformation("📊 CreateTeamsGroupAsync result - Success: {Success}, GroupId: {GroupId}, ErrorCount: {ErrorCount}", 
                            teamsResult.Success, teamsResult.GroupId ?? "null", teamsResult.Errors?.Count ?? 0);
                        
                        if (teamsResult.Errors?.Any() == true)
                        {
                            _logger.LogError("❌ ERRORS during Teams group creation: {Errors}", 
                                string.Join(" | ", teamsResult.Errors));
                        }
                        
                        if (teamsResult.Success && !string.IsNullOrEmpty(teamsResult.GroupId))
                        {
                            _logger.LogInformation("Group created successfully. Saving Group ID {GroupId} to organization {OrganizationId}", 
                                teamsResult.GroupId, organizationId);
                            
                            // Save M365 Group ID back to organization
                            try
                            {
                                var organizationToUpdate = await _organizationService.GetByIdAsync(organizationId.ToString());
                                if (organizationToUpdate != null)
                                {
                                    organizationToUpdate.M365GroupId = teamsResult.GroupId;
                                    var updateResult = await _organizationService.UpdateOrganizationAsync(organizationToUpdate);
                                    
                                    if (updateResult)
                                    {
                                        _logger.LogInformation("SUCCESS - M365GroupId {GroupId} saved to organization {OrganizationId}", 
                                            teamsResult.GroupId, organizationId);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Update returned false when saving M365GroupId to organization {OrganizationId}", 
                                            organizationId);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("Could not find organization {OrganizationId} to update M365GroupId", 
                                        organizationId);
                                }
                            }
                            catch (Exception updateEx)
                            {
                                _logger.LogError(updateEx, "Failed to save M365GroupId to organization {OrganizationId}", 
                                    organizationId);
                                // Don't fail the entire invitation process if this update fails
                            }
                            
                            groupIdToUse = teamsResult.GroupId;
                            groupWasCreated = true;
                            
                            _logger.LogInformation("Waiting 5 seconds for group provisioning before adding user...");
                            
                            // Wait for group provisioning
                            await Task.Delay(5000);
                        }
                        else
                        {
                            throw new Exception($"CreateTeamsGroupAsync failed: {string.Join(", ", teamsResult.Errors ?? new List<string>())};");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create Teams group for organization {OrganizationId}", organizationId);
                        throw;
                    }
                }
                
                // Add user to the Teams group (whether existing or newly created)
                if (!string.IsNullOrEmpty(groupIdToUse))
                {
                    _logger.LogInformation("Adding user {UserId} to Teams group {GroupId} (created: {WasCreated})", 
                        graphInvitation.UserId, groupIdToUse, groupWasCreated);
                    
                    var userAdded = await _graphService.AddUserToTeamsGroupAsync(graphInvitation.UserId, groupIdToUse);
                    
                    _logger.LogInformation("AddUserToTeamsGroupAsync returned: {Success}", userAdded);
                    
                    if (userAdded)
                    {
                        _logger.LogInformation("SUCCESS - User {UserId} added to Teams group {GroupId}", 
                            graphInvitation.UserId, groupIdToUse);
                    }
                    else
                    {
                        _logger.LogWarning("FAILED - Could not add user {UserId} to Teams group {GroupId}", 
                            graphInvitation.UserId, groupIdToUse);
                    }
                }
                else
                {
                    _logger.LogWarning("No valid GroupId available for adding user to Teams group");
                }
            }
            else
            {
                _logger.LogWarning("Organization {OrganizationId} not found - skipping Teams group creation", organizationId);
            }

            // Step 6: Auto-assign Azure AD app role based on user type
            if (agentTypes.Contains(LegacyAgentType.Admin))
            {
                _logger.LogInformation("Assigning OrgAdmin app role to user {Email} with Admin agent type", emailToInvite);
                var roleAssigned = await _graphService.AssignAppRoleToUserAsync(graphInvitation.UserId, "OrgAdmin");
                
                if (roleAssigned)
                {
                    _logger.LogInformation("Successfully assigned OrgAdmin app role to user {Email}", emailToInvite);
                }
                else
                {
                    _logger.LogWarning("User {Email} was invited successfully but OrgAdmin app role assignment failed. User may need manual role assignment.", emailToInvite);
                    // Note: We don't fail the entire invitation if role assignment fails
                }
            }
            else if (agentTypeIds.Any())
            {
                // Non-admin user with agent types gets OrgUser role
                _logger.LogInformation("Assigning OrgUser app role to user {Email} with non-admin agent types", emailToInvite);
                var roleAssigned = await _graphService.AssignAppRoleToUserAsync(graphInvitation.UserId, "OrgUser");
                
                if (roleAssigned)
                {
                    _logger.LogInformation("Successfully assigned OrgUser app role to user {Email}", emailToInvite);
                }
                else
                {
                    _logger.LogWarning("User {Email} was invited successfully but OrgUser app role assignment failed. User may need manual role assignment.", emailToInvite);
                    // Note: We don't fail the entire invitation if role assignment fails
                }
            }
            else
            {
                _logger.LogInformation("User {Email} has no agent types - skipping app role assignment", emailToInvite);
            }

            _logger.LogInformation("Successfully invited user {Email} to organization {OrganizationId}", 
                emailToInvite, organizationId);

            // Build success message with enhanced features info
            var messageDetails = new List<string>();
            
            if (agentTypes.Contains(LegacyAgentType.Admin))
            {
                messageDetails.Add("OrgAdmin role assigned");
            }
            
            if (agentShareUrls.Any())
            {
                messageDetails.Add($"{agentShareUrls.Count} agent application(s) included");
            }
            
            messageDetails.Add($"Custom redirect URI: {redirectUri}");
            
            // Final validation
            await _operationStatusService.UpdateStatusAsync(operationId, "Finalizing invitation...");
            var finalValidation = await _stateValidationService.ValidateUserStateConsistencyAsync(graphInvitation.UserId, organizationId);
            if (!finalValidation.IsValid)
            {
                _logger.LogWarning("Post-invitation validation found issues: {Issues}", string.Join(", ", finalValidation.Errors));
                await _operationStatusService.UpdateStatusAsync(operationId, "Warning: Final validation found inconsistencies", 
                    string.Join(", ", finalValidation.Errors));
            }
            
            var message = $"Successfully invited {emailToInvite}";
            if (messageDetails.Any())
            {
                message += $" with {string.Join(", ", messageDetails)}";
            }

            await _operationStatusService.CompleteOperationAsync(operationId, true, message);

            // CRITICAL FIX: Clear caches immediately so invited user appears in UI right away
            try
            {
                _logger.LogInformation("🚀 Clearing user list caches after successful invitation for organization {OrganizationId}", organizationId);
                
                // Clear NavigationOptimizer cache (used by ManageUsers page)
                var navigationCacheKey = $"{NavigationOptimizationService.USER_LIST_CACHE_PREFIX}{organizationId}";
                _cache.Remove(navigationCacheKey);
                
                // Clear OnboardedUserService cache
                var userCacheKey = $"users_org_{organizationId}";
                _cache.Remove(userCacheKey);
                
                _logger.LogInformation("✅ Successfully cleared caches - newly invited user should appear immediately");
            }
            catch (Exception cacheEx)
            {
                // Cache clearing is not critical - log warning but don't fail the invitation
                _logger.LogWarning(cacheEx, "⚠️ Failed to clear caches after invitation - user may not appear immediately in UI");
            }

            return new InvitationResult
            {
                Success = true,
                Message = message,
                InvitationId = onboardedUser.OnboardedUserId.ToString(),
                InvitationUrl = graphInvitation.InvitationUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inviting user {Email} to organization {OrganizationId}", 
                emailToInvite, organizationId);
            
            await _operationStatusService.CompleteOperationAsync(operationId, false, $"Invitation failed: {ex.Message}");
            
            return new InvitationResult
            {
                Success = false,
                Message = "An error occurred while sending the invitation",
                Errors = { ex.Message }
            };
        }
    }

    public async Task<List<InvitationRecord>> GetPendingInvitationsAsync(Guid organizationId)
    {
        try
        {
            // Get pending users from SQL Server
            var pendingUsers = await _dbContext.OnboardedUsers
                .Where(u => u.OrganizationLookupId == organizationId && u.StateCode == StateCode.Inactive)
                .ToListAsync();
            
            return pendingUsers.Select(user => new InvitationRecord
            {
                Id = user.OnboardedUserId,
                Email = user.Email,
                OrganizationId = organizationId,
                OrganizationName = "", // We don't have organization navigation property loaded
                InvitedBy = user.CreatedBy ?? Guid.Empty,
                InvitedDate = user.CreatedOn,
                Status = user.GetInvitationStatus(),
                AgentTypes = user.AgentTypes
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending invitations for organization {OrganizationId}", organizationId);
            return new List<InvitationRecord>();
        }
    }

    public async Task<InvitationResult> ResendInvitationAsync(Guid invitationId)
    {
        try
        {
            _logger.LogInformation("📧 RESENDING INVITATION for invitation ID {InvitationId}", invitationId);

            // Step 1: Find the user in the database by the invitation ID (OnboardedUserId)
            var user = await _dbContext.OnboardedUsers
                .Where(u => u.OnboardedUserId == invitationId)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogError("❌ User not found for invitation ID {InvitationId}", invitationId);
                return new InvitationResult
                {
                    Success = false,
                    Message = "User not found for the given invitation ID"
                };
            }

            _logger.LogInformation("📋 Found user {Email} for invitation resend", user.Email);

            // Step 2: Check if user has already accepted (no need to resend)
            if (user.StateCode == StateCode.Active)
            {
                _logger.LogInformation("✅ User {Email} has already accepted invitation", user.Email);
                return new InvitationResult
                {
                    Success = true,
                    Message = $"User {user.Email} has already accepted the invitation"
                };
            }

            // Step 3: Check current invitation status via Azure AD
            var statusCheck = await _graphService.CheckInvitationStatusAsync(user.Email);
            if (statusCheck.InvitationStatus == InvitationStatus.Accepted)
            {
                // Update database to reflect accepted status
                user.StateCode = StateCode.Active;
                user.StatusCode = StatusCode.Active;
                user.ModifiedOn = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("✅ User {Email} had already accepted invitation - updated database", user.Email);
                return new InvitationResult
                {
                    Success = true,
                    Message = $"User {user.Email} has already accepted the invitation"
                };
            }

            // Step 4: Get organization details for proper invitation
            var organization = await _organizationService.GetByIdAsync((user.OrganizationLookupId ?? Guid.Empty).ToString());
            if (organization == null)
            {
                _logger.LogError("❌ Organization not found for user {Email}", user.Email);
                return new InvitationResult
                {
                    Success = false,
                    Message = "Organization not found for the user"
                };
            }

            // Step 5: Resend invitation via GraphService (creates new B2B invitation)
            _logger.LogInformation("🔄 Creating new B2B invitation to resend for {Email}", user.Email);
            
            var graphResult = await _graphService.InviteGuestUserAsync(
                user.Email, 
                user.FullName ?? user.Name ?? user.Email.Split('@')[0], // Use stored display name or fallback
                organization.Name,
                user.RedirectUri ?? "/home", // Use stored redirect URI or default
                new List<string>(), // Agent share URLs - would need to rebuild from user's agent types
                false // Regular user invitation
            );

            if (graphResult.Success)
            {
                // Step 6: Update database with resend information
                user.LastInvitationDate = DateTime.UtcNow;
                user.ModifiedOn = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("✅ Successfully resent invitation to {Email}", user.Email);
                
                // Clear caches after successful resend (user status may have changed)
                try
                {
                    var organizationId = user.OrganizationLookupId ?? Guid.Empty;
                    var navigationCacheKey = $"{NavigationOptimizationService.USER_LIST_CACHE_PREFIX}{organizationId}";
                    _cache.Remove(navigationCacheKey);
                    _cache.Remove($"users_org_{organizationId}");
                    _logger.LogInformation("🚀 Cleared caches after invitation resend");
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "⚠️ Failed to clear caches after invitation resend");
                }
                
                return new InvitationResult
                {
                    Success = true,
                    Message = $"Invitation successfully resent to {user.Email}",
                    InvitationId = invitationId.ToString(),
                    InvitationUrl = graphResult.InvitationUrl
                };
            }
            else
            {
                _logger.LogError("❌ Failed to resend Graph invitation to {Email}: {Errors}", 
                    user.Email, string.Join(", ", graphResult.Errors));
                
                return new InvitationResult
                {
                    Success = false,
                    Message = $"Failed to resend invitation to {user.Email}",
                    Errors = graphResult.Errors
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error resending invitation {InvitationId}", invitationId);
            return new InvitationResult
            {
                Success = false,
                Message = "An unexpected error occurred while resending the invitation",
                Errors = { ex.Message }
            };
        }
    }

    public async Task<bool> CancelInvitationAsync(Guid invitationId)
    {
        try
        {
            // Find the OnboardedUser by invitation ID
            var user = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == invitationId);
            
            if (user != null)
            {
                // Mark as inactive to cancel the invitation
                user.StateCode = StateCode.Inactive;
                user.StatusCode = StatusCode.Inactive;
                user.ModifiedOn = DateTime.UtcNow;
                
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Cancelled invitation for user {Email} (ID: {UserId})", user.Email, invitationId);
                return true;
            }
            
            _logger.LogWarning("Invitation not found for ID: {InvitationId}", invitationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling invitation {InvitationId}", invitationId);
            return false;
        }
    }

    private async Task SaveOrUpdateOnboardedUserToSqlServer(OnboardedUser user, bool isNewUser)
    {
        try
        {
            if (isNewUser)
            {
                _logger.LogInformation("Creating new OnboardedUser: {UserId} ({Email}) to SQL Server", user.OnboardedUserId, user.Email);
                _dbContext.OnboardedUsers.Add(user);
            }
            else
            {
                _logger.LogInformation("Updating existing OnboardedUser: {UserId} ({Email}) in SQL Server", user.OnboardedUserId, user.Email);
                _dbContext.OnboardedUsers.Update(user);
            }
            
            _logger.LogDebug("OnboardedUser details - OwnerId: {OwnerId}, OrganizationId: {OrgId}, Email: {Email}, AzureObjectId: {AzureObjectId}", 
                user.OwnerId, user.OrganizationLookupId, user.Email, user.AzureObjectId);
            
            await _dbContext.SaveChangesAsync();
            
            var action = isNewUser ? "saved" : "updated";
            _logger.LogInformation("Successfully {Action} OnboardedUser to SQL Server: {UserId} ({Email})", action, user.OnboardedUserId, user.Email);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            var action = isNewUser ? "saving" : "updating";
            _logger.LogError(dbEx, "Database update exception when {Action} OnboardedUser {UserId} ({Email}). Inner exception: {InnerException}", 
                action, user.OnboardedUserId, user.Email, dbEx.InnerException?.Message);
            throw; // Re-throw to be handled by the calling method
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General exception when saving OnboardedUser {UserId} ({Email}): {ErrorMessage}", 
                user.OnboardedUserId, user.Email, ex.Message);
            throw; // Re-throw to be handled by the calling method
        }
    }
    
    /// <summary>
    /// CRITICAL SECURITY ENHANCEMENT: Assigns appropriate app role to invited users
    /// Ensures invited users have proper Azure AD app role assignments
    /// </summary>
    /// <param name="user">The invited user</param>
    /// <param name="assignedRole">The role assigned to the user</param>
    private async Task AssignAppRoleToInvitedUserAsync(OnboardedUser user, UserRole assignedRole)
    {
        try
        {
            _logger.LogInformation("🔒 INVITATION APP ROLE: Processing invited user {Email} for app role assignment (Role: {Role})", 
                user.Email, assignedRole);
            
            // Check if user has Azure Object ID (they should from the invitation process)
            if (string.IsNullOrEmpty(user.AzureObjectId))
            {
                _logger.LogWarning("⚠️ INVITATION APP ROLE: No Azure Object ID found for invited user {Email} - app role assignment skipped", user.Email);
                return;
            }
            
            // Determine appropriate app role based on assigned role
            string targetAppRole = DetermineAppRoleFromUserRole(assignedRole);
            
            if (string.IsNullOrEmpty(targetAppRole))
            {
                _logger.LogInformation("ℹ️ INVITATION APP ROLE: No app role determined for invited user {Email} with role {Role}", 
                    user.Email, assignedRole);
                return;
            }
            
            _logger.LogInformation("🎯 INVITATION APP ROLE: Assigning {AppRole} to invited user {Email} ({AzureObjectId})", 
                targetAppRole, user.Email, user.AzureObjectId);
            
            // Assign the app role using GraphService
            bool roleAssigned = await _graphService.AssignAppRoleToUserAsync(user.AzureObjectId, targetAppRole);
            
            if (roleAssigned)
            {
                _logger.LogInformation("✅ INVITATION APP ROLE SUCCESS: Assigned {AppRole} to invited user {Email}", 
                    targetAppRole, user.Email);
            }
            else
            {
                _logger.LogWarning("⚠️ INVITATION APP ROLE WARNING: Failed to assign {AppRole} to invited user {Email}", 
                    targetAppRole, user.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ INVITATION APP ROLE ERROR: Failed to assign app role to invited user {Email}", user.Email);
            // Don't throw - app role assignment failure shouldn't block invitation
        }
    }
    
    /// <summary>
    /// Determines the appropriate app role based on the user's assigned role
    /// </summary>
    /// <param name="userRole">The role assigned to the user</param>
    /// <returns>App role name to assign</returns>
    private string DetermineAppRoleFromUserRole(UserRole userRole)
    {
        return userRole switch
        {
            UserRole.SuperAdmin or UserRole.Developer => "SuperAdmin",
            UserRole.OrgAdmin => "OrgAdmin",
            UserRole.User => "OrgUser",
            _ => "OrgUser" // Default to OrgUser for any unrecognized roles
        };
    }

}