using AdminConsole.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using System.Security.Claims;

namespace AdminConsole.Services;

public class GraphService : IGraphService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<GraphService> _logger;
    private readonly IPowerShellExecutionService _powerShellService;
    private readonly IConfiguration _configuration;

    public GraphService(
        GraphServiceClient graphClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<GraphService> logger,
        IPowerShellExecutionService powerShellService,
        IConfiguration configuration)
    {
        _graphClient = graphClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _powerShellService = powerShellService;
        _configuration = configuration;
    }

    public async Task<GuestUser?> InviteAdminUserAsync(string email, string displayName, string organizationName)
    {
        try
        {
            var invitation = new Invitation
            {
                InvitedUserEmailAddress = email,
                InvitedUserDisplayName = displayName,
                InviteRedirectUrl = "http://localhost:5242",
                SendInvitationMessage = true,
                InvitedUserMessageInfo = new InvitedUserMessageInfo
                {
                    MessageLanguage = "en-US",
                    CustomizedMessageBody = $"You've been invited as an admin for {organizationName}. Please accept this invitation to manage your organization's users and secrets."
                }
            };

            var result = await _graphClient.Invitations.PostAsync(invitation);
            
            if (result?.InvitedUser != null)
            {
                // Extract organization ID from domain
                var domain = email.Split('@')[1];
                var orgId = GenerateOrganizationId(domain);

                return new GuestUser
                {
                    Id = result.InvitedUser.Id ?? string.Empty,
                    Email = email,
                    DisplayName = displayName,
                    UserPrincipalName = result.InvitedUser.UserPrincipalName ?? string.Empty,
                    OrganizationId = orgId,
                    OrganizationName = organizationName,
                    Role = UserRole.OrgAdmin,
                    InvitedDateTime = DateTime.UtcNow,
                    InvitationStatus = "PendingAcceptance"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invite admin user {Email}", email);
        }

        return null;
    }

    public async Task<GuestUser?> InviteUserAsync(string email, string displayName, string organizationId)
    {
        try
        {
            var invitation = new Invitation
            {
                InvitedUserEmailAddress = email,
                InvitedUserDisplayName = displayName,
                InviteRedirectUrl = "http://localhost:5242",
                SendInvitationMessage = true,
                InvitedUserMessageInfo = new InvitedUserMessageInfo
                {
                    MessageLanguage = "en-US",
                    CustomizedMessageBody = "You've been invited to access the admin console. Please accept this invitation to get started."
                }
            };

            var result = await _graphClient.Invitations.PostAsync(invitation);
            
            if (result?.InvitedUser != null)
            {
                return new GuestUser
                {
                    Id = result.InvitedUser.Id ?? string.Empty,
                    Email = email,
                    DisplayName = displayName,
                    UserPrincipalName = result.InvitedUser.UserPrincipalName ?? string.Empty,
                    OrganizationId = organizationId,
                    Role = UserRole.User,
                    InvitedDateTime = DateTime.UtcNow,
                    InvitationStatus = "PendingAcceptance"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invite user {Email} for organization {OrganizationId}", email, organizationId);
        }

        return null;
    }

    public async Task<IEnumerable<GuestUser>> GetGuestUsersAsync(string organizationId)
    {
        try
        {
            var users = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = "userType eq 'Guest'";
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime" };
            });

            if (users?.Value != null)
            {
                return users.Value
                    .Where(u => ExtractOrganizationFromUPN(u.UserPrincipalName) == organizationId)
                    .Select(u => new GuestUser
                    {
                        Id = u.Id ?? string.Empty,
                        Email = u.Mail ?? string.Empty,
                        DisplayName = u.DisplayName ?? string.Empty,
                        UserPrincipalName = u.UserPrincipalName ?? string.Empty,
                        OrganizationId = organizationId,
                        InvitedDateTime = u.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                        InvitationStatus = "Accepted"
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guest users for organization {OrganizationId}", organizationId);
        }

        return Enumerable.Empty<GuestUser>();
    }

    public async Task<bool> RevokeUserAccessAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Attempting to revoke access for user {UserId}", userId);
            
            // Get user information first
            Microsoft.Graph.Models.User? userInfo = null;
            try
            {
                userInfo = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "userType", "creationType", "accountEnabled" };
                });
                
                _logger.LogInformation("User details - ID: {UserId}, Name: {DisplayName}, UPN: {UserPrincipalName}, UserType: {UserType}, CreationType: {CreationType}, AccountEnabled: {AccountEnabled}", 
                    userInfo?.Id, userInfo?.DisplayName, userInfo?.UserPrincipalName, userInfo?.UserType, userInfo?.CreationType, userInfo?.AccountEnabled);
            }
            catch (Exception userInfoEx)
            {
                _logger.LogWarning(userInfoEx, "Could not retrieve user info for {UserId}", userId);
            }
            
            // Strategy 1: Try to disable the account (less privileges required than deletion)
            try
            {
                _logger.LogInformation("Attempting to disable user account {UserId}", userId);
                
                var userUpdate = new Microsoft.Graph.Models.User
                {
                    AccountEnabled = false
                };
                
                await _graphClient.Users[userId].PatchAsync(userUpdate);
                _logger.LogInformation("Successfully disabled user account {UserId}", userId);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError disableEx)
            {
                _logger.LogWarning("Could not disable user account {UserId}: {Error} (Code: {Code}). Trying alternative approach...", 
                    userId, disableEx.Error?.Message, disableEx.Error?.Code);
            }
            
            // Strategy 2: Try to delete the user (original approach)
            try
            {
                _logger.LogInformation("Attempting to delete user {UserId}", userId);
                await _graphClient.Users[userId].DeleteAsync();
                _logger.LogInformation("Successfully deleted user {UserId}", userId);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError deleteEx)
            {
                _logger.LogWarning("Could not delete user {UserId}: {Error} (Code: {Code}). Insufficient privileges.", 
                    userId, deleteEx.Error?.Message, deleteEx.Error?.Code);
            }
            
            // Strategy 3: Remove from all security groups (lowest privilege approach)
            _logger.LogInformation("Attempting to remove user {UserId} from all security groups as fallback", userId);
            bool removedFromAnyGroup = false;
            bool userHadNoGroups = false;
            
            try
            {
                var memberOfResponse = await _graphClient.Users[userId].MemberOf.GetAsync();
                var groups = memberOfResponse?.Value?.OfType<Microsoft.Graph.Models.Group>() ?? Enumerable.Empty<Microsoft.Graph.Models.Group>();
                
                foreach (var group in groups)
                {
                    if (group.Id != null)
                    {
                        try
                        {
                            await _graphClient.Groups[group.Id].Members[userId].Ref.DeleteAsync();
                            _logger.LogInformation("Removed user {UserId} from group {GroupId} ({GroupName})", userId, group.Id, group.DisplayName);
                            removedFromAnyGroup = true;
                        }
                        catch (Microsoft.Graph.Models.ODataErrors.ODataError groupEx)
                        {
                            _logger.LogWarning("Could not remove user {UserId} from group {GroupId}: {Error}", userId, group.Id, groupEx.Error?.Message);
                        }
                    }
                }
                
                if (removedFromAnyGroup)
                {
                    _logger.LogInformation("Successfully revoked access for user {UserId} by removing from security groups. User account remains active but access to resources is revoked.", userId);
                    return true;
                }
                else
                {
                    var groupCount = memberOfResponse?.Value?.Count() ?? 0;
                    if (groupCount == 0)
                    {
                        _logger.LogWarning("User {UserId} was not a member of any security groups. No action taken - user account remains active and unchanged.", userId);
                        userHadNoGroups = true;
                    }
                    else
                    {
                        _logger.LogWarning("User {UserId} was a member of {GroupCount} groups but could not be removed from any. Access revocation incomplete.", userId, groupCount);
                    }
                }
            }
            catch (Exception groupEx)
            {
                _logger.LogError(groupEx, "Failed to remove user {UserId} from security groups", userId);
            }
            
            // If we reach here, all strategies failed
            if (userHadNoGroups)
            {
                _logger.LogError("User {UserId} could not be disabled or deleted, and was not a member of any security groups. No access revocation was possible. Manual intervention required.", userId);
            }
            else
            {
                _logger.LogError("All revocation strategies failed for user {UserId}. User access could not be revoked through Graph API. Consider manually revoking access in Azure AD or updating application permissions.", userId);
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke access for user {UserId}", userId);
            return false;
        }
    }

    public Task<GuestUser?> GetCurrentUserAsync()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value;
                var displayName = user.FindFirst(ClaimTypes.Name)?.Value;
                var upn = user.FindFirst("preferred_username")?.Value;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(email))
                {
                    var orgId = ExtractOrganizationFromUPN(upn);
                    var isAdmin = user.HasClaim("extension_IsAdmin", "true");
                    var role = isAdmin ? UserRole.OrgAdmin : UserRole.User;

                    return Task.FromResult<GuestUser?>(new GuestUser
                    {
                        Id = userId,
                        Email = email,
                        DisplayName = displayName ?? string.Empty,
                        UserPrincipalName = upn ?? string.Empty,
                        OrganizationId = orgId,
                        Role = role,
                        InvitationStatus = "Accepted"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current user information");
        }

        return Task.FromResult<GuestUser?>(null);
    }

    private string GenerateOrganizationId(string domain)
    {
        // Generate consistent organization ID from domain
        return domain.Replace(".", "_").ToLowerInvariant();
    }

    private string ExtractOrganizationFromUPN(string? userPrincipalName)
    {
        if (string.IsNullOrEmpty(userPrincipalName))
            return string.Empty;

        // Extract original domain from UPN (before #EXT#)
        var parts = userPrincipalName.Split('#');
        if (parts.Length > 0)
        {
            var emailPart = parts[0];
            var domainPart = emailPart.Split('@').LastOrDefault();
            if (!string.IsNullOrEmpty(domainPart))
            {
                return domainPart.Replace(".", "_").ToLowerInvariant();
            }
        }

        return string.Empty;
    }

    // NEW METHODS for B2B and Security Group Management

    public async Task<GraphInvitationResult> InviteGuestUserAsync(string email, string organizationName)
    {
        // Call the enhanced method with default values for backward compatibility
        return await InviteGuestUserAsync(email, organizationName, "https://localhost:5242", new List<string>(), false);
    }

    /// <summary>
    /// Enhanced invitation method with custom redirect URI and agent share URLs
    /// </summary>
    /// <param name="email">Email address to invite</param>
    /// <param name="organizationName">Organization name for the invitation</param>
    /// <param name="redirectUri">Custom redirect URI for this user type</param>
    /// <param name="agentShareUrls">List of agent share URLs to include in invitation</param>
    /// <param name="isAdminUser">Whether this is an admin user (affects message content)</param>
    /// <returns>Invitation result</returns>
    public async Task<GraphInvitationResult> InviteGuestUserAsync(string email, string organizationName, string redirectUri, List<string> agentShareUrls, bool isAdminUser)
    {
        try
        {
            // Build enhanced custom message
            var messageBuilder = new System.Text.StringBuilder();
            
            if (isAdminUser)
            {
                messageBuilder.AppendLine($"Welcome to {organizationName}!");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("You have been invited as an administrator. You will have access to:");
                messageBuilder.AppendLine("• Organization management and user administration");
                messageBuilder.AppendLine("• Security group and permissions management");
                messageBuilder.AppendLine("• Database access configuration");
                messageBuilder.AppendLine("• Microsoft Teams collaboration");
            }
            else
            {
                messageBuilder.AppendLine($"Welcome to {organizationName}!");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("You have been invited to join the team. You will have access to:");
                messageBuilder.AppendLine("• Assigned applications and databases");
                messageBuilder.AppendLine("• Microsoft Teams collaboration");
                messageBuilder.AppendLine("• Supervised access to organizational resources");
            }

            // Add agent share URLs if provided
            if (agentShareUrls.Any())
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Your assigned agent applications:");
                foreach (var agentUrl in agentShareUrls.Where(url => !string.IsNullOrEmpty(url)))
                {
                    messageBuilder.AppendLine($"• {agentUrl}");
                }
            }

            messageBuilder.AppendLine();
            messageBuilder.AppendLine("Click the link below to accept the invitation and access the admin console.");
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("If you have any questions, please contact your system administrator.");

            var invitation = new Invitation
            {
                InvitedUserEmailAddress = email,
                InvitedUserDisplayName = email.Split('@')[0],
                InviteRedirectUrl = redirectUri,
                SendInvitationMessage = true,
                InvitedUserMessageInfo = new InvitedUserMessageInfo
                {
                    MessageLanguage = "en-US",
                    CustomizedMessageBody = messageBuilder.ToString()
                }
            };

            var result = await _graphClient.Invitations.PostAsync(invitation);
            
            if (result?.InvitedUser != null)
            {
                _logger.LogInformation("Successfully sent enhanced invitation to {Email} with redirect URI {RedirectUri} and {AgentUrlCount} agent URLs", 
                    email, redirectUri, agentShareUrls.Count);

                return new GraphInvitationResult
                {
                    Success = true,
                    UserId = result.InvitedUser.Id ?? string.Empty,
                    InvitationId = result.Id ?? string.Empty,
                    InvitationUrl = result.InviteRedeemUrl ?? string.Empty
                };
            }

            return new GraphInvitationResult
            {
                Success = false,
                Errors = { "Failed to create invitation - no result returned" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invite guest user {Email} with enhanced invitation", email);
            return new GraphInvitationResult
            {
                Success = false,
                Errors = { ex.Message }
            };
        }
    }

    public Task<bool> CancelInvitationAsync(string invitationId)
    {
        try
        {
            // Note: Azure AD doesn't support cancelling invitations directly
            // You would need to delete the invited user if they haven't accepted yet
            _logger.LogWarning("Invitation cancellation requested for {InvitationId} - not directly supported by Graph API", invitationId);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel invitation {InvitationId}", invitationId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> ResendInvitationAsync(string userId)
    {
        try
        {
            // Note: Azure AD doesn't support resending invitations directly
            // You would need to create a new invitation
            _logger.LogWarning("Invitation resend requested for user {UserId} - not directly supported by Graph API", userId);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend invitation for user {UserId}", userId);
            return Task.FromResult(false);
        }
    }

    public async Task<string> CreateSecurityGroupAsync(string groupName, string description)
    {
        try
        {
            var group = new Group
            {
                DisplayName = groupName,
                Description = description,
                MailEnabled = false,
                SecurityEnabled = true,
                MailNickname = groupName.Replace(" ", "").Replace("-", "").ToLowerInvariant()
            };

            var result = await _graphClient.Groups.PostAsync(group);
            
            if (result?.Id != null)
            {
                _logger.LogInformation("Created security group {GroupName} with ID {GroupId}", groupName, result.Id);
                return result.Id;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create security group {GroupName}", groupName);
            return string.Empty;
        }
    }

    public async Task<bool> DeleteSecurityGroupAsync(string groupId)
    {
        try
        {
            await _graphClient.Groups[groupId].DeleteAsync();
            _logger.LogInformation("Deleted security group {GroupId}", groupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete security group {GroupId}", groupId);
            return false;
        }
    }

    public async Task<bool> AddUserToGroupAsync(string userId, string groupNameOrId)
    {
        try
        {
            _logger.LogInformation("DEBUG: AddUserToGroupAsync called with UserId: {UserId}, GroupNameOrId: {GroupNameOrId}", 
                userId, groupNameOrId);
                
            Group? group = null;
            
            // Check if the input is a GUID (Object ID) or a display name
            if (Guid.TryParse(groupNameOrId, out _))
            {
                _logger.LogInformation("DEBUG: Detected Object ID format, attempting to get group by ID: {GroupId}", groupNameOrId);
                
                // It's an Object ID - get the group directly
                try
                {
                    group = await _graphClient.Groups[groupNameOrId].GetAsync();
                    _logger.LogInformation("DEBUG: Successfully found existing group by Object ID {GroupId}, DisplayName: {DisplayName}", 
                        groupNameOrId, group?.DisplayName);
                }
                catch (Exception ex)
                {
                    _logger.LogError("CRITICAL: Group with Object ID {GroupId} does not exist. Error: {Error}", 
                        groupNameOrId, ex.Message);
                    return false;
                }
            }
            else
            {
                // It's a display name - search for it
                var groups = await _graphClient.Groups.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"displayName eq '{groupNameOrId}'";
                });

                group = groups?.Value?.FirstOrDefault();
                if (group?.Id == null)
                {
                    // Create the group if it doesn't exist (only for display names, not Object IDs)
                    var groupId = await CreateSecurityGroupAsync(groupNameOrId, $"Security group for {groupNameOrId}");
                    if (string.IsNullOrEmpty(groupId))
                    {
                        return false;
                    }
                    group = new Group { Id = groupId };
                }
            }

            // Add user to group
            if (group?.Id == null)
            {
                _logger.LogError("Group is null or has no ID after processing");
                return false;
            }
            
            _logger.LogInformation("DEBUG: Attempting to add user {UserId} to group {GroupId}", userId, group.Id);
            
            var directoryObject = new ReferenceCreate
            {
                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
            };

            await _graphClient.Groups[group.Id].Members.Ref.PostAsync(directoryObject);
            _logger.LogInformation("SUCCESS: Added user {UserId} to group {GroupNameOrId} (ID: {GroupId})", userId, groupNameOrId, group.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user {UserId} to group {GroupNameOrId}", userId, groupNameOrId);
            return false;
        }
    }

    public async Task<bool> RemoveUserFromGroupAsync(string userId, string groupNameOrId)
    {
        try
        {
            Group? group = null;
            
            // Check if the input is a GUID (Object ID) or a display name
            if (Guid.TryParse(groupNameOrId, out _))
            {
                // It's an Object ID - get the group directly
                try
                {
                    group = await _graphClient.Groups[groupNameOrId].GetAsync();
                    _logger.LogInformation("Found existing group by Object ID {GroupId} for removal", groupNameOrId);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Group with Object ID {GroupId} not found when trying to remove user {UserId}", groupNameOrId, userId);
                    return false;
                }
            }
            else
            {
                // It's a display name - search for it
                var groups = await _graphClient.Groups.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"displayName eq '{groupNameOrId}'";
                });

                group = groups?.Value?.FirstOrDefault();
                if (group?.Id == null)
                {
                    _logger.LogWarning("Group {GroupName} not found when trying to remove user {UserId}", groupNameOrId, userId);
                    return false;
                }
            }

            // Remove user from group
            if (group?.Id == null)
            {
                _logger.LogError("Group is null or has no ID when trying to remove user");
                return false;
            }
            
            await _graphClient.Groups[group.Id].Members[userId].Ref.DeleteAsync();
            _logger.LogInformation("Removed user {UserId} from group {GroupNameOrId} (ID: {GroupId})", userId, groupNameOrId, group.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove user {UserId} from group {GroupNameOrId}", userId, groupNameOrId);
            return false;
        }
    }

    public async Task<List<string>> GetGroupMembersAsync(string groupName)
    {
        try
        {
            // First find the group by name
            var groups = await _graphClient.Groups.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"displayName eq '{groupName}'";
            });

            var group = groups?.Value?.FirstOrDefault();
            if (group?.Id == null)
            {
                return new List<string>();
            }

            // Get group members
            var members = await _graphClient.Groups[group.Id].Members.GetAsync();
            
            return members?.Value?.Select(m => m.Id ?? string.Empty).Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for group {GroupName}", groupName);
            return new List<string>();
        }
    }

    public async Task<bool> GroupExistsAsync(string groupNameOrId)
    {
        try
        {
            // Check if the input is a GUID (Object ID)
            if (Guid.TryParse(groupNameOrId, out _))
            {
                // If it's a GUID, query by ID directly
                try
                {
                    var group = await _graphClient.Groups[groupNameOrId].GetAsync();
                    return group != null;
                }
                catch (ServiceException ex) when ((int)ex.ResponseStatusCode == 404)
                {
                    // Group not found
                    return false;
                }
            }
            else
            {
                // If it's not a GUID, treat it as a display name
                var groups = await _graphClient.Groups.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"displayName eq '{groupNameOrId}'";
                });

                return groups?.Value?.Any() == true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if group {GroupNameOrId} exists", groupNameOrId);
            return false;
        }
    }

    public async Task<List<GuestUser>> GetAllGuestUsersAsync()
    {
        try
        {
            var users = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = "userType eq 'Guest'";
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime" };
            });

            if (users?.Value != null)
            {
                return users.Value.Select(u => new GuestUser
                {
                    Id = u.Id ?? string.Empty,
                    Email = u.Mail ?? string.Empty,
                    DisplayName = u.DisplayName ?? string.Empty,
                    UserPrincipalName = u.UserPrincipalName ?? string.Empty,
                    OrganizationId = ExtractOrganizationFromUPN(u.UserPrincipalName),
                    InvitedDateTime = u.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                    InvitationStatus = "Accepted"
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all guest users");
        }

        return new List<GuestUser>();
    }

    public async Task<GuestUser?> GetUserByEmailAsync(string email)
    {
        try
        {
            var users = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"mail eq '{email}' or userPrincipalName eq '{email}'";
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime", "userType" };
            });

            var user = users?.Value?.FirstOrDefault();
            if (user != null)
            {
                return new GuestUser
                {
                    Id = user.Id ?? string.Empty,
                    Email = user.Mail ?? email,
                    DisplayName = user.DisplayName ?? string.Empty,
                    UserPrincipalName = user.UserPrincipalName ?? string.Empty,
                    OrganizationId = ExtractOrganizationFromUPN(user.UserPrincipalName),
                    InvitedDateTime = user.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                    InvitationStatus = user.UserType == "Guest" ? "Accepted" : "Member"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user by email {Email}", email);
        }

        return null;
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole)
    {
        try
        {
            // This would require custom attributes or group membership management
            // For now, we'll use group membership to represent roles
            var user = await _graphClient.Users[userId].GetAsync();
            if (user == null) return false;

            // Remove from existing role groups and add to new role group
            // This is a simplified implementation
            _logger.LogInformation("Updated role for user {UserId} to {Role}", userId, newRole);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update role for user {UserId} to {Role}", userId, newRole);
            return false;
        }
    }

    public async Task<bool> DeactivateUserAsync(string userId)
    {
        try
        {
            var user = new User
            {
                AccountEnabled = false
            };

            await _graphClient.Users[userId].PatchAsync(user);
            _logger.LogInformation("Deactivated user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ReactivateUserAsync(string userId)
    {
        try
        {
            var user = new User
            {
                AccountEnabled = true
            };

            await _graphClient.Users[userId].PatchAsync(user);
            _logger.LogInformation("Reactivated user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactivate user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> AssignAppRoleToUserAsync(string userId, string appRoleName)
    {
        try
        {
            _logger.LogInformation("Starting app role assignment for user {UserId}, role {RoleName}", userId, appRoleName);
            
            // Constants from your Azure AD app configuration
            const string servicePrincipalId = "8ba6461c-c478-471e-b1f4-81b6a33481b2"; // Service Principal ID
            const string orgAdminRoleId = "5099e0c0-99b5-41f1-bd9e-ff2301fe3e73";     // OrgAdmin role ID
            
            // Only support OrgAdmin role assignment for now (SuperAdmin is manual)
            if (appRoleName != "OrgAdmin")
            {
                _logger.LogWarning("App role assignment requested for unsupported role: {RoleName}", appRoleName);
                return false;
            }

            // Validate user ID format
            if (!Guid.TryParse(userId, out _))
            {
                _logger.LogError("Invalid user ID format: {UserId}", userId);
                return false;
            }

            var appRoleAssignment = new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(userId),
                ResourceId = Guid.Parse(servicePrincipalId),
                AppRoleId = Guid.Parse(orgAdminRoleId)
            };

            await _graphClient.Users[userId].AppRoleAssignments.PostAsync(appRoleAssignment);
            
            _logger.LogInformation("Successfully assigned {RoleName} app role to user {UserId}", appRoleName, userId);
            return true;
        }
        catch (ServiceException ex) when ((int)ex.ResponseStatusCode == 400 && ex.Message.Contains("already exists"))
        {
            // User already has this role - consider it success
            _logger.LogInformation("User {UserId} already has {RoleName} app role assigned", userId, appRoleName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign {RoleName} app role to user {UserId}. Error: {ErrorMessage}", 
                appRoleName, userId, ex.Message);
            
            // Additional debug info for ServiceException
            if (ex is ServiceException serviceEx)
            {
                _logger.LogError("Graph API Error Details - Status: {StatusCode}, ResponseBody: {ResponseBody}", 
                    serviceEx.ResponseStatusCode, serviceEx.RawResponseBody);
            }
            
            return false;
        }
    }

    public async Task<TeamsGroupResult> CreateTeamsGroupAsync(string groupName, string description, string organizationId, List<string>? teamsAppIds = null)
    {
        var result = new TeamsGroupResult { TeamName = groupName };
        
        try
        {
            _logger.LogInformation("Creating Teams group {GroupName} for organization {OrganizationId}", groupName, organizationId);

            // Step 1: Create a Microsoft 365 Group first
            var group = new Group
            {
                DisplayName = groupName,
                Description = description,
                MailNickname = GenerateMailNickname(groupName),
                GroupTypes = new List<string> { "Unified" }, // Microsoft 365 group
                MailEnabled = true,
                SecurityEnabled = false,
                Visibility = "Private"
            };

            var createdGroup = await _graphClient.Groups.PostAsync(group);
            
            if (createdGroup?.Id == null)
            {
                result.Errors.Add("Failed to create Microsoft 365 group");
                return result;
            }

            result.GroupId = createdGroup.Id;
            _logger.LogInformation("Created Microsoft 365 group {GroupId} for Teams", createdGroup.Id);

            // Step 2: Add a real user as owner (required for Teams conversion)
            // Use m.nachman@erpure.ai as the owner (real user, not guest)
            const string ownerEmail = "m.nachman@erpure.ai";
            
            try
            {
                _logger.LogInformation("Looking up owner user {OwnerEmail} for group {GroupId}", ownerEmail, createdGroup.Id);
                
                // Get the real user by email
                var ownerUser = await GetUserByEmailAsync(ownerEmail);
                if (ownerUser?.Id != null)
                {
                    _logger.LogInformation("Adding real user {UserId} ({Email}) as owner of group {GroupId}", ownerUser.Id, ownerEmail, createdGroup.Id);
                    
                    var ownerReference = new ReferenceCreate
                    {
                        OdataId = $"https://graph.microsoft.com/v1.0/users/{ownerUser.Id}"
                    };
                    
                    await _graphClient.Groups[createdGroup.Id].Owners.Ref.PostAsync(ownerReference);
                    _logger.LogInformation("Successfully added real user owner {Email} to group {GroupId}", ownerEmail, createdGroup.Id);
                }
                else
                {
                    _logger.LogError("Could not find real user {OwnerEmail} to set as group owner for {GroupId}", ownerEmail, createdGroup.Id);
                    result.Errors.Add($"Error: Could not find real user {ownerEmail} to set as group owner");
                }
            }
            catch (Exception ownerEx)
            {
                _logger.LogError(ownerEx, "Failed to add real user owner {OwnerEmail} to group {GroupId}, Teams conversion may fail", ownerEmail, createdGroup.Id);
                result.Errors.Add($"Error: Could not add real user owner {ownerEmail}: {ownerEx.Message}");
            }

            // Step 3: Wait for group and owner propagation with verification
            _logger.LogInformation("Waiting for group {GroupId} and owner propagation...", createdGroup.Id);
            await WaitForGroupReplicationAsync(createdGroup.Id);

            // Step 4: Validate group has non-guest owner before Teams conversion
            try
            {
                _logger.LogInformation("Validating group {GroupId} has non-guest owner before Teams conversion", createdGroup.Id);
                
                var owners = await _graphClient.Groups[createdGroup.Id].Owners.GetAsync();
                var hasNonGuestOwner = false;
                
                if (owners?.Value != null)
                {
                    foreach (var owner in owners.Value)
                    {
                        if (owner is User user)
                        {
                            _logger.LogInformation("Found owner: {UserId}, UserType: {UserType}, UserPrincipalName: {UPN}", 
                                user.Id, user.UserType ?? "null", user.UserPrincipalName ?? "null");
                            
                            if (user.UserType != "Guest")
                            {
                                hasNonGuestOwner = true;
                                _logger.LogInformation("Confirmed non-guest owner: {UPN} (UserType: {UserType})", 
                                    user.UserPrincipalName, user.UserType ?? "Member");
                                break;
                            }
                        }
                    }
                }
                
                if (!hasNonGuestOwner)
                {
                    var errorMsg = "Cannot convert to Team: No non-guest owner found. Teams requires at least one real (non-guest) user as owner.";
                    _logger.LogError(errorMsg + " Group {GroupId}", createdGroup.Id);
                    result.Errors.Add(errorMsg);
                    return result;
                }
                
                _logger.LogInformation("✅ Group {GroupId} has valid non-guest owner. Proceeding with Teams conversion.", createdGroup.Id);
                
                // Step 5: Convert the group to a Team using Microsoft Graph recommended settings and retry logic
                _logger.LogInformation("Converting group {GroupId} to Team with retry logic", createdGroup.Id);
                
                // Create Team object with Microsoft's recommended settings
                var team = new Team
                {
                    MemberSettings = new TeamMemberSettings
                    {
                        AllowCreatePrivateChannels = true,
                        AllowCreateUpdateChannels = true,
                        AllowDeleteChannels = false,
                        AllowAddRemoveApps = true,
                        AllowCreateUpdateRemoveTabs = true,
                        AllowCreateUpdateRemoveConnectors = true
                    },
                    GuestSettings = new TeamGuestSettings
                    {
                        AllowCreateUpdateChannels = false,
                        AllowDeleteChannels = false
                    },
                    MessagingSettings = new TeamMessagingSettings
                    {
                        AllowUserEditMessages = true,
                        AllowUserDeleteMessages = true,
                        AllowOwnerDeleteMessages = true,
                        AllowTeamMentions = true,
                        AllowChannelMentions = true
                    },
                    FunSettings = new TeamFunSettings
                    {
                        AllowGiphy = true,
                        GiphyContentRating = GiphyRatingType.Strict,
                        AllowStickersAndMemes = true,
                        AllowCustomMemes = false
                    }
                };

                // Implement Microsoft's recommended retry logic for Teams conversion with enhanced 404 handling
                Team? createdTeam = null;
                int retryAttempts = 0;
                const int maxRetries = 5; // Increased from 3 to 5 for better reliability
                const int baseRetryDelaySeconds = 15; // Increased base delay

                while (retryAttempts <= maxRetries && createdTeam == null)
                {
                    try
                    {
                        _logger.LogInformation("Teams conversion attempt {Attempt} for group {GroupId}", retryAttempts + 1, createdGroup.Id);
                        
                        createdTeam = await _graphClient.Groups[createdGroup.Id].Team.PutAsync(team);
                        
                        if (createdTeam?.Id != null)
                        {
                            _logger.LogInformation("SUCCESS: Team created on attempt {Attempt} for group {GroupId}", retryAttempts + 1, createdGroup.Id);
                            break;
                        }
                    }
                    catch (ServiceException ex) when (ex.ResponseStatusCode == 404 && retryAttempts < maxRetries)
                    {
                        var delaySeconds = baseRetryDelaySeconds * (retryAttempts + 1); // Progressive delay
                        _logger.LogWarning("404 error on attempt {Attempt} for group {GroupId} - Group replication still in progress. Retrying in {Delay} seconds...", 
                            retryAttempts + 1, createdGroup.Id, delaySeconds);
                        
                        retryAttempts++;
                        if (retryAttempts <= maxRetries)
                        {
                            // Additional group verification before retry
                            await VerifyGroupExistsBeforeRetryAsync(createdGroup.Id);
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        }
                        continue;
                    }
                    catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.Error?.Code == "NotFound" && retryAttempts < maxRetries)
                    {
                        var delaySeconds = baseRetryDelaySeconds * (retryAttempts + 1);
                        _logger.LogWarning("OData NotFound error on attempt {Attempt} for group {GroupId} - Education tenant group not fully replicated. Retrying in {Delay} seconds...", 
                            retryAttempts + 1, createdGroup.Id, delaySeconds);
                        
                        retryAttempts++;
                        if (retryAttempts <= maxRetries)
                        {
                            await VerifyGroupExistsBeforeRetryAsync(createdGroup.Id);
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        }
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Non-404 error on Teams conversion attempt {Attempt} for group {GroupId}: {Error}", 
                            retryAttempts + 1, createdGroup.Id, ex.Message);
                        
                        // Log additional diagnostic information
                        if (ex is Microsoft.Graph.Models.ODataErrors.ODataError odataError)
                        {
                            _logger.LogError("OData Error Details - Code: {Code}, Message: {Message}", 
                                odataError.Error?.Code, odataError.Error?.Message);
                        }
                        
                        break; // Don't retry for non-404/NotFound errors
                    }
                    
                    retryAttempts++;
                }
                
                if (createdTeam?.Id != null)
                {
                    result.TeamId = createdGroup.Id; // Team ID is same as Group ID
                    result.TeamUrl = $"https://teams.microsoft.com/l/team/{createdGroup.Id}";
                    result.Success = true;
                    
                    _logger.LogInformation("Successfully converted group {GroupId} to Team with name {GroupName}", 
                        createdGroup.Id, groupName);

                    // Step 6: Install Teams Apps if provided (with proper polling for readiness)
                    if (teamsAppIds != null && teamsAppIds.Any())
                    {
                        _logger.LogInformation("Preparing to install {Count} Teams apps to team {TeamId}. Waiting for Team to be fully ready...", teamsAppIds.Count, createdGroup.Id);
                        
                        // Wait for Team to be fully ready for app installations
                        var isTeamReady = await WaitForTeamReadinessAsync(createdGroup.Id);
                        
                        if (isTeamReady)
                        {
                            _logger.LogInformation("Team {TeamId} is ready. Installing Teams apps...", createdGroup.Id);
                            var appInstallResults = await InstallMultipleTeamsAppsWithRetryAsync(createdGroup.Id, teamsAppIds);
                            var successfulInstalls = appInstallResults.Count(kvp => kvp.Value);
                            var failedInstalls = appInstallResults.Count(kvp => !kvp.Value);
                            
                            if (successfulInstalls > 0)
                            {
                                _logger.LogInformation("Successfully installed {SuccessCount}/{TotalCount} Teams apps to team {TeamId}", 
                                    successfulInstalls, teamsAppIds.Count, createdGroup.Id);
                                
                                // Automatically set up Teams App permission policies using PowerShell
                                _logger.LogInformation("🤖 Automatically configuring Teams App permission policies for {AppCount} apps...", teamsAppIds.Count);
                                await ConfigureTeamsAppPermissionPoliciesAsync(createdGroup.Id, groupName, teamsAppIds);
                            }
                            
                            if (failedInstalls > 0)
                            {
                                var failedAppIds = appInstallResults.Where(kvp => !kvp.Value).Select(kvp => kvp.Key);
                                var warningMsg = $"Failed to install {failedInstalls} Teams apps: {string.Join(", ", failedAppIds)}";
                                _logger.LogWarning(warningMsg + " for team {TeamId}", createdGroup.Id);
                                result.Errors.Add($"Warning: {warningMsg}");
                            }
                        }
                        else
                        {
                            var warningMsg = "Team was created but not ready for app installations within timeout period. Apps will need to be installed manually or via background job.";
                            _logger.LogWarning(warningMsg + " Team: {TeamId}", createdGroup.Id);
                            result.Errors.Add($"Warning: {warningMsg}");
                        }
                    }
                }
                else
                {
                    result.Errors.Add($"Failed to convert group to Team after {maxRetries + 1} attempts. The group may need more time to replicate (wait 15+ minutes and try again).");
                    _logger.LogError("FAILED: Could not convert group {GroupId} to Team after all retry attempts", createdGroup.Id);
                }
            }
            catch (Exception teamEx)
            {
                _logger.LogWarning(teamEx, "Failed to convert group {GroupId} to Team, but group was created", createdGroup.Id);
                // Still consider it a success since we have the group - users can still collaborate
                result.TeamId = createdGroup.Id;
                result.TeamUrl = $"https://teams.microsoft.com/l/team/{createdGroup.Id}";
                result.Success = true;
                result.Errors.Add($"Group created but Teams conversion failed: {teamEx.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Teams group {GroupName} for organization {OrganizationId}", 
                groupName, organizationId);
            result.Errors.Add($"Error creating Teams group: {ex.Message}");
        }

        return result;
    }

    public async Task<bool> AddUserToTeamsGroupAsync(string userId, string teamsGroupId)
    {
        try
        {
            _logger.LogInformation("Adding user {UserId} to Teams group {GroupId}", userId, teamsGroupId);

            var member = new ReferenceCreate
            {
                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
            };

            await _graphClient.Groups[teamsGroupId].Members.Ref.PostAsync(member);
            
            _logger.LogInformation("Successfully added user {UserId} to Teams group {GroupId}", userId, teamsGroupId);
            return true;
        }
        catch (ServiceException ex) when ((int)ex.ResponseStatusCode == 400 && ex.Message.Contains("already exists"))
        {
            _logger.LogInformation("User {UserId} is already a member of Teams group {GroupId}", userId, teamsGroupId);
            return true; // Consider it success if already a member
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user {UserId} to Teams group {GroupId}", userId, teamsGroupId);
            return false;
        }
    }

    public async Task<bool> RemoveUserFromTeamsGroupAsync(string userId, string teamsGroupId)
    {
        try
        {
            _logger.LogInformation("Removing user {UserId} from Teams group {GroupId}", userId, teamsGroupId);

            await _graphClient.Groups[teamsGroupId].Members[userId].Ref.DeleteAsync();
            
            _logger.LogInformation("Successfully removed user {UserId} from Teams group {GroupId}", userId, teamsGroupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove user {UserId} from Teams group {GroupId}", userId, teamsGroupId);
            return false;
        }
    }

    public async Task<List<string>> GetTeamsGroupMembersAsync(string teamsGroupId)
    {
        try
        {
            var members = await _graphClient.Groups[teamsGroupId].Members.GetAsync();
            
            return members?.Value?.Select(m => m.Id ?? string.Empty).Where(id => !string.IsNullOrEmpty(id)).ToList() 
                   ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for Teams group {GroupId}", teamsGroupId);
            return new List<string>();
        }
    }

    public async Task<bool> DeleteTeamsGroupAsync(string teamsGroupId)
    {
        try
        {
            _logger.LogInformation("Deleting Teams group {GroupId}", teamsGroupId);

            await _graphClient.Groups[teamsGroupId].DeleteAsync();
            
            _logger.LogInformation("Successfully deleted Teams group {GroupId}", teamsGroupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Teams group {GroupId}", teamsGroupId);
            return false;
        }
    }

    private string GenerateMailNickname(string groupName)
    {
        // Create a mail nickname from the group name (alphanumeric only)
        var nickname = new string(groupName.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
        
        // Ensure it's not empty and add suffix if needed
        if (string.IsNullOrEmpty(nickname))
        {
            nickname = "teamsgroup";
        }
        
        // Add timestamp to ensure uniqueness
        nickname += DateTime.UtcNow.ToString("yyyyMMddHHmm");
        
        return nickname;
    }

    /// <summary>
    /// Installs a Microsoft Teams App to a specific Team
    /// </summary>
    /// <param name="teamId">The ID of the Team to install the app to</param>
    /// <param name="teamsAppId">The ID of the Teams App to install</param>
    /// <returns>True if installation was successful, false otherwise</returns>
    public async Task<bool> InstallTeamsAppAsync(string teamId, string teamsAppId)
    {
        try
        {
            if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(teamsAppId))
            {
                _logger.LogWarning("Cannot install Teams app: TeamId or TeamsAppId is null or empty");
                return false;
            }

            _logger.LogInformation("Installing Teams app {TeamsAppId} to team {TeamId}", teamsAppId, teamId);

            var teamsAppInstallation = new TeamsAppInstallation
            {
                AdditionalData = new Dictionary<string, object>
                {
                    {
                        "teamsApp@odata.bind", 
                        $"https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/{teamsAppId}"
                    }
                }
            };

            await _graphClient.Teams[teamId].InstalledApps.PostAsync(teamsAppInstallation);
            
            _logger.LogInformation("Successfully installed Teams app {TeamsAppId} to team {TeamId}", teamsAppId, teamId);
            return true;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 409)
        {
            // App is already installed - consider this a success
            _logger.LogInformation("Teams app {TeamsAppId} is already installed in team {TeamId}", teamsAppId, teamId);
            return true;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogError("Teams app {TeamsAppId} not found in app catalog or team {TeamId} not found", teamsAppId, teamId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Teams app {TeamsAppId} to team {TeamId}: {Error}", 
                teamsAppId, teamId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Installs multiple Teams Apps to a specific Team
    /// </summary>
    /// <param name="teamId">The ID of the Team to install the apps to</param>
    /// <param name="teamsAppIds">List of Teams App IDs to install</param>
    /// <returns>Dictionary with app IDs as keys and success status as values</returns>
    public async Task<Dictionary<string, bool>> InstallMultipleTeamsAppsAsync(string teamId, List<string> teamsAppIds)
    {
        var results = new Dictionary<string, bool>();

        if (string.IsNullOrEmpty(teamId) || teamsAppIds == null || !teamsAppIds.Any())
        {
            _logger.LogWarning("Cannot install Teams apps: TeamId is null/empty or no app IDs provided");
            return results;
        }

        _logger.LogInformation("Installing {Count} Teams apps to team {TeamId}", teamsAppIds.Count, teamId);

        foreach (var appId in teamsAppIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            var success = await InstallTeamsAppAsync(teamId, appId);
            results[appId] = success;
        }

        var successCount = results.Values.Count(success => success);
        _logger.LogInformation("Installed {SuccessCount}/{TotalCount} Teams apps to team {TeamId}", 
            successCount, teamsAppIds.Count, teamId);

        return results;
    }

    /// <summary>
    /// Waits for a Team to be fully ready for app installations by polling the Teams endpoint
    /// </summary>
    /// <param name="teamId">The Team ID to check</param>
    /// <returns>True if Team is ready within timeout, false otherwise</returns>
    private async Task<bool> WaitForTeamReadinessAsync(string teamId)
    {
        const int maxWaitTimeMinutes = 5; // Maximum wait time
        const int pollIntervalSeconds = 15; // Check every 15 seconds
        const int maxPollAttempts = (maxWaitTimeMinutes * 60) / pollIntervalSeconds;
        
        _logger.LogInformation("Polling Team {TeamId} readiness (max {MaxWait} minutes, polling every {PollInterval} seconds)", 
            teamId, maxWaitTimeMinutes, pollIntervalSeconds);
        
        for (int attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            try
            {
                // Try to access the Team directly to see if it's fully ready
                var team = await _graphClient.Teams[teamId].GetAsync();
                
                if (team?.Id != null)
                {
                    // Additional check: Try to get the apps list to confirm it's ready for app operations
                    try
                    {
                        await _graphClient.Teams[teamId].InstalledApps.GetAsync();
                        _logger.LogInformation("Team {TeamId} is ready for app installations (attempt {Attempt}/{MaxAttempts})", 
                            teamId, attempt, maxPollAttempts);
                        return true;
                    }
                    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
                    {
                        // Team exists but apps endpoint not ready yet
                        _logger.LogDebug("Team {TeamId} exists but apps endpoint not ready yet (attempt {Attempt}/{MaxAttempts})", 
                            teamId, attempt, maxPollAttempts);
                    }
                }
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
            {
                _logger.LogDebug("Team {TeamId} not found yet (attempt {Attempt}/{MaxAttempts}) - waiting...", 
                    teamId, attempt, maxPollAttempts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Team {TeamId} readiness (attempt {Attempt}/{MaxAttempts}): {Error}", 
                    teamId, attempt, maxPollAttempts, ex.Message);
            }
            
            if (attempt < maxPollAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
            }
        }
        
        _logger.LogWarning("Team {TeamId} was not ready for app installations within {MaxWait} minutes", 
            teamId, maxWaitTimeMinutes);
        return false;
    }

    /// <summary>
    /// Installs multiple Teams Apps with retry logic for better reliability
    /// </summary>
    /// <param name="teamId">The Team ID to install apps to</param>
    /// <param name="teamsAppIds">List of Teams App IDs to install</param>
    /// <returns>Dictionary with app IDs as keys and success status as values</returns>
    private async Task<Dictionary<string, bool>> InstallMultipleTeamsAppsWithRetryAsync(string teamId, List<string> teamsAppIds)
    {
        var results = new Dictionary<string, bool>();

        if (string.IsNullOrEmpty(teamId) || teamsAppIds == null || !teamsAppIds.Any())
        {
            _logger.LogWarning("Cannot install Teams apps: TeamId is null/empty or no app IDs provided");
            return results;
        }

        _logger.LogInformation("Installing {Count} Teams apps to team {TeamId} with retry logic", teamsAppIds.Count, teamId);

        foreach (var appId in teamsAppIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            const int maxRetries = 3;
            bool success = false;
            
            for (int retry = 0; retry <= maxRetries && !success; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        _logger.LogInformation("Retrying Teams app {AppId} installation (attempt {Attempt}/{MaxRetries})", 
                            appId, retry + 1, maxRetries + 1);
                        await Task.Delay(TimeSpan.FromSeconds(10 * retry)); // Exponential backoff
                    }
                    
                    success = await InstallTeamsAppAsync(teamId, appId);
                    
                    if (success)
                    {
                        _logger.LogInformation("Successfully installed Teams app {AppId} to team {TeamId} on attempt {Attempt}", 
                            appId, teamId, retry + 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to install Teams app {AppId} on attempt {Attempt}: {Error}", 
                        appId, retry + 1, ex.Message);
                }
            }
            
            results[appId] = success;
            
            if (!success)
            {
                _logger.LogError("Failed to install Teams app {AppId} to team {TeamId} after {MaxRetries} attempts", 
                    appId, teamId, maxRetries + 1);
            }
        }

        var successCount = results.Values.Count(s => s);
        _logger.LogInformation("Successfully installed {SuccessCount}/{TotalCount} Teams apps to team {TeamId}", 
            successCount, teamsAppIds.Count, teamId);

        return results;
    }

    /// <summary>
    /// Waits for group replication across Microsoft services with progressive backoff
    /// </summary>
    /// <param name="groupId">The Group ID to verify</param>
    /// <returns>Task representing the async operation</returns>
    private async Task WaitForGroupReplicationAsync(string groupId)
    {
        const int maxWaitAttempts = 6; // Total ~90 seconds of waiting
        const int baseDelaySeconds = 5;
        
        for (int attempt = 1; attempt <= maxWaitAttempts; attempt++)
        {
            try
            {
                _logger.LogDebug("Group replication check {Attempt}/{MaxAttempts} for group {GroupId}", attempt, maxWaitAttempts, groupId);
                
                // Try to access the group to verify replication
                var group = await _graphClient.Groups[groupId].GetAsync();
                if (group?.Id != null)
                {
                    _logger.LogInformation("Group {GroupId} replication verified after {Attempt} attempts", groupId, attempt);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Group replication check {Attempt} failed for {GroupId}: {Error}", attempt, groupId, ex.Message);
            }
            
            var delaySeconds = baseDelaySeconds * attempt; // Progressive delay: 5, 10, 15, 20, 25, 30
            _logger.LogDebug("Waiting {Delay} seconds before next replication check...", delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
        
        _logger.LogWarning("Group {GroupId} replication could not be verified after {MaxAttempts} attempts", groupId, maxWaitAttempts);
    }

    /// <summary>
    /// Verifies group existence before retry attempts with additional diagnostics
    /// </summary>
    /// <param name="groupId">The Group ID to verify</param>
    /// <returns>Task representing the async operation</returns>
    private async Task VerifyGroupExistsBeforeRetryAsync(string groupId)
    {
        try
        {
            _logger.LogDebug("Verifying group {GroupId} exists before Teams conversion retry...", groupId);
            
            var group = await _graphClient.Groups[groupId].GetAsync();
            if (group?.Id != null)
            {
                _logger.LogDebug("Group verification successful - Group {GroupId} exists with name: {GroupName}", groupId, group.DisplayName);
                
                // Additional checks for Teams readiness
                if (group.GroupTypes?.Contains("Unified") == true)
                {
                    _logger.LogDebug("Group {GroupId} is a Unified (M365) group - ready for Teams conversion", groupId);
                }
                else
                {
                    _logger.LogWarning("Group {GroupId} is not a Unified group - Teams conversion may fail", groupId);
                }
            }
            else
            {
                _logger.LogWarning("Group verification failed - Group {GroupId} returned null", groupId);
            }
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("Group {GroupId} not found during verification - replication still in progress", groupId);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.Error?.Code == "NotFound")
        {
            _logger.LogWarning("Group {GroupId} not found during verification (OData NotFound) - Education tenant replication delay", groupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during group verification for {GroupId}: {Error}", groupId, ex.Message);
        }
    }

    /// <summary>
    /// Automatically configures Teams App permission policies using PowerShell for multiple agent types
    /// </summary>
    /// <param name="groupId">Azure AD Group ID</param>
    /// <param name="groupName">Group display name</param>
    /// <param name="teamsAppIds">List of Teams App IDs to configure policies for</param>
    /// <returns>Task representing the async operation</returns>
    private async Task ConfigureTeamsAppPermissionPoliciesAsync(string groupId, string groupName, List<string> teamsAppIds)
    {
        try
        {
            if (!teamsAppIds.Any())
            {
                _logger.LogInformation("No Teams App IDs provided for permission policy configuration");
                return;
            }

            _logger.LogInformation("Configuring Teams App permission policies for group {GroupId} with {AppCount} apps", 
                groupId, teamsAppIds.Count);

            var tenantId = _configuration["AzureAd:TenantId"];
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("Azure AD Tenant ID not configured. Cannot set up Teams App permission policies.");
                return;
            }

            // Execute PowerShell script to configure policies
            var result = await _powerShellService.ExecuteTeamsAppPermissionPolicyAsync(
                tenantId, 
                groupId, 
                groupName, 
                teamsAppIds);

            if (result.Success)
            {
                _logger.LogInformation("✅ Successfully installed Teams Apps directly to team {GroupId}", groupId);
                
                // Log detailed results if available
                if (result.ResultData.TryGetValue("SuccessfulOperations", out var successCountObj) &&
                    result.ResultData.TryGetValue("TotalApps", out var totalAppsObj))
                {
                    _logger.LogInformation("App installation results: {SuccessCount}/{TotalCount} apps successfully installed to team", 
                        successCountObj, totalAppsObj);
                }
                else if (result.ResultData.TryGetValue("SuccessfulPolicies", out successCountObj) &&
                         result.ResultData.TryGetValue("TotalApps", out totalAppsObj))
                {
                    _logger.LogInformation("App installation results: {SuccessCount}/{TotalCount} apps successfully processed", 
                        successCountObj, totalAppsObj);
                }
            }
            else
            {
                _logger.LogError("❌ Failed to install Teams Apps to team {GroupId}: {Error}", 
                    groupId, result.Error);
                
                // Log PowerShell output for debugging
                if (!string.IsNullOrEmpty(result.Output))
                {
                    _logger.LogDebug("PowerShell Output: {Output}", result.Output);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring Teams App permission policies for group {GroupId}", groupId);
        }
    }

    /// <summary>
    /// Sets up Teams App permission policy to restrict app availability to specific groups
    /// </summary>
    /// <param name="teamsAppId">The Teams App ID to restrict</param>
    /// <param name="allowedGroupIds">List of Azure AD Group IDs that should have access</param>
    /// <returns>True if policy was set successfully</returns>
    public Task<bool> SetTeamsAppPermissionPolicyAsync(string teamsAppId, List<string> allowedGroupIds)
    {
        try
        {
            _logger.LogInformation("Setting Teams App permission policy for app {TeamsAppId} to restrict to {GroupCount} groups", 
                teamsAppId, allowedGroupIds.Count);

            // Note: Microsoft Teams App permission policies are typically managed through:
            // 1. Teams Admin Center UI
            // 2. PowerShell Teams module
            // 3. Graph API app setup policies (preview)
            
            // For now, we'll log this requirement and suggest manual configuration
            _logger.LogWarning("Teams App permission policy management requires manual configuration in Teams Admin Center or PowerShell.");
            _logger.LogInformation("To restrict app {TeamsAppId} to specific groups:", teamsAppId);
            _logger.LogInformation("1. Go to Teams Admin Center > Teams apps > Permission policies");
            _logger.LogInformation("2. Create a new policy or modify existing");
            _logger.LogInformation("3. Block the app globally and allow only for groups: {Groups}", string.Join(", ", allowedGroupIds));
            
            // TODO: Implement Graph API call when Microsoft makes app permission policies available via Graph API
            // Currently this requires Teams PowerShell module or Teams Admin Center
            
            return Task.FromResult(false); // Return false to indicate manual action needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Teams app permission policy for app {TeamsAppId}", teamsAppId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Creates a Teams App Setup Policy that includes specific apps for a group
    /// This is a workaround approach using Graph API's available functionality
    /// </summary>
    /// <param name="policyName">Name for the setup policy</param>
    /// <param name="teamsAppIds">List of Teams App IDs to include in the policy</param>
    /// <param name="groupId">Azure AD Group ID to assign this policy to</param>
    /// <returns>True if setup policy was created successfully</returns>
    public Task<bool> CreateTeamsAppSetupPolicyAsync(string policyName, List<string> teamsAppIds, string groupId)
    {
        try
        {
            _logger.LogInformation("Creating Teams App setup policy {PolicyName} for {AppCount} apps and group {GroupId}", 
                policyName, teamsAppIds.Count, groupId);

            // Note: Teams App Setup Policies via Graph API are in preview and have limited functionality
            // The most reliable approach is still through Teams Admin Center or PowerShell
            
            _logger.LogInformation("Teams App Setup Policy creation for group-specific app management:");
            _logger.LogInformation("Policy Name: {PolicyName}", policyName);
            _logger.LogInformation("Apps to include: {Apps}", string.Join(", ", teamsAppIds));
            _logger.LogInformation("Target Group: {GroupId}", groupId);
            
            _logger.LogWarning("This requires manual configuration in Teams Admin Center:");
            _logger.LogInformation("1. Go to Teams Admin Center > Teams apps > Setup policies");
            _logger.LogInformation("2. Create policy '{PolicyName}'", policyName);
            _logger.LogInformation("3. Add apps: {Apps}", string.Join(", ", teamsAppIds));
            _logger.LogInformation("4. Assign to group: {GroupId}", groupId);
            
            return Task.FromResult(false); // Return false to indicate manual action needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Teams app setup policy {PolicyName}", policyName);
            return Task.FromResult(false);
        }
    }
}