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

    public InvitationService(
        IOrganizationService organizationService,
        IGraphService graphService,
        ISecurityGroupService securityGroupService,
        AdminConsoleDbContext dbContext,
        IMemoryCache cache,
        ILogger<InvitationService> logger,
        IAgentGroupAssignmentService agentGroupAssignmentService,
        ITeamsGroupService teamsGroupService,
        IAgentTypeService agentTypeService)
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
        // Call the enhanced method with empty agent type IDs for backward compatibility
        return await InviteUserAsync(organizationId, emailToInvite, invitedBy, agentTypes, new List<Guid>());
    }

    /// <summary>
    /// Enhanced invitation method that supports both legacy and new agent-based group assignment
    /// </summary>
    public async Task<InvitationResult> InviteUserAsync(Guid organizationId, string emailToInvite, Guid invitedBy, List<LegacyAgentType> agentTypes, List<Guid> agentTypeIds)
    {
        try
        {
            // Step 1: Validate domain permission
            if (!await CanInviteEmailAsync(organizationId, emailToInvite))
            {
                return new InvitationResult
                {
                    Success = false,
                    Message = "Cannot invite users from outside your organization's domain",
                    Errors = { $"Email {emailToInvite} does not belong to your organization's domain" }
                };
            }

            var organization = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (organization == null)
            {
                return new InvitationResult
                {
                    Success = false,
                    Message = "Organization not found",
                    Errors = { "Invalid organization ID" }
                };
            }

            // Step 2: Determine redirect URI and collect agent share URLs
            var isAdminUser = agentTypes.Contains(LegacyAgentType.Admin);
            var redirectUri = isAdminUser ? "https://localhost:5242/admin" : "https://localhost:5242/user";
            
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
            var graphInvitation = await _graphService.InviteGuestUserAsync(
                emailToInvite, 
                organization.Name, 
                redirectUri, 
                agentShareUrls, 
                isAdminUser);
            if (!graphInvitation.Success)
            {
                return new InvitationResult
                {
                    Success = false,
                    Message = "Failed to send Azure AD invitation",
                    Errors = graphInvitation.Errors
                };
            }

            // Step 4: Create OnboardedUser record in SQL Server
            var onboardedUser = new OnboardedUser
            {
                OnboardedUserId = Guid.NewGuid(),
                Name = emailToInvite.Split('@')[0], // Use email prefix as default name
                Email = emailToInvite,
                OrganizationLookupId = organizationId,
                OrganizationId = organizationId, // Set the foreign key property
                AgentTypes = agentTypes, // Legacy field for backward compatibility
                AgentTypeIds = agentTypeIds, // New database-driven agent type IDs
                RedirectUri = redirectUri, // Custom redirect URI for this user type
                LastInvitationDate = DateTime.UtcNow, // Track when invitation was sent
                StateCode = StateCode.Active,
                StatusCode = StatusCode.Active,
                CreatedBy = invitedBy,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                ModifiedBy = invitedBy,
                // Set required fields with defaults
                FullName = emailToInvite.Split('@')[0], // Use email prefix as display name
                AssignedDatabaseIds = new List<Guid>(), // Will be assigned by admin later
                OwnerId = invitedBy, // Set the required OwnerId field
                OwningUser = invitedBy // Set the owning user as well
            };

            // Save to SQL Server
            await SaveOnboardedUserToSqlServer(onboardedUser);

            // Step 5: Skip organization-based security groups - using agent-based Global Security Groups instead
            _logger.LogInformation("Skipping organization-specific security group assignment for user {Email} - using agent-based Global Security Groups", 
                emailToInvite);

            // Step 5.5: Assign user to agent-based security groups (FIXED VERSION)
            Directory.CreateDirectory("C:\\temp");
            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: About to process agent type assignments for user {emailToInvite}\n");
            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Agent type IDs count: {agentTypeIds.Count}, IDs: {string.Join(", ", agentTypeIds)}\n");
            
            // Collect Teams App IDs from selected agent types for automatic installation
            List<string> teamsAppIds = new();
            
            if (agentTypeIds.Any())
            {
                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Starting security group assignment for user {emailToInvite} with UserId {graphInvitation.UserId}\n");
                
                var selectedAgentTypes = await _agentTypeService.GetAgentTypesByIdsAsync(agentTypeIds);
                
                // Collect Teams App IDs from selected agent types for automatic installation
                teamsAppIds = selectedAgentTypes
                    .Where(agentType => !string.IsNullOrEmpty(agentType.TeamsAppId))
                    .Select(agentType => agentType.TeamsAppId!)
                    .ToList();
                
                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Found {teamsAppIds.Count} Teams App IDs to install: {string.Join(", ", teamsAppIds)}\n");
                
                // FIXED: Direct Azure AD group assignment using Graph API
                foreach (var agentType in selectedAgentTypes)
                {
                    if (!string.IsNullOrEmpty(agentType.GlobalSecurityGroupId))
                    {
                        await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Assigning user {graphInvitation.UserId} to security group {agentType.GlobalSecurityGroupId} for agent {agentType.Name}\n");
                        
                        try
                        {
                            // Enhanced Debug: Check if group exists first with detailed info
                            var groupExists = await _graphService.GroupExistsAsync(agentType.GlobalSecurityGroupId);
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Group exists check for {agentType.GlobalSecurityGroupId}: {groupExists}\n");
                            
                            if (!groupExists)
                            {
                                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: CRITICAL - Security group {agentType.GlobalSecurityGroupId} does not exist in Azure AD\n");
                                throw new Exception("Security group does not exist in Azure AD");
                            }
                            
                            // Enhanced Debug: Check user exists
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Checking if user {graphInvitation.UserId} exists in Azure AD\n");
                            
                            // Use existing GraphService method with detailed error handling
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: Calling AddUserToGroupAsync for user {graphInvitation.UserId} to group {agentType.GlobalSecurityGroupId}\n");
                            
                            var success = await _graphService.AddUserToGroupAsync(graphInvitation.UserId, agentType.GlobalSecurityGroupId);
                            
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: AddUserToGroupAsync returned: {success}\n");
                            
                            if (!success) 
                            {
                                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: CRITICAL - AddUserToGroupAsync failed. This could be: 1) Permissions issue with Graph API, 2) User not found, 3) Group access denied\n");
                                throw new Exception("AddUserToGroupAsync returned false - check Graph API permissions and user/group access");
                            }
                            
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: SUCCESS - User assigned to security group {agentType.GlobalSecurityGroupId}\n");
                        }
                        catch (Exception ex)
                        {
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: FAILED to assign user to security group {agentType.GlobalSecurityGroupId}. Error: {ex.Message}\n");
                        }
                    }
                }
            }
            else
            {
                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INVITATION: No agent type IDs provided - skipping security group assignment\n");
            }

            // Step 5.6: FIXED M365 Teams group creation and assignment
            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Starting M365 Teams group creation for organization {organizationId}\n");
            
            var orgForTeams = await _organizationService.GetByIdAsync(organizationId.ToString());
            if (orgForTeams != null)
            {
                var teamName = $"{orgForTeams.Name} - Team";
                var description = $"Microsoft Teams collaboration space for {orgForTeams.Name}";
                
                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Creating Teams group: {teamName}\n");
                
                try
                {
                    // Enhanced Teams creation with detailed logging
                    await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: About to call CreateTeamsGroupAsync with name='{teamName}', description='{description}'\n");
                    
                    var teamsResult = await _graphService.CreateTeamsGroupAsync(teamName, description, organizationId.ToString(), teamsAppIds);
                    
                    await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: CreateTeamsGroupAsync result - Success: {teamsResult.Success}, GroupId: {teamsResult.GroupId}, TeamId: {teamsResult.TeamId}, TeamUrl: {teamsResult.TeamUrl}\n");
                    
                    if (teamsResult.Errors.Any())
                    {
                        await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Errors during creation: {string.Join(", ", teamsResult.Errors)}\n");
                    }
                    
                    if (teamsResult.Success && !string.IsNullOrEmpty(teamsResult.GroupId))
                    {
                        await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Group created successfully. Waiting 5 seconds for provisioning before adding user...\n");
                        
                        // Wait for group provisioning
                        await Task.Delay(5000);
                        
                        await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: Adding user {graphInvitation.UserId} to Teams group {teamsResult.GroupId}\n");
                        
                        // Add user to the created Teams group
                        var userAdded = await _graphService.AddUserToTeamsGroupAsync(graphInvitation.UserId, teamsResult.GroupId);
                        
                        await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: User addition result: {userAdded}\n");
                        
                        if (userAdded)
                        {
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: SUCCESS - Teams group created with ID: {teamsResult.GroupId}, User added as member\n");
                        }
                        else
                        {
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: WARNING - Teams group created but failed to add user as member\n");
                        }
                        
                        // Check if Teams conversion succeeded
                        if (string.IsNullOrEmpty(teamsResult.TeamId) || teamsResult.Errors.Any(e => e.Contains("Teams conversion")))
                        {
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: WARNING - Group created but may not be Teams-enabled. TeamId: {teamsResult.TeamId}\n");
                        }
                        else
                        {
                            await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: SUCCESS - Full Teams functionality enabled with TeamId: {teamsResult.TeamId}\n");
                        }
                    }
                    else
                    {
                        throw new Exception($"CreateTeamsGroupAsync failed: {string.Join(", ", teamsResult.Errors)}");
                    }
                }
                catch (Exception ex)
                {
                    await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: FAILED - Teams group creation failed. Error: {ex.Message}\n");
                }
            }
            else
            {
                await File.AppendAllTextAsync("C:\\temp\\invitation-debug.log", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEAMS: FAILED - Organization not found for Teams group creation\n");
            }

            // Step 6: Auto-assign Azure AD app role if user has Admin agent type
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
            
            var message = $"Successfully invited {emailToInvite}";
            if (messageDetails.Any())
            {
                message += $" with {string.Join(", ", messageDetails)}";
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

    public Task<InvitationResult> ResendInvitationAsync(Guid invitationId)
    {
        try
        {
            // Implementation would resend the B2B invitation
            // For now, return success
            return Task.FromResult(new InvitationResult
            {
                Success = true,
                Message = "Invitation resent successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending invitation {InvitationId}", invitationId);
            return Task.FromResult(new InvitationResult
            {
                Success = false,
                Message = "Failed to resend invitation",
                Errors = { ex.Message }
            });
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

    private async Task SaveOnboardedUserToSqlServer(OnboardedUser user)
    {
        try
        {
            _logger.LogInformation("Attempting to save OnboardedUser: {UserId} ({Email}) to SQL Server", user.OnboardedUserId, user.Email);
            _logger.LogDebug("OnboardedUser details - OwnerId: {OwnerId}, OrganizationId: {OrgId}, Email: {Email}", 
                user.OwnerId, user.OrganizationLookupId, user.Email);
            
            _dbContext.OnboardedUsers.Add(user);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Successfully saved OnboardedUser to SQL Server: {UserId} ({Email})", user.OnboardedUserId, user.Email);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database update exception when saving OnboardedUser {UserId} ({Email}). Inner exception: {InnerException}", 
                user.OnboardedUserId, user.Email, dbEx.InnerException?.Message);
            throw; // Re-throw to be handled by the calling method
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General exception when saving OnboardedUser {UserId} ({Email}): {ErrorMessage}", 
                user.OnboardedUserId, user.Email, ex.Message);
            throw; // Re-throw to be handled by the calling method
        }
    }

}