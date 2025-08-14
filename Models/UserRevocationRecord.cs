using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace AdminConsole.Models;

/// <summary>
/// Tracks user revocation details for proper access restoration
/// Stores all information needed to restore a user's original access when reactivating
/// </summary>
public class UserRevocationRecord
{
    /// <summary>
    /// Primary key for the revocation record
    /// </summary>
    [Key]
    public Guid RevocationRecordId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User identifier - can be Azure AD Object ID or email
    /// </summary>
    [Required]
    [StringLength(255)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User's email address
    /// </summary>
    [Required]
    [StringLength(255)]
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>
    /// User's display name at time of revocation
    /// </summary>
    [StringLength(255)]
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Organization this revocation belongs to (for tenant isolation)
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Organization navigation property (optional - may not be populated due to permissions)
    /// </summary>
    public Organization? Organization { get; set; }

    /// <summary>
    /// Email of the user who performed the revocation
    /// </summary>
    [Required]
    [StringLength(255)]
    public string RevokedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the revocation was performed
    /// </summary>
    [Required]
    public DateTime RevokedOn { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Email of the user who restored access (if applicable)
    /// </summary>
    [StringLength(255)]
    public string? RestoredBy { get; set; }

    /// <summary>
    /// When access was restored (if applicable)
    /// </summary>
    public DateTime? RestoredOn { get; set; }

    /// <summary>
    /// Current status of the revocation
    /// </summary>
    [Required]
    public RevocationStatus Status { get; set; } = RevocationStatus.Active;

    /// <summary>
    /// Azure AD Security Groups the user was removed from (JSON array)
    /// Format: [{"GroupId": "guid", "GroupName": "name"}, ...]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string SecurityGroupsRemoved { get; set; } = "[]";

    /// <summary>
    /// Microsoft 365 Groups the user was removed from (JSON array)
    /// Format: [{"GroupId": "guid", "GroupName": "name"}, ...]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string M365GroupsRemoved { get; set; } = "[]";

    /// <summary>
    /// App roles that were revoked (JSON array)
    /// Format: [{"AppRoleId": "guid", "RoleName": "OrgAdmin"}, ...]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string AppRolesRevoked { get; set; } = "[]";

    /// <summary>
    /// Additional revocation details (JSON object)
    /// Can store custom fields, error messages, etc.
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string AdditionalDetails { get; set; } = "{}";

    /// <summary>
    /// Whether the Azure AD account was disabled/deleted during revocation
    /// </summary>
    public bool AccountDisabled { get; set; } = false;

    /// <summary>
    /// Whether the revocation was successful (all operations completed)
    /// </summary>
    public bool RevocationSuccessful { get; set; } = false;

    /// <summary>
    /// Whether the restoration was successful (if attempted)
    /// </summary>
    public bool? RestorationSuccessful { get; set; }

    /// <summary>
    /// Error message if revocation/restoration failed
    /// </summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    // Standard audit fields
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedOn { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public Guid? ModifiedBy { get; set; }

    // Helper methods for JSON serialization/deserialization
    
    /// <summary>
    /// Gets the security groups as a list of objects
    /// </summary>
    [NotMapped]
    public List<RemovedGroup> SecurityGroups
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<List<RemovedGroup>>(SecurityGroupsRemoved) ?? new List<RemovedGroup>();
            }
            catch
            {
                return new List<RemovedGroup>();
            }
        }
        set
        {
            try
            {
                SecurityGroupsRemoved = JsonSerializer.Serialize(value);
            }
            catch
            {
                SecurityGroupsRemoved = "[]";
            }
        }
    }

