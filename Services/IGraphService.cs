using AdminConsole.Models;

namespace AdminConsole.Services;

public class GraphPermissionStatus
{
    public bool CanDisableUsers { get; set; }
    public bool CanDeleteUsers { get; set; }
    public bool CanManageGroups { get; set; }
    public List<string> MissingPermissions { get; set; } = new();
    public List<string> ErrorMessages { get; set; } = new();
}

public interface IGraphService
{
    // Legacy methods for backward compatibility
    Task<GuestUser?> InviteAdminUserAsync(string email, string displayName, string organizationName);
    Task<GuestUser?> InviteUserAsync(string email, string displayName, string organizationId);
    Task<IEnumerable<GuestUser>> GetGuestUsersAsync(string organizationId);
    Task<bool> RevokeUserAccessAsync(string userId);
    Task<bool> DisableUserAccountAsync(string userId);
    Task<bool> EnableUserAccountAsync(string userId);
    Task<GraphPermissionStatus> CheckUserManagementPermissionsAsync();
    Task<GuestUser?> GetCurrentUserAsync();
    
    // NEW: B2B Invitation methods
    Task<GraphInvitationResult> InviteGuestUserAsync(string email, string organizationName);
    Task<GraphInvitationResult> InviteGuestUserAsync(string email, string displayName, string organizationName, string redirectUri, List<string> agentShareUrls, bool isAdminUser);
    Task<bool> CancelInvitationAsync(string invitationId);
    Task<bool> ResendInvitationAsync(string userId);
    Task<InvitationStatusResult> CheckInvitationStatusAsync(string email);
    
    // NEW: Azure AD Security Group management
    Task<string> CreateSecurityGroupAsync(string groupName, string description);
    Task<bool> DeleteSecurityGroupAsync(string groupId);
    Task<bool> AddUserToGroupAsync(string userId, string groupName);
    Task<bool> RemoveUserFromGroupAsync(string userId, string groupName);
    Task<List<string>> GetGroupMembersAsync(string groupName);
    Task<bool> GroupExistsAsync(string groupName);
    
    // NEW: User management
    Task<List<GuestUser>> GetAllGuestUsersAsync();
    Task<List<GuestUser>> GetAllTenantUsersAsync(); // NEW: Get all tenant users (not guests)
    Task<List<GuestUser>> GetTenantUsersByDomainAsync(string domain); // NEW: Filter tenant users by domain
    Task<List<GuestUser>> GetAllUsersAsync(); // NEW: Get both tenant and guest users
    Task<GuestUser?> GetUserByEmailAsync(string email);
    Task<bool> UserExistsAsync(string userId);
    Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole);
    Task<bool> DeactivateUserAsync(string userId);
    Task<bool> ReactivateUserAsync(string userId);
    
    // NEW: App role assignment and revocation
    Task<bool> AssignAppRoleToUserAsync(string userId, string appRoleName);
    Task<bool> RevokeAppRoleFromUserAsync(string userId, string appRoleName);
    
    // NEW: Comprehensive user reactivation
    Task<UserReactivationResult> ReactivateUserAccessAsync(string userId, Guid organizationId);
    Task<bool> RestoreUserToGroupsAsync(string userId, List<RemovedGroup> groups);
    Task<bool> RestoreAppRolesToUserAsync(string userId, List<RevokedAppRole> appRoles);
    
    // NEW: Microsoft Teams management
    Task<TeamsGroupResult> CreateTeamsGroupAsync(string groupName, string description, string organizationId, List<string>? teamsAppIds = null);
    Task<bool> AddUserToTeamsGroupAsync(string userId, string teamsGroupId);
    Task<bool> RemoveUserFromTeamsGroupAsync(string userId, string teamsGroupId);
    Task<List<string>> GetTeamsGroupMembersAsync(string teamsGroupId);
    Task<List<Microsoft.Graph.Models.Group>> GetUserGroupMembershipsAsync(string userId);
    Task<bool> IsUserMemberOfGroupAsync(string userId, string groupId);
    Task<bool> DeleteTeamsGroupAsync(string teamsGroupId);
    
    // NEW: Teams App management
    Task<bool> InstallTeamsAppAsync(string teamId, string teamsAppId);
    Task<Dictionary<string, bool>> InstallMultipleTeamsAppsAsync(string teamId, List<string> teamsAppIds);
    Task<bool> UninstallTeamsAppAsync(string teamId, string teamsAppId);
    Task<Dictionary<string, bool>> UninstallMultipleTeamsAppsAsync(string teamId, List<string> teamsAppIds);
    
    // NEW: Teams App Permission Policy management
    Task<bool> ConfigureTeamsAppPermissionPoliciesAsync(string groupId, string groupName, List<string> teamsAppIds);
    Task<bool> ConfigureTenantLevelTeamsAppApprovalAsync(string teamsAppId);
}

/// <summary>
/// Result of a Graph API invitation operation
/// </summary>
public class GraphInvitationResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string InvitationUrl { get; set; } = string.Empty;
    public string InvitationId { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of a Teams group creation operation
/// </summary>
public class TeamsGroupResult
{
    public bool Success { get; set; }
    public string GroupId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamUrl { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of a user reactivation operation
/// </summary>
public class UserReactivationResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int GroupsRestored { get; set; } = 0;
    public int AppRolesRestored { get; set; } = 0;
    public bool AccountEnabled { get; set; } = false;
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime RestoredOn { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of checking invitation status for a user
/// </summary>
public class InvitationStatusResult
{
    public string Email { get; set; } = string.Empty;
    public InvitationStatus InvitationStatus { get; set; } = InvitationStatus.Unknown;
    public bool ExistsInAzureAD { get; set; } = false;
    public string? AzureUserId { get; set; }
    public string UserType { get; set; } = string.Empty;
    public DateTime? AcceptedDate { get; set; }
    public DateTime CheckedOn { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public DateTime? LastInvitationDate { get; set; }
    public int InvitationAttempts { get; set; } = 0;
}

/// <summary>
/// Enhanced invitation status enum
/// </summary>
public enum InvitationStatus
{
    /// <summary>
    /// User has never been invited
    /// </summary>
    NotInvited = 0,

    /// <summary>
    /// Invitation sent but not yet accepted
    /// </summary>
    PendingAcceptance = 1,

    /// <summary>
    /// User has accepted the invitation and is active
    /// </summary>
    Accepted = 2,

    /// <summary>
    /// Invitation failed to send or is in error state
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Status could not be determined
    /// </summary>
    Unknown = 4,

    /// <summary>
    /// Invitation was sent but may have expired
    /// </summary>
    Expired = 5
}