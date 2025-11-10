using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdminConsole.Models;

/// <summary>
/// Represents an organization from the new_Organization Dataverse table
/// Maps to the exact schema from your Dataverse metadata
/// </summary>
public class Organization
{
    // Primary Key - new_organizationid
    [Key]
    public Guid OrganizationId { get; set; } = Guid.NewGuid();
    
    // Primary name field - new_name (required)
    public string Name { get; set; } = string.Empty;
    
    // Admin information
    public string AdminEmail { get; set; } = string.Empty; // cr032_adminemail (required)
    
    // KEY VAULT CONFIGURATION (CRITICAL for your multi-tenant setup)
    public string KeyVaultUri { get; set; } = string.Empty; // new_keyvaulturi
    public string KeyVaultSecretPrefix { get; set; } = string.Empty; // new_keyvaultsecretprefix
    
    // MICROSOFT 365 INTEGRATION
    public string? M365GroupId { get; set; } // Microsoft 365 Group Object ID (nullable for existing organizations)
    
    // Database configuration
    public OrganizationDatabaseType DatabaseType { get; set; } = OrganizationDatabaseType.SQL; // new_databasetype (default: SQL)
    
    // SAP Configuration - nullable to support proper empty states
    public string? SAPServiceLayerHostname { get; set; } // new_sapservicelayerhostname
    public string? SAPAPIGatewayHostname { get; set; } // new_sapapigatewayhostname
    public string? SAPBusinessOneWebClientHost { get; set; } // cr032_sapbusinessonewebclienthost
    public string? DocumentCode { get; set; } // cr032_documentcode
    
    // Document storage container (for document processing service) 
    public string DocumentStorageContainer { get; set; } = "default";
    
    // ORGANIZATION-LEVEL AGENT TYPE ALLOCATION (SuperAdmin responsibility)
    public string OrganizationAgentTypeIds { get; set; } = string.Empty; // JSON array of agent type GUIDs allocated to organization
    
    // USER INVITATION PERMISSIONS (SuperAdmin control for granular access management)
    /// <summary>
    /// Controls whether the organization admin can invite users to their organization.
    /// When false, creates a "restricted org admin" who cannot manage users.
    /// Defaults to true for backward compatibility.
    /// </summary>
    public bool AllowUserInvitations { get; set; } = true;
    
    // System fields (standard Dataverse fields)
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedOn { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public Guid? ModifiedBy { get; set; }
    public Guid? CreatedOnBehalfBy { get; set; }
    public Guid? ModifiedOnBehalfBy { get; set; }
    
    // Ownership
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
    
    // Navigation properties
    public List<OnboardedUser> Users { get; set; } = new();
    
    // LEGACY PROPERTIES FOR BACKWARD COMPATIBILITY (made settable for mapping)
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public string AdminUserName { get; set; } = string.Empty;
    public string AdminUserEmail { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int UserCount { get; set; } = 0;
    public int SecretCount { get; set; } = 0; // Will be calculated from Key Vault
    
    // Helper method to sync legacy properties with new properties
    public void SyncLegacyProperties()
    {
        Id = OrganizationId.ToString();
        Domain = ExtractDomainFromEmail();
        AdminUserId = CreatedBy?.ToString() ?? string.Empty;
        AdminUserName = AdminEmail.Split('@').FirstOrDefault() ?? string.Empty;
        AdminUserEmail = AdminEmail;
        CreatedDate = CreatedOn;
        IsActive = StateCode == StateCode.Active;
        UserCount = Users?.Count ?? 0;
    }
    
    /// <summary>
    /// Gets the effective Key Vault URI for this organization
    /// Falls back to shared vault if organization doesn't have its own
    /// </summary>
    public string GetEffectiveKeyVaultUri()
    {
        return !string.IsNullOrEmpty(KeyVaultUri) 
            ? KeyVaultUri 
            : "https://kv-a3632d43-3b45-1211-f8.vault.azure.net/"; // Your shared vault
    }
    
    /// <summary>
    /// Gets the effective secret prefix for this organization
    /// Uses organization-specific prefix or falls back to domain-based prefix
    /// </summary>
    public string GetEffectiveSecretPrefix()
    {
        if (!string.IsNullOrEmpty(KeyVaultSecretPrefix))
            return KeyVaultSecretPrefix;
            
        // Generate prefix from domain if not set
        var domain = ExtractDomainFromEmail();
        return domain.Replace(".", "-").ToLower();
    }
    
    private string ExtractDomainFromEmail()
    {
        if (string.IsNullOrEmpty(AdminEmail)) return string.Empty;
        var atIndex = AdminEmail.IndexOf('@');
        return atIndex > 0 ? AdminEmail.Substring(atIndex + 1) : string.Empty;
    }
    
    /// <summary>
    /// Gets the list of agent type GUIDs allocated to this organization by SuperAdmin
    /// </summary>
    public List<Guid> GetOrganizationAgentTypeIds()
    {
        if (string.IsNullOrEmpty(OrganizationAgentTypeIds))
            return new List<Guid>();
            
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(OrganizationAgentTypeIds) ?? new List<Guid>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<Guid>();
        }
    }
    
    /// <summary>
    /// Sets the list of agent type GUIDs allocated to this organization by SuperAdmin
    /// </summary>
    public void SetOrganizationAgentTypeIds(IEnumerable<Guid> agentTypeIds)
    {
        var agentTypeList = agentTypeIds?.ToList() ?? new List<Guid>();
        OrganizationAgentTypeIds = System.Text.Json.JsonSerializer.Serialize(agentTypeList);
    }
}

/// <summary>
/// Database type options from new_databasetype option set
/// Values match your Dataverse metadata exactly
/// </summary>
public enum OrganizationDatabaseType
{
    SQL = 100000000,   // Default value from your schema
    HANA = 100000001
}

/// <summary>
/// Extension methods for Organization
/// </summary>
public static class OrganizationExtensions
{
    /// <summary>
    /// Generates Azure AD Security Group name for this organization
    /// </summary>
    public static string GetSecurityGroupName(this Organization org)
    {
        var domain = org.Domain.Replace(".", "-");
        return $"Partner-{domain}-Users";
    }
    
    /// <summary>
    /// Gets the organization's domain for invitation validation
    /// </summary>
    public static string GetInvitationDomain(this Organization org)
    {
        return org.Domain;
    }
    
    /// <summary>
    /// Checks if the organization has an associated Microsoft 365 Group
    /// </summary>
    public static bool HasM365Group(this Organization org)
    {
        return !string.IsNullOrEmpty(org.M365GroupId) && Guid.TryParse(org.M365GroupId, out _);
    }
    
    /// <summary>
    /// Validates if an email belongs to this organization's domain
    /// </summary>
    public static bool CanInviteEmail(this Organization org, string email)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(org.Domain))
            return false;
            
        return email.EndsWith($"@{org.Domain}", StringComparison.OrdinalIgnoreCase);
    }
}