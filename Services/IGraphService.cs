using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IGraphService
{
    // Legacy methods for backward compatibility
    Task<GuestUser?> InviteAdminUserAsync(string email, string displayName, string organizationName);
    Task<GuestUser?> InviteUserAsync(string email, string displayName, string organizationId);
    Task<IEnumerable<GuestUser>> GetGuestUsersAsync(string organizationId);
    Task<bool> RevokeUserAccessAsync(string userId);
    Task<GuestUser?> GetCurrentUserAsync();
    
    // NEW: B2B Invitation methods
    Task<GraphInvitationResult> InviteGuestUserAsync(string email, string organizationName);
    Task<GraphInvitationResult> InviteGuestUserAsync(string email, string organizationName, string redirectUri, List<string> agentShareUrls, bool isAdminUser);
    Task<bool> CancelInvitationAsync(string invitationId);
    Task<bool> ResendInvitationAsync(string userId);
    
    // NEW: Azure AD Security Group management
    Task<string> CreateSecurityGroupAsync(string groupName, string description);
    Task<bool> DeleteSecurityGroupAsync(string groupId);
    Task<bool> AddUserToGroupAsync(string userId, string groupName);
    Task<bool> RemoveUserFromGroupAsync(string userId, string groupName);
    Task<List<string>> GetGroupMembersAsync(string groupName);
    Task<bool> GroupExistsAsync(string groupName);
    
    // NEW: User management
    Task<List<GuestUser>> GetAllGuestUsersAsync();
    Task<GuestUser?> GetUserByEmailAsync(string email);
    Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole);
    Task<bool> DeactivateUserAsync(string userId);
    Task<bool> ReactivateUserAsync(string userId);
    
    // NEW: App role assignment
    Task<bool> AssignAppRoleToUserAsync(string userId, string appRoleName);
    
    // NEW: Microsoft Teams management
    Task<TeamsGroupResult> CreateTeamsGroupAsync(string groupName, string description, string organizationId, List<string>? teamsAppIds = null);
    Task<bool> AddUserToTeamsGroupAsync(string userId, string teamsGroupId);
    Task<bool> RemoveUserFromTeamsGroupAsync(string userId, string teamsGroupId);
    Task<List<string>> GetTeamsGroupMembersAsync(string teamsGroupId);
    Task<bool> DeleteTeamsGroupAsync(string teamsGroupId);
    
    // NEW: Teams App management
    Task<bool> InstallTeamsAppAsync(string teamId, string teamsAppId);
    Task<Dictionary<string, bool>> InstallMultipleTeamsAppsAsync(string teamId, List<string> teamsAppIds);
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