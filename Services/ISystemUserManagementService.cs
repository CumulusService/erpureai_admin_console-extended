using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// System-level user management service for Master Developer users
/// Provides comprehensive user promotion, role assignment, and management capabilities
/// </summary>
public interface ISystemUserManagementService
{
    /// <summary>
    /// Get all tenant users (internal organization users) from Azure AD
    /// </summary>
    Task<List<SystemUser>> GetAllTenantUsersAsync();
    
    /// <summary>
    /// Get all guest users from Azure AD
    /// </summary>
    Task<List<SystemUser>> GetAllGuestUsersAsync();
    
    /// <summary>
    /// Get all users (both tenant and guest) from Azure AD
    /// </summary>
    Task<List<SystemUser>> GetAllSystemUsersAsync();
    
    /// <summary>
    /// Get tenant users filtered by domain (e.g., erpure.ai users)
    /// </summary>
    Task<List<SystemUser>> GetTenantUsersByDomainAsync(string domain);
    
    /// <summary>
    /// Promote an existing Azure AD user to SuperAdmin or Developer role
    /// Creates database record and assigns appropriate Azure AD app roles
    /// </summary>
    Task<UserPromotionResult> PromoteUserAsync(string userId, UserRole targetRole, Guid? organizationId = null);
    
    /// <summary>
    /// Demote a user to a lower role or remove system access completely
    /// Updates database record and revokes Azure AD app roles
    /// </summary>
    Task<UserDemotionResult> DemoteUserAsync(string userId, UserRole? targetRole = null);
    
    /// <summary>
    /// Create a new SuperAdmin or Developer user via external invitation
    /// Sends B2B invitation and sets up database record with role
    /// </summary>
    Task<UserCreationResult> CreateSystemUserAsync(string email, string displayName, UserRole role, Guid? organizationId = null);
    
    /// <summary>
    /// Get comprehensive user information including database and Azure AD status
    /// </summary>
    Task<SystemUserDetails?> GetSystemUserDetailsAsync(string userId);
    
    /// <summary>
    /// Update user role in both database and Azure AD
    /// </summary>
    Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole);
    
    /// <summary>
    /// Deactivate user - removes from database and disables Azure AD account
    /// </summary>
    Task<bool> DeactivateSystemUserAsync(string userId);
    
    /// <summary>
    /// Reactivate user - restores database record and re-enables Azure AD account
    /// </summary>
    Task<UserReactivationResult> ReactivateSystemUserAsync(string userId, UserRole role, Guid? organizationId = null);
    
    /// <summary>
    /// Get system statistics for Master Developer dashboard
    /// </summary>
    Task<SystemUserStatistics> GetSystemStatisticsAsync();
}

/// <summary>
/// System user status enumeration for accurate status tracking
/// </summary>
public enum SystemUserStatus
{
    Unknown,
    Active,
    PendingInvitation,
    InvitationExpired,
    Disabled,
    Revoked
}

/// <summary>
/// Enhanced system user model with both Azure AD and database information
/// </summary>
public class SystemUser
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty; // Member, Guest
    public bool IsEnabled { get; set; } = true;
    public DateTime? CreatedDateTime { get; set; }
    
    // Enhanced status information
    public SystemUserStatus Status { get; set; } = SystemUserStatus.Unknown;
    public InvitationStatus? InvitationStatus { get; set; }
    public DateTime? LastSignInDateTime { get; set; }
    public DateTime? InvitationAcceptedDate { get; set; }
    public DateTime? InvitationExpiryDate { get; set; }
    
    // Database information
    public bool HasDatabaseRecord { get; set; } = false;
    public UserRole? DatabaseRole { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public DateTime? DatabaseCreatedOn { get; set; }
    
    // Enhanced user context
    public string AzureObjectId { get; set; } = string.Empty;
    public List<string> AssignedAppRoles { get; set; } = new();
    public List<Guid> AgentTypeIds { get; set; } = new();
    public bool IsSystemUser => DatabaseRole == UserRole.Developer || DatabaseRole == UserRole.SuperAdmin;
    
    // Computed properties for UI
    public string StatusDisplayName => Status switch
    {
        SystemUserStatus.Active => "Active",
        SystemUserStatus.PendingInvitation => "Pending Invitation",
        SystemUserStatus.InvitationExpired => "Invitation Expired",
        SystemUserStatus.Disabled => "Disabled",
        SystemUserStatus.Revoked => "Access Revoked",
        _ => "Unknown"
    };
    
    public string StatusBadgeClass => Status switch
    {
        SystemUserStatus.Active => "bg-success",
        SystemUserStatus.PendingInvitation => "bg-warning",
        SystemUserStatus.InvitationExpired => "bg-danger",
        SystemUserStatus.Disabled => "bg-secondary",
        SystemUserStatus.Revoked => "bg-danger",
        _ => "bg-light text-dark"
    };
    
    public string RoleDisplayName => DatabaseRole?.ToString() ?? "No System Access";
    
    public string RoleBadgeClass => DatabaseRole switch
    {
        UserRole.Developer => "bg-success",
        UserRole.SuperAdmin => "bg-warning text-dark",
        UserRole.OrgAdmin => "bg-info",
        UserRole.User => "bg-secondary",
        _ => "bg-light text-dark"
    };
}

/// <summary>
/// Detailed system user information for management interface
/// </summary>
public class SystemUserDetails : SystemUser
{
    public List<string> GroupMemberships { get; set; } = new();
    public bool CanPromoteToSuperAdmin => !HasDatabaseRecord || DatabaseRole != UserRole.SuperAdmin;
    public bool CanPromoteToDeveloper => !HasDatabaseRecord || DatabaseRole != UserRole.Developer;
    public bool CanDemote => HasDatabaseRecord && (DatabaseRole == UserRole.SuperAdmin || DatabaseRole == UserRole.Developer);
    public List<string> AvailableActions { get; set; } = new();
}

/// <summary>
/// Result of user promotion operation
/// </summary>
public class UserPromotionResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public UserRole TargetRole { get; set; }
    public bool DatabaseRecordCreated { get; set; }
    public bool AppRoleAssigned { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime PromotedOn { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of user demotion operation
/// </summary>
public class UserDemotionResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public UserRole? PreviousRole { get; set; }
    public UserRole? NewRole { get; set; }
    public bool DatabaseRecordUpdated { get; set; }
    public bool DatabaseRecordRemoved { get; set; }
    public bool AppRoleRevoked { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime DemotedOn { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of creating a new system user
/// </summary>
public class UserCreationResult
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string InvitationUrl { get; set; } = string.Empty;
    public UserRole AssignedRole { get; set; }
    public bool InvitationSent { get; set; }
    public bool DatabaseRecordCreated { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// System-wide user statistics for Master Developer dashboard
/// </summary>
public class SystemUserStatistics
{
    public int TotalTenantUsers { get; set; }
    public int TotalGuestUsers { get; set; }
    public int TotalDatabaseUsers { get; set; }
    public int SuperAdminCount { get; set; }
    public int DeveloperCount { get; set; }
    public int OrgAdminCount { get; set; }
    public int OrgUserCount { get; set; }
    public int ActiveUsersCount { get; set; }
    public int InactiveUsersCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}