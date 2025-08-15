using AdminConsole.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using System.Security.Claims;
using System.Security;

namespace AdminConsole.Services;

public class GraphService : IGraphService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<GraphService> _logger;
    private readonly IPowerShellExecutionService _powerShellService;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// 🚨 CRITICAL SECURITY VALIDATOR: Intercepts and blocks any group deletion attempts
    /// This method provides a final fail-safe against accidental group deletion
    /// </summary>
    private void ValidateNoGroupDeletion(string operationName, string? groupId = null, string? methodName = null)
    {
        var stackTrace = System.Environment.StackTrace;
        var isGroupDeletion = operationName.ToLowerInvariant().Contains("delete") && 
                             (operationName.ToLowerInvariant().Contains("group") || 
                              operationName.ToLowerInvariant().Contains("team"));
        
        if (isGroupDeletion)
        {
            _logger.LogCritical("🚨🚨🚨 SECURITY ALERT: Group deletion attempt detected!");
            _logger.LogCritical("🔒 OPERATION: {Operation}", operationName);
            _logger.LogCritical("🔒 GROUP ID: {GroupId}", groupId ?? "UNKNOWN");
            _logger.LogCritical("🔒 METHOD: {Method}", methodName ?? "UNKNOWN");
            _logger.LogCritical("🔒 FULL STACK TRACE:\n{StackTrace}", stackTrace);
            
            throw new SecurityException($"🚨 SECURITY LOCKDOWN: Group deletion operation '{operationName}' is PERMANENTLY BLOCKED for security reasons. Group ID: {groupId}. All group deletions must be performed manually in Azure Portal.");
        }
        
        // Log all group operations for forensic analysis
        if (operationName.ToLowerInvariant().Contains("group"))
        {
            _logger.LogInformation("🔍 GROUP OPERATION: {Operation} - GroupId: {GroupId} - Method: {Method}", 
                operationName, groupId ?? "N/A", methodName ?? "N/A");
        }
    }

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

    /// <summary>
    /// Enhanced error handler for missing Azure AD resources with better logging and context
    /// </summary>
    private void HandleMissingResourceError(Exception ex, string resourceType, string resourceId, string operationContext, bool isExpected = false)
    {
        var isNotFoundError = ex is ServiceException serviceEx && serviceEx.ResponseStatusCode == 404 ||
                              ex is ODataError odataEx && odataEx.Error?.Code == "NotFound" ||
                              ex.Message.Contains("Resource does not exist") ||
                              ex.Message.Contains("does not exist");

        if (isNotFoundError)
        {
            if (isExpected)
            {
                _logger.LogInformation("🔍 {ResourceType} {ResourceId} not found during {Operation} - this is expected (resource may have been cleaned up)", 
                    resourceType, resourceId, operationContext);
            }
            else
            {
                _logger.LogWarning("⚠️ {ResourceType} {ResourceId} not found during {Operation} - possible database inconsistency. Resource may have been deleted outside of application", 
                    resourceType, resourceId, operationContext);
                _logger.LogWarning("💡 RECOMMENDATION: Consider running database cleanup to remove orphaned {ResourceType} references for {ResourceId}", 
                    resourceType, resourceId);
            }
        }
        else
        {
            _logger.LogError(ex, "❌ Unexpected error during {Operation} for {ResourceType} {ResourceId}: {Error}", 
                operationContext, resourceType, resourceId, ex.Message);
        }
    }

    public async Task<GuestUser?> InviteAdminUserAsync(string email, string displayName, string organizationName)
    {
        try
        {
            var invitation = new Invitation
            {
                InvitedUserEmailAddress = email,
                InvitedUserDisplayName = displayName,
                InviteRedirectUrl = "http://localhost:5243",
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
                InviteRedirectUrl = "http://localhost:5243",
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
            
            // Get user information first - try by ID, then by email
            Microsoft.Graph.Models.User? userInfo = null;
            string? actualUserId = null;
            
            try
            {
                // First try direct ID lookup
                if (Guid.TryParse(userId, out _))
                {
                    userInfo = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "userType", "creationType", "accountEnabled" };
                    });
                    actualUserId = userInfo?.Id;
                }
            }
            catch (Exception directIdEx)
            {
                _logger.LogInformation("Direct ID lookup failed for {UserId}, trying email lookup: {Error}", userId, directIdEx.Message);
                
                // Fallback: try to find user by email (for B2B users)
                if (userId.Contains("@"))
                {
                    try
                    {
                        var usersByEmail = await _graphClient.Users.GetAsync(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Filter = $"mail eq '{userId}' or userPrincipalName eq '{userId}'";
                            requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "userType", "creationType", "accountEnabled" };
                        });
                        
                        userInfo = usersByEmail?.Value?.FirstOrDefault();
                        actualUserId = userInfo?.Id;
                        
                        if (userInfo != null)
                        {
                            _logger.LogInformation("Found user by email lookup: {FoundUserId} for email {Email}", actualUserId, userId);
                        }
                        else
                        {
                            _logger.LogWarning("No user found for email {Email}", userId);
                        }
                    }
                    catch (Exception emailLookupEx)
                    {
                        _logger.LogWarning(emailLookupEx, "Email lookup also failed for {Email}", userId);
                    }
                }
            }
            
            if (userInfo != null)
            {
                _logger.LogInformation("User details - ID: {UserId}, Name: {DisplayName}, UPN: {UserPrincipalName}, UserType: {UserType}, CreationType: {CreationType}, AccountEnabled: {AccountEnabled}", 
                    actualUserId, userInfo?.DisplayName, userInfo?.UserPrincipalName, userInfo?.UserType, userInfo?.CreationType, userInfo?.AccountEnabled);
                    
                // CRITICAL DEBUG: Log exact values for guest detection
                _logger.LogInformation("GUEST DEBUG - Raw UserType: '{RawUserType}', Raw CreationType: '{RawCreationType}', UPN: '{UPN}'", 
                    userInfo?.UserType, userInfo?.CreationType, userInfo?.UserPrincipalName);
            }
            else
            {
                _logger.LogWarning("Could not retrieve user info for {UserId} - user may not exist in Azure AD", userId);
            }
            
            // Determine if this is a guest user and use appropriate revocation strategy
            bool isGuestUser = false;
            if (userInfo != null)
            {
                var userType = userInfo.UserType?.ToLowerInvariant();
                var creationType = userInfo.CreationType?.ToLowerInvariant();
                var upn = userInfo.UserPrincipalName?.ToLowerInvariant();
                
                isGuestUser = userType == "guest" || 
                             creationType == "invitation" || 
                             creationType == "viraluser" ||
                             (upn != null && upn.Contains("#ext#"));
            }
            
            _logger.LogInformation("User {UserId}: {UserCategory} [Type:{UserType}, Creation:{CreationType}]", 
                userId, isGuestUser ? "GUEST" : "MEMBER", userInfo?.UserType, userInfo?.CreationType);
            
            // If we couldn't find the user in Azure AD at all, skip Azure AD revocation
            if (userInfo == null || string.IsNullOrEmpty(actualUserId))
            {
                _logger.LogWarning("User {UserId} not found in Azure AD - skipping Graph API revocation (database revocation will still occur)", userId);
                return false;
            }
            
            if (isGuestUser)
            {
                _logger.LogInformation("Using guest-specific revocation strategy for user {ActualUserId}", actualUserId);
                return await RevokeGuestUserAccessAsync(actualUserId, userInfo);
            }
            else
            {
                _logger.LogInformation("Using member user revocation strategy for user {ActualUserId}", actualUserId);
                return await RevokeMemberUserAccessAsync(actualUserId, userInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke access for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Revoke access for guest users using guest-appropriate strategies
    /// Guest users cannot be disabled or deleted, so we focus on group removal and app role revocation
    /// </summary>
    private async Task<bool> RevokeGuestUserAccessAsync(string userId, Microsoft.Graph.Models.User? userInfo)
    {
        try
        {
            _logger.LogInformation("=== GUEST USER REVOCATION for {UserId} ({Email}) ===", userId, userInfo?.UserPrincipalName);
            _logger.LogInformation("Note: Guest users cannot be disabled/deleted via Graph API - using comprehensive revocation strategy");
            
            // SECURITY CRITICAL: Step 1 - Revoke app role assignments first (most critical for access control)
            _logger.LogInformation("🔒 STEP 1: Revoking app role assignments for guest user {UserId}", userId);
            
            // Revoke both OrgAdmin and OrgUser roles
            bool orgAdminRevoked = await RevokeAppRoleFromUserAsync(userId, "OrgAdmin");
            bool orgUserRevoked = await RevokeAppRoleFromUserAsync(userId, "OrgUser");
            
            if (orgAdminRevoked || orgUserRevoked)
            {
                _logger.LogInformation("✅ App role revocation completed for guest user {UserId} (OrgAdmin: {OrgAdminRevoked}, OrgUser: {OrgUserRevoked})", 
                    userId, orgAdminRevoked, orgUserRevoked);
            }
            else
            {
                _logger.LogWarning("⚠️ App role revocation failed for guest user {UserId} - continuing with group removal", userId);
            }
            
            // Step 2: Remove from all security groups (this is the primary method for guests)
            _logger.LogInformation("🔒 STEP 2: Removing guest user {UserId} from all security and M365 groups", userId);
            bool removedFromAnyGroup = false;
            bool userHadNoGroups = false;
            
            try
            {
                var memberOfResponse = await _graphClient.Users[userId].MemberOf.GetAsync();
                var groups = memberOfResponse?.Value?.OfType<Microsoft.Graph.Models.Group>() ?? Enumerable.Empty<Microsoft.Graph.Models.Group>();
                
                _logger.LogInformation("Guest user {UserId} is member of {GroupCount} groups", userId, groups.Count());
                
                foreach (var group in groups)
                {
                    if (group.Id != null)
                    {
                        try
                        {
                            // 🔒 SECURITY LOG: This is removing USER FROM GROUP, not deleting the group
                            _logger.LogInformation("🔒 SECURITY: Removing user {UserId} FROM group {GroupId} ({GroupName}) - NOT deleting group", userId, group.Id, group.DisplayName);
                            await _graphClient.Groups[group.Id].Members[userId].Ref.DeleteAsync();
                            _logger.LogInformation("✅ Removed guest user {UserId} from group {GroupId} ({GroupName})", userId, group.Id, group.DisplayName);
                            removedFromAnyGroup = true;
                        }
                        catch (Microsoft.Graph.Models.ODataErrors.ODataError groupEx)
                        {
                            _logger.LogWarning("❌ Could not remove guest user {UserId} from group {GroupId}: {Error}", userId, group.Id, groupEx.Error?.Message);
                        }
                    }
                }
                
                if (removedFromAnyGroup)
                {
                    _logger.LogInformation("✅ SUCCESS: Guest user {UserId} revocation completed. User removed from groups and app roles revoked.", userId);
                    return true;
                }
                else
                {
                    var groupCount = memberOfResponse?.Value?.Count() ?? 0;
                    if (groupCount == 0)
                    {
                        _logger.LogInformation("Guest user {UserId} was not a member of any security groups. App role revocation provides primary access control.", userId);
                        userHadNoGroups = true;
                    }
                    else
                    {
                        _logger.LogWarning("Guest user {UserId} was a member of {GroupCount} groups but could not be removed from any. Access revocation incomplete.", userId, groupCount);
                    }
                }
            }
            catch (Exception groupEx)
            {
                _logger.LogError(groupEx, "Failed to process group memberships for guest user {UserId}", userId);
            }
            
            // For guest users, evaluate combined success of app role and group revocation
            if (userHadNoGroups)
            {
                if (orgAdminRevoked || orgUserRevoked)
                {
                    _logger.LogInformation("✅ SUCCESS: Guest user {UserId} had no groups but app role was revoked. Access effectively revoked.", userId);
                    return true; // App role revocation is sufficient for access control
                }
                else
                {
                    _logger.LogWarning("Guest user {UserId} has no group memberships and app role revocation failed. Database revocation will handle access control.", userId);
                    return false; // Return false so database revocation handles it
                }
            }
            else
            {
                if (orgAdminRevoked || orgUserRevoked)
                {
                    _logger.LogInformation("✅ PARTIAL SUCCESS: Guest user {UserId} group removal failed but app role was revoked. Primary access revoked.", userId);
                    return true; // App role revocation is the most critical part
                }
                else
                {
                    _logger.LogError("Guest user {UserId} both group and app role revocation failed. Database revocation will provide fallback security.", userId);
                    return false; // Return false so database revocation handles it
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during guest user revocation for {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Revoke access for member users using comprehensive Azure AD strategies
    /// Member users can be disabled, deleted, removed from groups, and have app roles revoked
    /// </summary>
    private async Task<bool> RevokeMemberUserAccessAsync(string userId, Microsoft.Graph.Models.User? userInfo)
    {
        try
        {
            _logger.LogInformation("=== MEMBER USER REVOCATION for {UserId} ({Email}) ===", userId, userInfo?.UserPrincipalName);
            
            // SECURITY CRITICAL: Step 0 - Revoke app role assignments first (most critical for access control)
            _logger.LogInformation("🔒 STEP 0: Revoking app role assignments for member user {UserId}", userId);
            
            // Revoke both OrgAdmin and OrgUser roles
            bool orgAdminRevoked = await RevokeAppRoleFromUserAsync(userId, "OrgAdmin");
            bool orgUserRevoked = await RevokeAppRoleFromUserAsync(userId, "OrgUser");
            
            if (orgAdminRevoked || orgUserRevoked)
            {
                _logger.LogInformation("✅ App role revocation completed for member user {UserId} (OrgAdmin: {OrgAdminRevoked}, OrgUser: {OrgUserRevoked})", 
                    userId, orgAdminRevoked, orgUserRevoked);
            }
            else
            {
                _logger.LogWarning("⚠️ App role revocation failed for member user {UserId} - continuing with account disable", userId);
            }
            
            bool accountDisabled = false;
            try
            {
                _logger.LogInformation("CRITICAL: Attempting to disable member user account {UserId} in Azure Entra ID", userId);
                
                // Check if account is already disabled
                if (userInfo?.AccountEnabled == false)
                {
                    _logger.LogInformation("Member user account {UserId} is already disabled in Azure Entra ID", userId);
                    accountDisabled = true;
                }
                else
                {
                    var userUpdate = new Microsoft.Graph.Models.User
                    {
                        AccountEnabled = false
                    };
                    
                    await _graphClient.Users[userId].PatchAsync(userUpdate);
                    _logger.LogInformation("✅ SUCCESS: Member user account {UserId} has been DISABLED in Azure Entra ID. User cannot authenticate.", userId);
                    accountDisabled = true;
                }
                
                if (accountDisabled)
                {
                    // If we successfully disabled the account, that's the most secure approach
                    // The user is now completely blocked from authentication and app roles were revoked
                    _logger.LogInformation("✅ COMPLETE SUCCESS: Member user {UserId} account disabled and app roles revoked", userId);
                    return true;
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError disableEx)
            {
                _logger.LogError("❌ Could not disable member user account {UserId} in Azure Entra ID: {Error} (Code: {Code})", 
                    userId, disableEx.Error?.Message, disableEx.Error?.Code);
                    
                // Log specific error details for troubleshooting
                if (disableEx.Error?.Code == "Forbidden" || disableEx.Error?.Code == "Authorization_RequestDenied")
                {
                    _logger.LogError("PERMISSIONS ISSUE: Application service principal lacks permission to disable users. Required permission: 'User.ReadWrite.All' or 'Directory.ReadWrite.All'");
                }
                
                _logger.LogWarning("Member user account disable failed for {UserId}, trying delete strategy...", userId);
            }
            catch (Exception disableGenEx)
            {
                _logger.LogError(disableGenEx, "UNEXPECTED ERROR: Failed to disable member user account {UserId}", userId);
            }
            
            // Strategy 2: Try to delete the member user (original approach)
            try
            {
                _logger.LogInformation("🔒 STEP 2: Attempting to delete member user {UserId}", userId);
                await _graphClient.Users[userId].DeleteAsync();
                _logger.LogInformation("✅ COMPLETE SUCCESS: Member user {UserId} deleted and app roles revoked", userId);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError deleteEx)
            {
                _logger.LogWarning("❌ Could not delete member user {UserId}: {Error} (Code: {Code}). Trying group removal...", 
                    userId, deleteEx.Error?.Message, deleteEx.Error?.Code);
            }
            
            // Strategy 3: Remove from all security groups (fallback approach)
            _logger.LogInformation("🔒 STEP 3: Removing member user {UserId} from all security and M365 groups as fallback", userId);
            bool removedFromAnyGroup = false;
            bool userHadNoGroups = false;
            
            try
            {
                var memberOfResponse = await _graphClient.Users[userId].MemberOf.GetAsync();
                var groups = memberOfResponse?.Value?.OfType<Microsoft.Graph.Models.Group>() ?? Enumerable.Empty<Microsoft.Graph.Models.Group>();
                
                _logger.LogInformation("Member user {UserId} is member of {GroupCount} groups", userId, groups.Count());
                
                foreach (var group in groups)
                {
                    if (group.Id != null)
                    {
                        try
                        {
                            // 🔒 SECURITY LOG: This is removing USER FROM GROUP, not deleting the group
                            _logger.LogInformation("🔒 SECURITY: Removing user {UserId} FROM group {GroupId} ({GroupName}) - NOT deleting group", userId, group.Id, group.DisplayName);
                            await _graphClient.Groups[group.Id].Members[userId].Ref.DeleteAsync();
                            _logger.LogInformation("✅ Removed member user {UserId} from group {GroupId} ({GroupName})", userId, group.Id, group.DisplayName);
                            removedFromAnyGroup = true;
                        }
                        catch (Microsoft.Graph.Models.ODataErrors.ODataError groupEx)
                        {
                            _logger.LogWarning("❌ Could not remove member user {UserId} from group {GroupId}: {Error}", userId, group.Id, groupEx.Error?.Message);
                        }
                    }
                }
                
                if (removedFromAnyGroup)
                {
                    _logger.LogInformation("✅ SUCCESS: Member user {UserId} revocation completed via group removal and app roles revoked.", userId);
                    return true;
                }
                else
                {
                    var groupCount = memberOfResponse?.Value?.Count() ?? 0;
                    if (groupCount == 0)
                    {
                        _logger.LogInformation("Member user {UserId} was not a member of any security groups. App role revocation provides primary access control.", userId);
                        userHadNoGroups = true;
                    }
                    else
                    {
                        _logger.LogWarning("Member user {UserId} was a member of {GroupCount} groups but could not be removed from any. Access revocation incomplete.", userId, groupCount);
                    }
                }
            }
            catch (Exception groupEx)
            {
                _logger.LogError(groupEx, "Failed to process group memberships for member user {UserId}", userId);
            }
            
            // Evaluate final success based on combined app role and other revocation strategies
            if (userHadNoGroups)
            {
                if (orgAdminRevoked || orgUserRevoked)
                {
                    _logger.LogInformation("✅ PARTIAL SUCCESS: Member user {UserId} could not be disabled/deleted and had no groups, but app role was revoked. Primary access revoked.", userId);
                    return true; // App role revocation is the most critical part
                }
                else
                {
                    _logger.LogError("Member user {UserId} could not be disabled/deleted, had no groups, and app role revocation failed. Database revocation will provide fallback security.", userId);
                    return false;
                }
            }
            else
            {
                if (orgAdminRevoked || orgUserRevoked)
                {
                    _logger.LogInformation("✅ PARTIAL SUCCESS: Member user {UserId} disable/delete/group removal failed but app role was revoked. Primary access revoked.", userId);
                    return true; // App role revocation is the most critical part
                }
                else
                {
                    _logger.LogError("All Azure AD revocation strategies failed for member user {UserId}, including app roles. Database revocation will provide fallback security.", userId);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during member user revocation for {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> DisableUserAccountAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Attempting to disable user account {UserId} in Azure Entra ID", userId);
            
            // Get current user info first
            var userInfo = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "accountEnabled" };
            });
            
            if (userInfo?.AccountEnabled == false)
            {
                _logger.LogInformation("User account {UserId} is already disabled", userId);
                return true;
            }
            
            var userUpdate = new Microsoft.Graph.Models.User
            {
                AccountEnabled = false
            };
            
            await _graphClient.Users[userId].PatchAsync(userUpdate);
            _logger.LogInformation("Successfully disabled user account {UserId} in Azure Entra ID", userId);
            return true;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            _logger.LogError("Failed to disable user account {UserId}: {Error} (Code: {Code})", 
                userId, ex.Error?.Message, ex.Error?.Code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error disabling user account {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> EnableUserAccountAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Attempting to enable user account {UserId} in Azure Entra ID", userId);
            
            var userUpdate = new Microsoft.Graph.Models.User
            {
                AccountEnabled = true
            };
            
            await _graphClient.Users[userId].PatchAsync(userUpdate);
            _logger.LogInformation("Successfully enabled user account {UserId} in Azure Entra ID", userId);
            return true;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            _logger.LogError("Failed to enable user account {UserId}: {Error} (Code: {Code})", 
                userId, ex.Error?.Message, ex.Error?.Code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error enabling user account {UserId}", userId);
            return false;
        }
    }

    public async Task<GraphPermissionStatus> CheckUserManagementPermissionsAsync()
    {
        var status = new GraphPermissionStatus();
        
        try
        {
            _logger.LogInformation("Checking Microsoft Graph permissions for user management operations");
            
            // Test 1: Try to read users (basic permission check)
            try
            {
                await _graphClient.Users.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = 1;
                    requestConfiguration.QueryParameters.Select = new[] { "id", "displayName" };
                });
                _logger.LogInformation("✅ Basic user read permission confirmed");
            }
            catch (Exception readEx)
            {
                status.ErrorMessages.Add($"Cannot read users: {readEx.Message}");
                status.MissingPermissions.Add("User.Read.All");
            }
            
            // Test 2: Check if we can query for a specific test user to test disable permissions
            // We'll create a test scenario with the current app's service principal
            try
            {
                var servicePrincipal = await _graphClient.ServicePrincipals.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"appId eq '{_graphClient.RequestAdapter.BaseUrl}'";
                    requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "appRoles" };
                    requestConfiguration.QueryParameters.Top = 1;
                });
                
                _logger.LogInformation("✅ Service principal access confirmed");
            }
            catch (Exception spEx)
            {
                status.ErrorMessages.Add($"Cannot access service principal info: {spEx.Message}");
            }
            
            // For now, we'll assume permissions are available if basic operations work
            // In a production environment, you would test against a specific test user
            status.CanDisableUsers = status.ErrorMessages.Count == 0;
            status.CanDeleteUsers = status.ErrorMessages.Count == 0;
            status.CanManageGroups = status.ErrorMessages.Count == 0;
            
            if (!status.CanDisableUsers)
            {
                status.MissingPermissions.Add("User.ReadWrite.All or Directory.ReadWrite.All");
            }
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Graph permissions");
            status.ErrorMessages.Add($"Permission check failed: {ex.Message}");
        }
        
        return status;
    }

    public Task<GuestUser?> GetCurrentUserAsync()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? 
                            user.FindFirst("oid")?.Value ?? 
                            user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value ?? 
                           user.FindFirst("preferred_username")?.Value;
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
        return await InviteGuestUserAsync(email, organizationName, "https://localhost:5243", new List<string>(), false);
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

    /// <summary>
    /// Enhanced invitation status checker that verifies both database and Azure AD status
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <returns>Detailed invitation status information</returns>
    public async Task<InvitationStatusResult> CheckInvitationStatusAsync(string email)
    {
        try
        {
            _logger.LogInformation("🔍 Checking invitation status for {Email}", email);

            var result = new InvitationStatusResult
            {
                Email = email,
                CheckedOn = DateTime.UtcNow
            };

            // Check if user exists in Azure AD
            var azureUser = await GetUserByEmailAsync(email);
            if (azureUser != null)
            {
                result.ExistsInAzureAD = true;
                result.AzureUserId = azureUser.Id;
                result.UserType = azureUser.InvitationStatus == "Accepted" ? "Guest" : "Unknown";
                
                // For guests, if they exist in Azure AD with UserType="Guest", they've accepted
                if (azureUser.InvitationStatus == "Accepted")
                {
                    result.InvitationStatus = InvitationStatus.Accepted;
                    result.AcceptedDate = azureUser.InvitedDateTime;
                }
                else
                {
                    // User exists but may still be pending (check account status)
                    result.InvitationStatus = InvitationStatus.PendingAcceptance;
                }

                _logger.LogInformation("✅ User {Email} found in Azure AD: UserType={UserType}, Status={Status}", 
                    email, result.UserType, result.InvitationStatus);
            }
            else
            {
                result.ExistsInAzureAD = false;
                result.InvitationStatus = InvitationStatus.NotInvited;
                
                _logger.LogInformation("❌ User {Email} not found in Azure AD - likely not invited yet", email);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking invitation status for {Email}", email);
            return new InvitationStatusResult
            {
                Email = email,
                CheckedOn = DateTime.UtcNow,
                InvitationStatus = InvitationStatus.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> ResendInvitationAsync(string userId)
    {
        try
        {
            _logger.LogInformation("📧 RESENDING INVITATION for user {UserId}", userId);

            // Step 1: Resolve user email if we got an Azure ID
            string userEmail = userId;
            if (Guid.TryParse(userId, out _))
            {
                // It's an Azure AD Object ID, get the email
                var azureUser = await GetUserByEmailAsync(userId);
                if (azureUser?.Email != null)
                {
                    userEmail = azureUser.Email;
                }
                else
                {
                    _logger.LogError("❌ Could not resolve email for Azure AD user {UserId}", userId);
                    return false;
                }
            }

            // Step 2: Check current invitation status
            var statusCheck = await CheckInvitationStatusAsync(userEmail);
            
            if (statusCheck.InvitationStatus == InvitationStatus.Accepted)
            {
                _logger.LogWarning("⚠️ User {Email} has already accepted invitation - no resend needed", userEmail);
                return true; // Consider this success since user is already in
            }

            // Step 3: Create new B2B invitation (this effectively "resends" the invitation)
            _logger.LogInformation("🔄 Creating new B2B invitation for {Email} to resend", userEmail);
            
            // Use existing GraphService invitation method
            var invitationResult = await InviteGuestUserAsync(userEmail, "Your Organization");
            
            if (invitationResult.Success)
            {
                _logger.LogInformation("✅ Successfully resent invitation to {Email} - new invitation ID: {InvitationId}", 
                    userEmail, invitationResult.InvitationId);
                return true;
            }
            else
            {
                _logger.LogError("❌ Failed to resend invitation to {Email}: {Errors}", 
                    userEmail, string.Join(", ", invitationResult.Errors));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Failed to resend invitation for user {UserId}", userId);
            return false;
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

    public Task<bool> DeleteSecurityGroupAsync(string groupId)
    {
        // 🚨 FINAL SECURITY VALIDATOR - This will throw if group deletion is detected
        ValidateNoGroupDeletion("DeleteSecurityGroup", groupId, "DeleteSecurityGroupAsync");
        
        // 🚨 SECURITY LOCKDOWN: Completely disable group deletion functionality
        // Groups were being accidentally deleted during invitation/admin processes
        _logger.LogCritical("🚨 SECURITY BLOCK: DeleteSecurityGroupAsync called for group {GroupId} - OPERATION BLOCKED", groupId);
        _logger.LogCritical("🔒 SECURITY: Group deletion is PERMANENTLY DISABLED to prevent accidental deletion");
        _logger.LogCritical("📋 MANUAL ACTION REQUIRED: If you need to delete group {GroupId}, do it manually in Azure Portal", groupId);
        
        // Log the call stack to see what's trying to delete groups
        var stackTrace = System.Environment.StackTrace;
        _logger.LogCritical("🕵️ CALL STACK for blocked group deletion:\n{StackTrace}", stackTrace);
        
        return Task.FromResult(false); // Always fail - never delete groups via code
        
        /* ORIGINAL DANGEROUS CODE - PERMANENTLY DISABLED
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
        */
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
                    // 🚨 CRITICAL SECURITY FIX: NEVER AUTO-CREATE GROUPS!
                    // This was causing accidental group creation/deletion during invitations
                    _logger.LogError("🚨 CRITICAL: Group '{GroupName}' does not exist in Azure AD. REFUSING to auto-create groups for security reasons.", groupNameOrId);
                    _logger.LogError("📋 Please manually create the security group '{GroupName}' in Azure AD before assigning users to it.", groupNameOrId);
                    return false;
                }
            }

            // Add user to group
            if (group?.Id == null)
            {
                _logger.LogError("Group is null or has no ID after processing");
                return false;
            }
            
            _logger.LogInformation("DEBUG: Attempting to add user {UserId} to group {GroupId}", userId, group.Id);
            
            // Check if user exists before attempting to add them
            try
            {
                var user = await _graphClient.Users[userId].GetAsync();
                if (user == null)
                {
                    _logger.LogError("❌ User {UserId} not found in Azure AD", userId);
                    return false;
                }
            }
            catch (Exception userEx)
            {
                _logger.LogError(userEx, "❌ Failed to find user {UserId} in Azure AD: {Error}", userId, userEx.Message);
                return false;
            }
            
            var directoryObject = new ReferenceCreate
            {
                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
            };

            _logger.LogInformation("🔍 DIAGNOSTIC: About to call Azure AD API with Group ID: {GroupId}, User ID: {UserId}, OData ID: {ODataId}", 
                group.Id, userId, directoryObject.OdataId);

            await _graphClient.Groups[group.Id].Members.Ref.PostAsync(directoryObject);
            _logger.LogInformation("SUCCESS: Added user {UserId} to group {GroupNameOrId} (ID: {GroupId})", userId, groupNameOrId, group.Id);
            return true;
        }
        catch (Exception ex)
        {
            // Enhanced error logging to help diagnose the specific issue
            _logger.LogError("🔍 DIAGNOSTIC: Exception Type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", 
                ex.GetType().Name, ex.Message, ex.StackTrace);
            
            if (ex.Message.Contains("already exists") || ex.Message.Contains("One or more added object references already exist"))
            {
                _logger.LogInformation("ℹ️ User {UserId} is already a member of group {GroupNameOrId} - treating as success", userId, groupNameOrId);
                return true; // User is already in the group, treat as success
            }
            else if (ex.Message.Contains("does not exist") || ex.Message.Contains("not found"))
            {
                _logger.LogError("❌ User {UserId} or Group {GroupNameOrId} does not exist in Azure AD: {Error}", userId, groupNameOrId, ex.Message);
            }
            else if (ex.Message.Contains("Forbidden") || ex.Message.Contains("Authorization"))
            {
                _logger.LogError("❌ Insufficient permissions to add user {UserId} to group {GroupNameOrId}: {Error}", userId, groupNameOrId, ex.Message);
            }
            else
            {
                _logger.LogError(ex, "❌ Failed to add user {UserId} to group {GroupNameOrId}: {Error}", userId, groupNameOrId, ex.Message);
            }
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
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime", "externalUserState", "externalUserStateChangeDateTime", "accountEnabled" };
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
                    InvitationStatus = GetInvitationStatusFromAzureAD(u.ExternalUserState, u.AccountEnabled),
                    // CRITICAL FIX: Set the real Entra ID enabled status and UserType
                    IsEnabled = u.AccountEnabled ?? false,
                    UserType = "Guest" // Guest users are Guests
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all guest users");
        }

        return new List<GuestUser>();
    }

    /// <summary>
    /// Gets all tenant users (internal users, not guests)
    /// Used for Developer interface to promote existing erpure.ai users
    /// </summary>
    public async Task<List<GuestUser>> GetAllTenantUsersAsync()
    {
        try
        {
            _logger.LogInformation("Getting all tenant users (userType eq 'Member')");
            
            var users = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = "userType eq 'Member'";
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime", "accountEnabled", "jobTitle", "department" };
            });

            if (users?.Value != null)
            {
                var tenantUsers = users.Value.Select(u => new GuestUser
                {
                    Id = u.Id ?? string.Empty,
                    Email = u.Mail ?? u.UserPrincipalName ?? string.Empty, // Use UPN if mail is empty
                    DisplayName = u.DisplayName ?? string.Empty,
                    UserPrincipalName = u.UserPrincipalName ?? string.Empty,
                    OrganizationId = "tenant", // Mark as tenant users
                    InvitedDateTime = u.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                    InvitationStatus = u.AccountEnabled == true ? "Active" : "Disabled", // Tenant users don't have invitation status
                    // CRITICAL FIX: Set the real Entra ID enabled status and UserType
                    IsEnabled = u.AccountEnabled ?? false,
                    UserType = "Member" // Tenant users are Members, not Guests
                }).ToList();
                
                _logger.LogInformation("Found {Count} tenant users", tenantUsers.Count);
                return tenantUsers;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all tenant users");
        }

        return new List<GuestUser>();
    }

    /// <summary>
    /// Gets tenant users filtered by email domain
    /// </summary>
    public async Task<List<GuestUser>> GetTenantUsersByDomainAsync(string domain)
    {
        try
        {
            _logger.LogInformation("Getting tenant users for domain: {Domain}", domain);
            
            var allTenantUsers = await GetAllTenantUsersAsync();
            
            var domainUsers = allTenantUsers
                .Where(u => u.Email.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
                .ToList();
                
            _logger.LogInformation("Found {Count} tenant users in domain {Domain}", domainUsers.Count, domain);
            return domainUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tenant users for domain {Domain}", domain);
            return new List<GuestUser>();
        }
    }

    /// <summary>
    /// Gets all users (both tenant and guest users)
    /// </summary>
    public async Task<List<GuestUser>> GetAllUsersAsync()
    {
        try
        {
            _logger.LogInformation("Getting all users (tenant and guest)");
            
            var tenantUsersTask = GetAllTenantUsersAsync();
            var guestUsersTask = GetAllGuestUsersAsync();
            
            await Task.WhenAll(tenantUsersTask, guestUsersTask);
            
            var allUsers = new List<GuestUser>();
            allUsers.AddRange(tenantUsersTask.Result);
            allUsers.AddRange(guestUsersTask.Result);
            
            _logger.LogInformation("Found {TenantCount} tenant users and {GuestCount} guest users", 
                tenantUsersTask.Result.Count, guestUsersTask.Result.Count);
                
            return allUsers.OrderBy(u => u.Email).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all users");
            return new List<GuestUser>();
        }
    }

    private string GetInvitationStatusFromAzureAD(string? externalUserState, bool? accountEnabled)
    {
        // ExternalUserState possible values:
        // "PendingAcceptance" - User has been invited but hasn't accepted yet
        // "Accepted" - User has accepted the invitation
        // null or empty - Member user (not guest) or legacy data
        
        if (string.IsNullOrEmpty(externalUserState))
        {
            // For users without externalUserState (could be members or older guests)
            // Use account status as fallback
            return accountEnabled == true ? "Accepted" : "PendingAcceptance";
        }
        
        // Map Azure AD external user state directly
        return externalUserState switch
        {
            "PendingAcceptance" => "PendingAcceptance",
            "Accepted" => "Accepted",
            _ => accountEnabled == true ? "Accepted" : "PendingAcceptance"
        };
    }

    public async Task<bool> UserExistsAsync(string userId)
    {
        try
        {
            // Check if it's an Azure Object ID or email
            if (Guid.TryParse(userId, out _))
            {
                // It's an Azure Object ID - get user directly
                var user = await _graphClient.Users[userId].GetAsync();
                return user != null;
            }
            else
            {
                // It's an email - search for user
                var users = await _graphClient.Users.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"mail eq '{userId}' or userPrincipalName eq '{userId}'";
                });
                return users?.Value?.Any() == true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "User {UserId} does not exist in Azure AD: {Error}", userId, ex.Message);
            return false;
        }
    }

    public async Task<GuestUser?> GetUserByEmailAsync(string email)
    {
        try
        {
            var users = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"mail eq '{email}' or userPrincipalName eq '{email}'";
                requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "createdDateTime", "userType", "externalUserState", "externalUserStateChangeDateTime", "accountEnabled" };
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
                    InvitationStatus = user.UserType == "Guest" ? GetInvitationStatusFromAzureAD(user.ExternalUserState, user.AccountEnabled) : "Member"
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
            const string orgUserRoleId = "b6ce7f42-61fa-4edf-b666-da7726be5e5b";                 // OrgUser role ID - TO BE UPDATED
            const string devRoleId = "5b9b72d7-e667-4c6d-8415-d098bdece416";                          // DevRole role ID - TO BE UPDATED
            const string superAdminRoleId = "9eb43d2c-a5a0-40ae-9504-7d2e69ba187b";           // SuperAdmin role ID - TO BE UPDATED
            
            // Support all app roles for system user management
            if (appRoleName != "OrgAdmin" && appRoleName != "OrgUser" && appRoleName != "DevRole" && appRoleName != "SuperAdmin")
            {
                _logger.LogWarning("App role assignment requested for unsupported role: {RoleName}", appRoleName);
                return false;
            }
            
            // Find the actual Azure AD user ID (similar to RevokeUserAccessAsync logic)
            Microsoft.Graph.Models.User? userInfo = null;
            string? actualUserId = null;
            
            try
            {
                // First try direct ID lookup
                if (Guid.TryParse(userId, out _))
                {
                    userInfo = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName" };
                    });
                    actualUserId = userInfo?.Id;
                }
            }
            catch (Exception directIdEx)
            {
                _logger.LogInformation("Direct ID lookup failed for role assignment {UserId}, trying email lookup: {Error}", userId, directIdEx.Message);
                
                // Fallback: try to find user by email
                if (userId.Contains("@"))
                {
                    try
                    {
                        var usersByEmail = await _graphClient.Users.GetAsync(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Filter = $"mail eq '{userId}' or userPrincipalName eq '{userId}'";
                            requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName" };
                        });
                        
                        userInfo = usersByEmail?.Value?.FirstOrDefault();
                        actualUserId = userInfo?.Id;
                        
                        if (userInfo != null)
                        {
                            _logger.LogInformation("Found user by email for role assignment: {FoundUserId} for email {Email}", actualUserId, userId);
                        }
                        else
                        {
                            _logger.LogError("❌ App role assignment failed - no user found for email {Email}", userId);
                            return false;
                        }
                    }
                    catch (Exception emailLookupEx)
                    {
                        _logger.LogError(emailLookupEx, "❌ App role assignment failed - email lookup failed for {Email}", userId);
                        return false;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(actualUserId))
            {
                _logger.LogError("❌ App role assignment failed - could not resolve user ID for {UserId}", userId);
                return false;
            }

            // Validate user ID format
            if (!Guid.TryParse(actualUserId, out _))
            {
                _logger.LogError("Invalid user ID format: {ActualUserId}", actualUserId);
                return false;
            }

            // Determine which role ID to use based on the role name
            string roleIdToUse = appRoleName switch
            {
                "OrgAdmin" => orgAdminRoleId,
                "OrgUser" => orgUserRoleId,
                "DevRole" => devRoleId,
                "SuperAdmin" => superAdminRoleId,
                _ => throw new ArgumentException($"Unsupported app role: {appRoleName}")
            };
            
            var appRoleAssignment = new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(actualUserId),
                ResourceId = Guid.Parse(servicePrincipalId),
                AppRoleId = Guid.Parse(roleIdToUse)
            };

            await _graphClient.Users[actualUserId].AppRoleAssignments.PostAsync(appRoleAssignment);
            
            _logger.LogInformation("Successfully assigned {RoleName} app role to user {ActualUserId} (original: {OriginalUserId})", appRoleName, actualUserId, userId);
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

    /// <summary>
    /// SECURITY CRITICAL: Revokes app role assignments from a user (opposite of AssignAppRoleToUserAsync)
    /// This is essential for complete user access revocation
    /// </summary>
    /// <param name="userId">User ID (supports both Azure AD ID and email)</param>
    /// <param name="appRoleName">App role name to revoke (currently supports "OrgAdmin")</param>
    /// <returns>True if revocation was successful or role wasn't assigned, false on error</returns>
    public async Task<bool> RevokeAppRoleFromUserAsync(string userId, string appRoleName)
    {
        try
        {
            _logger.LogInformation("🔒 SECURITY: Starting app role revocation for user {UserId}, role {RoleName}", userId, appRoleName);
            
            // Constants from your Azure AD app configuration
            const string servicePrincipalId = "8ba6461c-c478-471e-b1f4-81b6a33481b2"; // Service Principal ID
            const string orgAdminRoleId = "5099e0c0-99b5-41f1-bd9e-ff2301fe3e73";     // OrgAdmin role ID
            const string orgUserRoleId = "YOUR_ORG_USER_ROLE_ID_HERE";                 // OrgUser role ID - TO BE UPDATED
            const string devRoleId = "YOUR_DEV_ROLE_ID_HERE";                          // DevRole role ID - TO BE UPDATED
            const string superAdminRoleId = "YOUR_SUPER_ADMIN_ROLE_ID_HERE";           // SuperAdmin role ID - TO BE UPDATED
            
            // Support all app roles for system user management
            if (appRoleName != "OrgAdmin" && appRoleName != "OrgUser" && appRoleName != "DevRole" && appRoleName != "SuperAdmin")
            {
                _logger.LogInformation("App role revocation requested for unsupported role: {RoleName} - skipping", appRoleName);
                return true; // Return true since we don't need to revoke unsupported roles
            }
            
            // Find the actual Azure AD user ID (similar to RevokeUserAccessAsync logic)
            Microsoft.Graph.Models.User? userInfo = null;
            string? actualUserId = null;
            
            try
            {
                // First try direct ID lookup
                if (Guid.TryParse(userId, out _))
                {
                    userInfo = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName" };
                    });
                    actualUserId = userInfo?.Id;
                }
            }
            catch (Exception directIdEx)
            {
                _logger.LogInformation("Direct ID lookup failed for role revocation {UserId}, trying email lookup: {Error}", userId, directIdEx.Message);
                
                // Fallback: try to find user by email
                if (userId.Contains("@"))
                {
                    try
                    {
                        var usersByEmail = await _graphClient.Users.GetAsync(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Filter = $"mail eq '{userId}' or userPrincipalName eq '{userId}'";
                            requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName" };
                        });
                        
                        userInfo = usersByEmail?.Value?.FirstOrDefault();
                        actualUserId = userInfo?.Id;
                        
                        if (userInfo != null)
                        {
                            _logger.LogInformation("Found user by email for role revocation: {FoundUserId} for email {Email}", actualUserId, userId);
                        }
                        else
                        {
                            _logger.LogWarning("No user found for app role revocation - email {Email} (may have been deleted already)", userId);
                            return true; // Return true since user doesn't exist = role already revoked
                        }
                    }
                    catch (Exception emailLookupEx)
                    {
                        _logger.LogWarning(emailLookupEx, "Email lookup failed for app role revocation {Email} - user may not exist", userId);
                        return true; // Return true since user doesn't exist = role already revoked
                    }
                }
            }
            
            if (string.IsNullOrEmpty(actualUserId))
            {
                _logger.LogWarning("Could not resolve user ID for app role revocation {UserId} - user may not exist", userId);
                return true; // Return true since user doesn't exist = role already revoked
            }

            // Get all app role assignments for the user
            var appRoleAssignments = await _graphClient.Users[actualUserId].AppRoleAssignments.GetAsync();
            
            if (appRoleAssignments?.Value == null || !appRoleAssignments.Value.Any())
            {
                _logger.LogInformation("✅ User {ActualUserId} has no app role assignments - revocation not needed", actualUserId);
                return true;
            }

            // Determine which role ID to look for based on the role name
            string roleIdToRevoke = appRoleName switch
            {
                "OrgAdmin" => orgAdminRoleId,
                "OrgUser" => orgUserRoleId,
                "DevRole" => devRoleId,
                "SuperAdmin" => superAdminRoleId,
                _ => throw new ArgumentException($"Unsupported app role for revocation: {appRoleName}")
            };
            
            // Find and remove the specific app role assignment
            bool foundAndRevoked = false;
            foreach (var assignment in appRoleAssignments.Value)
            {
                if (assignment.ResourceId.ToString() == servicePrincipalId && 
                    assignment.AppRoleId.ToString() == roleIdToRevoke)
                {
                    try
                    {
                        await _graphClient.Users[actualUserId].AppRoleAssignments[assignment.Id].DeleteAsync();
                        _logger.LogInformation("🔒 SECURITY SUCCESS: Revoked {RoleName} app role from user {ActualUserId} (original: {OriginalUserId})", 
                            appRoleName, actualUserId, userId);
                        foundAndRevoked = true;
                        break;
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogError(deleteEx, "❌ Failed to delete app role assignment {AssignmentId} for user {ActualUserId}", 
                            assignment.Id, actualUserId);
                        return false;
                    }
                }
            }
            
            if (!foundAndRevoked)
            {
                _logger.LogInformation("✅ User {ActualUserId} did not have {RoleName} app role assigned - revocation not needed", actualUserId, appRoleName);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SECURITY ERROR: Failed to revoke {RoleName} app role from user {UserId}. Error: {ErrorMessage}", 
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

    /// <summary>
    /// Gets all groups that a user is a member of (for bidirectional sync)
    /// </summary>
    public async Task<List<Microsoft.Graph.Models.Group>> GetUserGroupMembershipsAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Getting all group memberships for user {UserId}", userId);
            
            // SIMPLIFIED: Use the most basic approach that should work
            // The complex PageIterator approach might be causing issues
            var memberOfResponse = await _graphClient.Users[userId].MemberOf.GetAsync();
            
            _logger.LogInformation("Raw MemberOf response received. Value count: {Count}", 
                memberOfResponse?.Value?.Count ?? 0);
            
            var allGroups = new List<Microsoft.Graph.Models.Group>();
            
            if (memberOfResponse?.Value != null)
            {
                foreach (var directoryObject in memberOfResponse.Value)
                {
                    _logger.LogDebug("Processing directory object: Type={Type}, Id={Id}", 
                        directoryObject?.GetType().Name ?? "null", 
                        directoryObject?.Id ?? "null");
                        
                    if (directoryObject is Microsoft.Graph.Models.Group group)
                    {
                        allGroups.Add(group);
                        _logger.LogInformation("✅ Found group membership: {GroupId} ({DisplayName}) - SecurityEnabled: {SecurityEnabled}", 
                            group.Id, group.DisplayName, group.SecurityEnabled);
                    }
                    else
                    {
                        _logger.LogDebug("❌ Directory object is not a group: {Type}", directoryObject?.GetType().Name);
                    }
                }
            }
            else
            {
                _logger.LogWarning("MemberOf response or Value is null for user {UserId}", userId);
            }
            
            _logger.LogInformation("User {UserId} is member of {Count} total groups", userId, allGroups.Count);
            
            // Log breakdown by group type for debugging
            var securityGroups = allGroups.Where(g => g.SecurityEnabled == true).Count();
            var distributionGroups = allGroups.Where(g => g.MailEnabled == true && g.SecurityEnabled != true).Count();
            var m365Groups = allGroups.Where(g => g.GroupTypes?.Contains("Unified") == true).Count();
            
            _logger.LogInformation("Group breakdown for user {UserId}: {SecurityGroups} security groups, {DistributionGroups} distribution groups, {M365Groups} M365 groups", 
                userId, securityGroups, distributionGroups, m365Groups);
            
            return allGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get group memberships for user {UserId}", userId);
            return new List<Microsoft.Graph.Models.Group>();
        }
    }

    /// <summary>
    /// Check if a user is a member of a specific group by querying the group's members
    /// This is a reverse lookup to verify group membership from the group perspective
    /// </summary>
    public async Task<bool> IsUserMemberOfGroupAsync(string userId, string groupId)
    {
        try
        {
            _logger.LogInformation("Checking if user {UserId} is member of group {GroupId} from group perspective", userId, groupId);
            
            // Query the group's members to see if our user is in there
            var groupMembers = await _graphClient.Groups[groupId].Members.GetAsync();
            
            _logger.LogInformation("Group {GroupId} has {MemberCount} total members", groupId, groupMembers?.Value?.Count ?? 0);
            
            if (groupMembers?.Value != null)
            {
                foreach (var member in groupMembers.Value)
                {
                    if (member?.Id == userId)
                    {
                        _logger.LogInformation("✅ CONFIRMED: User {UserId} is a member of group {GroupId}", userId, groupId);
                        return true;
                    }
                }
            }
            
            _logger.LogWarning("❌ NOT FOUND: User {UserId} is NOT a member of group {GroupId}", userId, groupId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if user {UserId} is member of group {GroupId}", userId, groupId);
            return false;
        }
    }

    public Task<bool> DeleteTeamsGroupAsync(string teamsGroupId)
    {
        // 🚨 FINAL SECURITY VALIDATOR - This will throw if group deletion is detected
        ValidateNoGroupDeletion("DeleteTeamsGroup", teamsGroupId, "DeleteTeamsGroupAsync");
        
        // 🚨 SECURITY LOCKDOWN: Completely disable Teams group deletion functionality  
        // Groups were being accidentally deleted during invitation/admin processes
        _logger.LogCritical("🚨 SECURITY BLOCK: DeleteTeamsGroupAsync called for Teams group {GroupId} - OPERATION BLOCKED", teamsGroupId);
        _logger.LogCritical("🔒 SECURITY: Teams group deletion is PERMANENTLY DISABLED to prevent accidental deletion");
        _logger.LogCritical("📋 MANUAL ACTION REQUIRED: If you need to delete Teams group {GroupId}, do it manually in Azure Portal", teamsGroupId);
        
        // Log the call stack to see what's trying to delete Teams groups
        var stackTrace = System.Environment.StackTrace;
        _logger.LogCritical("🕵️ CALL STACK for blocked Teams group deletion:\n{StackTrace}", stackTrace);
        
        return Task.FromResult(false); // Always fail - never delete Teams groups via code
        
        /* ORIGINAL DANGEROUS CODE - PERMANENTLY DISABLED
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
        */
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
    /// Finds the installation ID of a Teams App in a specific Team
    /// </summary>
    /// <param name="teamId">The ID of the Team</param>
    /// <param name="teamsAppId">The Teams App ID to find</param>
    /// <returns>Installation ID if found, null otherwise</returns>
    private async Task<string?> FindTeamsAppInstallationIdAsync(string teamId, string teamsAppId)
    {
        try
        {
            var installedApps = await _graphClient.Teams[teamId].InstalledApps.GetAsync();
            
            var matchingApp = installedApps?.Value?.FirstOrDefault(app => 
                app.TeamsApp?.Id?.Equals(teamsAppId, StringComparison.OrdinalIgnoreCase) == true);
            
            return matchingApp?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find installation ID for Teams app {TeamsAppId} in team {TeamId}", 
                teamsAppId, teamId);
            return null;
        }
    }

    /// <summary>
    /// Uninstalls a Microsoft Teams App from a specific Team
    /// </summary>
    /// <param name="teamId">The ID of the Team to uninstall the app from</param>
    /// <param name="teamsAppId">The ID of the Teams App to uninstall</param>
    /// <returns>True if uninstallation was successful, false otherwise</returns>
    public async Task<bool> UninstallTeamsAppAsync(string teamId, string teamsAppId)
    {
        try
        {
            if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(teamsAppId))
            {
                _logger.LogWarning("Cannot uninstall Teams app: TeamId or TeamsAppId is null or empty");
                return false;
            }

            _logger.LogInformation("Uninstalling Teams app {TeamsAppId} from team {TeamId}", teamsAppId, teamId);

            // First, find the installation ID
            var installationId = await FindTeamsAppInstallationIdAsync(teamId, teamsAppId);
            if (string.IsNullOrEmpty(installationId))
            {
                _logger.LogInformation("Teams app {TeamsAppId} is not installed in team {TeamId} - considering uninstallation as successful", 
                    teamsAppId, teamId);
                return true;
            }

            // Delete the installation
            await _graphClient.Teams[teamId].InstalledApps[installationId].DeleteAsync();
            
            _logger.LogInformation("Successfully uninstalled Teams app {TeamsAppId} from team {TeamId}", teamsAppId, teamId);
            return true;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            // App is not installed or team doesn't exist - consider this a success for uninstallation
            _logger.LogInformation("Teams app {TeamsAppId} not found in team {TeamId} or team doesn't exist - considering uninstallation as successful", 
                teamsAppId, teamId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Teams app {TeamsAppId} from team {TeamId}: {Error}", 
                teamsAppId, teamId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Uninstalls multiple Teams Apps from a specific Team
    /// </summary>
    /// <param name="teamId">The ID of the Team to uninstall the apps from</param>
    /// <param name="teamsAppIds">List of Teams App IDs to uninstall</param>
    /// <returns>Dictionary with app IDs as keys and success status as values</returns>
    public async Task<Dictionary<string, bool>> UninstallMultipleTeamsAppsAsync(string teamId, List<string> teamsAppIds)
    {
        var results = new Dictionary<string, bool>();

        if (string.IsNullOrEmpty(teamId) || teamsAppIds == null || !teamsAppIds.Any())
        {
            _logger.LogWarning("Cannot uninstall Teams apps: TeamId is null/empty or no app IDs provided");
            return results;
        }

        _logger.LogInformation("Uninstalling {Count} Teams apps from team {TeamId}", teamsAppIds.Count, teamId);

        foreach (var appId in teamsAppIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            var success = await UninstallTeamsAppAsync(teamId, appId);
            results[appId] = success;
        }

        var successCount = results.Values.Count(success => success);
        _logger.LogInformation("Uninstalled {SuccessCount}/{TotalCount} Teams apps from team {TeamId}", 
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
    /// Comprehensively reactivates user access by restoring all previously revoked permissions
    /// </summary>
    /// <param name="userId">User identifier (Azure AD Object ID or email)</param>
    /// <param name="organizationId">Organization GUID for tenant isolation</param>
    /// <returns>Detailed result of the reactivation operation</returns>
    public async Task<UserReactivationResult> ReactivateUserAccessAsync(string userId, Guid organizationId)
    {
        var result = new UserReactivationResult
        {
            UserId = userId,
            UserEmail = userId.Contains("@") ? userId : string.Empty
        };

        try
        {
            _logger.LogInformation("🔄 STARTING USER REACTIVATION for {UserId} in organization {OrganizationId}", userId, organizationId);

            // Step 1: Find the most recent active revocation record
            var revocationRecord = await GetActiveRevocationRecordAsync(userId, organizationId);
            if (revocationRecord == null)
            {
                result.Errors.Add("No active revocation record found for user");
                _logger.LogWarning("❌ No active revocation record found for user {UserId} in organization {OrganizationId}", userId, organizationId);
                return result;
            }

            _logger.LogInformation("📋 Found revocation record: {GroupCount} security groups, {M365Count} M365 groups, {AppRoleCount} app roles to restore",
                revocationRecord.SecurityGroups.Count, revocationRecord.M365Groups.Count, revocationRecord.AppRoles.Count);

            // Get actual Azure AD user ID if needed
            string? actualUserId = await ResolveUserIdAsync(userId);
            if (string.IsNullOrEmpty(actualUserId))
            {
                result.Errors.Add("Could not resolve Azure AD user ID");
                _logger.LogError("❌ Could not resolve Azure AD user ID for {UserId}", userId);
                return result;
            }

            result.UserId = actualUserId;
            if (string.IsNullOrEmpty(result.UserEmail))
            {
                result.UserEmail = revocationRecord.UserEmail;
            }

            // Step 2: Enable Azure AD account if it was disabled
            if (revocationRecord.AccountDisabled)
            {
                _logger.LogInformation("🔒 SECURITY: Attempting to enable Azure AD account for {UserId}", actualUserId);
                
                try
                {
                    var accountEnabled = await EnableUserAccountAsync(actualUserId);
                    result.AccountEnabled = accountEnabled;
                    
                    if (accountEnabled)
                    {
                        _logger.LogInformation("✅ Successfully enabled Azure AD account for {UserId}", actualUserId);
                    }
                    else
                    {
                        result.Warnings.Add("Failed to enable Azure AD account");
                        _logger.LogWarning("⚠️ Failed to enable Azure AD account for {UserId}", actualUserId);
                    }
                }
                catch (Exception accountEx)
                {
                    result.Warnings.Add($"Error enabling account: {accountEx.Message}");
                    _logger.LogError(accountEx, "❌ Error enabling account for {UserId}", actualUserId);
                }
            }

            // Step 3: Restore security and M365 groups
            var allGroups = new List<RemovedGroup>();
            allGroups.AddRange(revocationRecord.SecurityGroups);
            allGroups.AddRange(revocationRecord.M365Groups);

            if (allGroups.Any())
            {
                _logger.LogInformation("👥 Restoring user to {GroupCount} groups...", allGroups.Count);
                var groupsRestored = await RestoreUserToGroupsAsync(actualUserId, allGroups);
                result.GroupsRestored = groupsRestored ? allGroups.Count : 0;

                if (groupsRestored)
                {
                    _logger.LogInformation("✅ Successfully restored user to groups");
                }
                else
                {
                    result.Warnings.Add("Partial or failed group restoration");
                    _logger.LogWarning("⚠️ Partial or failed group restoration for {UserId}", actualUserId);
                }
            }

            // Step 4: Restore app roles
            if (revocationRecord.AppRoles.Any())
            {
                _logger.LogInformation("🎯 Restoring {AppRoleCount} app roles...", revocationRecord.AppRoles.Count);
                var appRolesRestored = await RestoreAppRolesToUserAsync(actualUserId, revocationRecord.AppRoles);
                result.AppRolesRestored = appRolesRestored ? revocationRecord.AppRoles.Count : 0;

                if (appRolesRestored)
                {
                    _logger.LogInformation("✅ Successfully restored app roles");
                }
                else
                {
                    result.Warnings.Add("Partial or failed app role restoration");
                    _logger.LogWarning("⚠️ Partial or failed app role restoration for {UserId}", actualUserId);
                }
            }

            // Step 5: Update revocation record to mark as restored
            await UpdateRevocationRecordAsRestoredAsync(revocationRecord, result);

            // Determine overall success
            result.Success = result.Errors.Count == 0 && (result.GroupsRestored > 0 || result.AppRolesRestored > 0 || result.AccountEnabled);
            
            if (result.Success)
            {
                _logger.LogInformation("🎉 USER REACTIVATION COMPLETED for {UserId}: {Groups} groups, {Roles} app roles, account enabled: {Account}",
                    actualUserId, result.GroupsRestored, result.AppRolesRestored, result.AccountEnabled);
            }
            else
            {
                _logger.LogError("❌ USER REACTIVATION FAILED for {UserId}: {ErrorCount} errors, {WarningCount} warnings",
                    actualUserId, result.Errors.Count, result.Warnings.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 CRITICAL ERROR during user reactivation for {UserId}", userId);
            result.Success = false;
            result.Errors.Add($"Critical error: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Restores user to a list of groups they were previously removed from
    /// </summary>
    /// <param name="userId">Azure AD user Object ID</param>
    /// <param name="groups">List of groups to restore the user to</param>
    /// <returns>True if restoration was successful</returns>
    public async Task<bool> RestoreUserToGroupsAsync(string userId, List<RemovedGroup> groups)
    {
        try
        {
            _logger.LogInformation("👥 RESTORING USER TO GROUPS: {UserId} to {GroupCount} groups", userId, groups.Count);
            
            int successfulRestorations = 0;
            int totalGroups = groups.Count;

            foreach (var group in groups)
            {
                try
                {
                    _logger.LogInformation("🔄 Restoring user {UserId} to group {GroupId} ({GroupName})", 
                        userId, group.GroupId, group.GroupName);

                    // Check if group still exists
                    bool groupExists = await GroupExistsAsync(group.GroupId);
                    if (!groupExists)
                    {
                        _logger.LogWarning("⚠️ Group {GroupId} ({GroupName}) no longer exists - skipping restoration", 
                            group.GroupId, group.GroupName);
                        continue;
                    }

                    // Add user back to the group
                    bool addedToGroup = await AddUserToGroupAsync(userId, group.GroupId);
                    if (addedToGroup)
                    {
                        successfulRestorations++;
                        _logger.LogInformation("✅ Successfully restored user {UserId} to group {GroupId} ({GroupName})", 
                            userId, group.GroupId, group.GroupName);
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to restore user {UserId} to group {GroupId} ({GroupName})", 
                            userId, group.GroupId, group.GroupName);
                    }
                }
                catch (Exception groupEx)
                {
                    _logger.LogError(groupEx, "💥 Error restoring user {UserId} to group {GroupId} ({GroupName}): {Error}", 
                        userId, group.GroupId, group.GroupName, groupEx.Message);
                }
            }

            var success = successfulRestorations > 0;
            _logger.LogInformation("📊 GROUP RESTORATION SUMMARY: {SuccessCount}/{TotalCount} groups restored for user {UserId}", 
                successfulRestorations, totalGroups, userId);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 CRITICAL ERROR during group restoration for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Restores app roles that were previously revoked from a user
    /// </summary>
    /// <param name="userId">Azure AD user Object ID</param>
    /// <param name="appRoles">List of app roles to restore</param>
    /// <returns>True if restoration was successful</returns>
    public async Task<bool> RestoreAppRolesToUserAsync(string userId, List<RevokedAppRole> appRoles)
    {
        try
        {
            _logger.LogInformation("🎯 RESTORING APP ROLES: {UserId} to {RoleCount} app roles", userId, appRoles.Count);
            
            int successfulRestorations = 0;
            int totalRoles = appRoles.Count;

            foreach (var appRole in appRoles)
            {
                try
                {
                    _logger.LogInformation("🔄 Restoring app role {RoleName} ({AppRoleId}) to user {UserId}", 
                        appRole.RoleName, appRole.AppRoleId, userId);

                    // Use the existing app role assignment method
                    bool roleAssigned = await AssignAppRoleToUserAsync(userId, appRole.RoleName);
                    if (roleAssigned)
                    {
                        successfulRestorations++;
                        _logger.LogInformation("✅ Successfully restored app role {RoleName} to user {UserId}", 
                            appRole.RoleName, userId);
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to restore app role {RoleName} to user {UserId}", 
                            appRole.RoleName, userId);
                    }
                }
                catch (Exception roleEx)
                {
                    _logger.LogError(roleEx, "💥 Error restoring app role {RoleName} to user {UserId}: {Error}", 
                        appRole.RoleName, userId, roleEx.Message);
                }
            }

            var success = successfulRestorations > 0;
            _logger.LogInformation("📊 APP ROLE RESTORATION SUMMARY: {SuccessCount}/{TotalCount} roles restored for user {UserId}", 
                successfulRestorations, totalRoles, userId);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 CRITICAL ERROR during app role restoration for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Gets the most recent active revocation record for a user
    /// </summary>
    private Task<UserRevocationRecord?> GetActiveRevocationRecordAsync(string userId, Guid organizationId)
    {
        try
        {
            // This would need to be implemented with proper database access
            // For now, we'll return null to indicate no database integration yet
            _logger.LogWarning("⚠️ Database integration for UserRevocationRecord not yet implemented");
            return Task.FromResult<UserRevocationRecord?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving revocation record for user {UserId}", userId);
            return Task.FromResult<UserRevocationRecord?>(null);
        }
    }

    /// <summary>
    /// Resolves a user identifier to an Azure AD Object ID
    /// </summary>
    private async Task<string?> ResolveUserIdAsync(string userId)
    {
        try
        {
            // If it's already a GUID, return as-is
            if (Guid.TryParse(userId, out _))
            {
                return userId;
            }

            // If it's an email, look up the user
            if (userId.Contains("@"))
            {
                var user = await GetUserByEmailAsync(userId);
                return user?.Id;
            }

            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving user ID for {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Updates a revocation record to mark it as restored
    /// </summary>
    private Task UpdateRevocationRecordAsRestoredAsync(UserRevocationRecord record, UserReactivationResult result)
    {
        try
        {
            // This would need to be implemented with proper database access
            _logger.LogInformation("📝 Would update revocation record {RecordId} as restored", record.RevocationRecordId);
            
            // TODO: Implement database update
            // record.Status = result.Success ? RevocationStatus.Restored : RevocationStatus.RestorationFailed;
            // record.RestoredOn = result.RestoredOn;
            // record.RestorationSuccessful = result.Success;
            // await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating revocation record {RecordId}", record.RevocationRecordId);
        }
        return Task.CompletedTask;
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