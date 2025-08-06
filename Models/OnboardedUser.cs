using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdminConsole.Models;

/// <summary>
/// Represents an onboarded user from the new_OnboardedUser Dataverse table
/// Maps to the exact schema from your Dataverse metadata
/// </summary>
public class OnboardedUser
{
    // Primary Key - new_onboardeduserid
    [Key]
    public Guid OnboardedUserId { get; set; } = Guid.NewGuid();
    
    // Primary name field - new_onboardeduser1 (required)
    public string Name { get; set; } = string.Empty;
    
    // Core user information
    public string Email { get; set; } = string.Empty; // cr032_email (required)
    public string FullName { get; set; } = string.Empty; // User's full name for display
    
    // Database assignments - users can access multiple databases from their organization
    public List<Guid> AssignedDatabaseIds { get; set; } = new(); // new_assigneddatabases (multi-select lookup)
    
    // Organization relationship - new_organizationlookup
    public Guid? OrganizationLookupId { get; set; }
    public Guid? OrganizationId { get; set; } // Foreign key for EF relationship
    public Organization? Organization { get; set; }
    
    // User status fields
    public bool IsActive { get; set; } = false; // cr032_isactive
    public bool UserActive { get; set; } = true; // Existing database column
    
    // Agent-related fields
    public List<LegacyAgentType> AgentTypes { get; set; } = new(); // new_agenttypes (multi-select) - Legacy field for backward compatibility
    public List<Guid> AgentTypeIds { get; set; } = new(); // New database-driven agent type IDs for future implementation
    public Guid? AgentNameId { get; set; } // new_agentname (lookup to cr032_copilotchannelinfo)
    public string AssignedSupervisorEmail { get; set; } = string.Empty; // new_assignedsupervisoremail
    
    // System fields (standard Dataverse fields)
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedOn { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public Guid? ModifiedBy { get; set; }
    public Guid? CreatedOnBehalfBy { get; set; }
    public Guid? ModifiedOnBehalfBy { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? OwningBusinessUnit { get; set; }
    public Guid? OwningTeam { get; set; }
    public Guid? OwningUser { get; set; }
    
    // State and Status
    public StateCode StateCode { get; set; } = StateCode.Active;
    public StatusCode StatusCode { get; set; } = StatusCode.Active;
    
    // Import and timezone fields
    public int? ImportSequenceNumber { get; set; }
    public DateTime? OverriddenCreatedOn { get; set; }
    public int? TimeZoneRuleVersionNumber { get; set; }
    public int? UTCConversionTimeZoneCode { get; set; }
    
    // New fields for enhanced functionality (additive - won't break existing code)
    /// <summary>
    /// Soft delete flag - when true, user is considered deleted but data is preserved
    /// </summary>
    public bool IsDeleted { get; set; } = false;
    
    /// <summary>
    /// Custom redirect URI for different user types (admin vs regular user)
    /// </summary>
    [StringLength(500)]
    public string? RedirectUri { get; set; }
    
    /// <summary>
    /// When the last invitation was sent to this user
    /// </summary>
    public DateTime? LastInvitationDate { get; set; }
}

/// <summary>
/// Database type options from cr032_databasetype option set
/// </summary>
public enum DatabaseType
{
    HANA = 379960000,
    MSSQL = 379960001
}

/// <summary>
/// Legacy agent type options from new_agenttypes multi-select option set
/// Kept for backward compatibility - use AgentType table for new implementations
/// </summary>
public enum LegacyAgentType
{
    SBOAgentAppv1 = 100000000,
    Sales = 100000001,
    Admin = 100000002
}

/// <summary>
/// State code values for the OnboardedUser entity
/// </summary>
public enum StateCode
{
    Active = 0,
    Inactive = 1
}

/// <summary>
/// Status code values for the OnboardedUser entity
/// </summary>
public enum StatusCode
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Extension methods for OnboardedUser to provide backward compatibility
/// </summary>
public static class OnboardedUserExtensions
{
    public static string GetDisplayName(this OnboardedUser user)
    {
        return user.Name;
    }
    
    public static string GetUserPrincipalName(this OnboardedUser user)
    {
        return $"{user.Email.Replace("@", "_")}#EXT#@erpure.onmicrosoft.com";
    }
    
    public static UserRole GetUserRole(this OnboardedUser user)
    {
        if (user.AgentTypes.Contains(LegacyAgentType.Admin)) return UserRole.OrgAdmin;
        if (user.AgentTypes.Contains(LegacyAgentType.Sales)) return UserRole.User;
        return UserRole.User;
    }
    
    public static string GetInvitationStatus(this OnboardedUser user)
    {
        return user.StateCode == StateCode.Active ? "Accepted" : "PendingAcceptance";
    }
}