    /// <summary>
    /// Gets the M365 groups as a list of objects
    /// </summary>
    [NotMapped]
    public List<RemovedGroup> M365Groups
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<List<RemovedGroup>>(M365GroupsRemoved) ?? new List<RemovedGroup>();
            }
            catch
            {
                return new List<RemovedGroup>();
            }
        }
        set
        {
            try
            {
                M365GroupsRemoved = JsonSerializer.Serialize(value);
            }
            catch
            {
                M365GroupsRemoved = "[]";
            }
        }
    }

    /// <summary>
    /// Gets the app roles as a list of objects
    /// </summary>
    [NotMapped]
    public List<RevokedAppRole> AppRoles
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<List<RevokedAppRole>>(AppRolesRevoked) ?? new List<RevokedAppRole>();
            }
            catch
            {
                return new List<RevokedAppRole>();
            }
        }
        set
        {
            try
            {
                AppRolesRevoked = JsonSerializer.Serialize(value);
            }
            catch
            {
                AppRolesRevoked = "[]";
            }
        }
    }

    /// <summary>
    /// Gets additional details as a dictionary
    /// </summary>
    [NotMapped]
    public Dictionary<string, object> Details
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(AdditionalDetails) ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }
        set
        {
            try
            {
                AdditionalDetails = JsonSerializer.Serialize(value);
            }
            catch
            {
                AdditionalDetails = "{}";
            }
        }
    }
}

/// <summary>
/// Represents a group that was removed during revocation
/// </summary>
public class RemovedGroup
{
    /// <summary>
    /// Azure AD Group Object ID
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the group
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Type of group (Security, M365, Distribution, etc.)
    /// </summary>
    public string GroupType { get; set; } = string.Empty;

    /// <summary>
    /// When the user was removed from this group
    /// </summary>
    public DateTime RemovedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an app role that was revoked
/// </summary>
public class RevokedAppRole
{
    /// <summary>
    /// App role ID (GUID)
    /// </summary>
    public string AppRoleId { get; set; } = string.Empty;

    /// <summary>
    /// Role name (e.g., "OrgAdmin")
    /// </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// Service principal ID that owns this role
    /// </summary>
    public string ServicePrincipalId { get; set; } = string.Empty;

    /// <summary>
    /// When the role was revoked
    /// </summary>
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a user revocation record
/// </summary>
public enum RevocationStatus
{
    /// <summary>
    /// Revocation is active - user access has been revoked
    /// </summary>
    Active = 0,

    /// <summary>
    /// Access has been restored
    /// </summary>
    Restored = 1,

    /// <summary>
    /// Revocation failed - user may still have access
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Revocation was partially successful - some access may remain
    /// </summary>
    PartiallyRevoked = 3,

    /// <summary>
    /// Restoration failed - user access remains revoked
    /// </summary>
    RestorationFailed = 4,

    /// <summary>
    /// Restoration was partial - some access may not have been restored
    /// </summary>
    PartiallyRestored = 5
}

/// <summary>
/// Extension methods for UserRevocationRecord
/// </summary>
public static class UserRevocationRecordExtensions
{
    /// <summary>
    /// Checks if the revocation is currently active
    /// </summary>
    public static bool IsActivelyRevoked(this UserRevocationRecord record)
    {
        return record.Status == RevocationStatus.Active || 
               record.Status == RevocationStatus.PartiallyRevoked;
    }

    /// <summary>
    /// Checks if access has been restored
    /// </summary>
    public static bool IsRestored(this UserRevocationRecord record)
    {
        return record.Status == RevocationStatus.Restored || 
               record.Status == RevocationStatus.PartiallyRestored;
    }

    /// <summary>
    /// Gets the total number of items removed during revocation
    /// </summary>
    public static int GetTotalRemovedItemsCount(this UserRevocationRecord record)
    {
        return record.SecurityGroups.Count + record.M365Groups.Count + record.AppRoles.Count;
    }

    /// <summary>
    /// Checks if the record has any groups or roles to restore
    /// </summary>
    public static bool HasItemsToRestore(this UserRevocationRecord record)
    {
        return record.GetTotalRemovedItemsCount() > 0;
    }
}