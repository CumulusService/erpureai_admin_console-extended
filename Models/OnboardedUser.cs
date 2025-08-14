using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AdminConsole.Services;

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
    
    /// <summary>
    /// Azure AD Object ID - the stable unique identifier from Azure AD
    /// This is the GUID that should be used for all Azure AD operations instead of email lookup
    /// </summary>
    [StringLength(36)]
    public string? AzureObjectId { get; set; }
    
    // Database assignments - users can access multiple databases from their organization
    public List<Guid> AssignedDatabaseIds { get; set; } = new(); // new_assigneddatabases (multi-select lookup)
    
    // Organization relationship - new_organizationlookup
    public Guid? OrganizationLookupId { get; set; }
    public Guid? OrganizationId { get; set; } // Foreign key for EF relationship
    public Organization? Organization { get; set; }
    
    // User status fields
    public bool IsActive { get; set; } = false; // cr032_isactive
    public bool UserActive { get; set; } = true; // Existing database column
    
    // User role field - determines admin/user permissions (separate from agent types)
    public UserRole AssignedRole { get; set; } = UserRole.User; // Default to regular user
    
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
        // NEW ARCHITECTURE: Use the dedicated AssignedRole field as the primary source of truth
        // This separates user permissions from agent type assignments
        
        // If user has an explicitly assigned role (anything other than the default User), use it
        if (user.AssignedRole == UserRole.OrgAdmin || user.AssignedRole == UserRole.SuperAdmin || user.AssignedRole == UserRole.Developer)
        {
            return user.AssignedRole;
        }
        
        // FALLBACK: For existing users without explicit role assignment, maintain backward compatibility
        // This handles migration period - existing admins should be migrated to use AssignedRole field
        
        // Check legacy agent types for backward compatibility during migration
        if (user.AgentTypes.Contains(LegacyAgentType.Admin)) return UserRole.OrgAdmin;
        
        // Treat SBOAgentAppv1 as admin for backward compatibility (to be migrated)
        if (user.AgentTypes.Contains(LegacyAgentType.SBOAgentAppv1))
        {
            return UserRole.OrgAdmin;
        }
        
        // Default to regular user (either explicitly set as User or fallback)
        return UserRole.User;
    }
    
    /// <summary>
    /// ENHANCED: Method to check if user should be considered an admin based on multiple criteria
    /// This helps identify admin users that might not have proper legacy agent types set
    /// </summary>
    public static bool IsLikelyAdminUser(this OnboardedUser user)
    {
        // Standard check
        if (user.AgentTypes.Contains(LegacyAgentType.Admin)) return true;
        
        // Additional checks could be added here:
        // - Check if user was invited as admin
        // - Check specific agent type IDs that indicate admin access
        // - Check user permissions/roles in other systems
        
        return false;
    }
    
    public static string GetInvitationStatus(this OnboardedUser user)
    {
        // If user was never invited, they can't have accepted
        if (user.LastInvitationDate == null || user.LastInvitationDate == DateTime.MinValue)
        {
            return "NotInvited";
        }
        
        // CRITICAL FIX: Database StateCode/StatusCode being Active does not mean B2B invitation was accepted
        // StateCode.Active only means the user record is active in the database, not that they accepted the B2B invitation
        // The real invitation status must come from Azure AD via GetRealTimeInvitationStatusAsync()
        
        // Conservative approach: If we only have database info, assume pending until proven otherwise by Azure AD
        // This prevents showing "Accepted" when the user hasn't actually clicked the invitation link
        return "PendingAcceptance";
    }

    /// <summary>
    /// Gets the real-time invitation status by checking Azure AD via GraphService
    /// This method should be used when you need the most up-to-date status
    /// </summary>
    public static async Task<string> GetRealTimeInvitationStatusAsync(this OnboardedUser user, IGraphService graphService)
    {
        // If user was never invited, they can't have accepted
        if (user.LastInvitationDate == null || user.LastInvitationDate == DateTime.MinValue)
        {
            return "NotInvited";
        }
        
        try
        {
            // Check current status in Azure AD
            var statusCheck = await graphService.CheckInvitationStatusAsync(user.Email);
            
            // Return Azure AD status directly
            return statusCheck.InvitationStatus switch
            {
                InvitationStatus.Accepted => "Accepted",
                InvitationStatus.PendingAcceptance => "PendingAcceptance",
                InvitationStatus.NotInvited => "NotInvited",
                _ => "PendingAcceptance"
            };
        }
        catch (Exception)
        {
            // If Azure AD check fails, fall back to database status
            return GetInvitationStatus(user);
        }
    }
}