using System.ComponentModel.DataAnnotations;

namespace AdminConsole.Models;

/// <summary>
/// Enhanced AgentTypeEntity with global security group mapping and Teams integration
/// Replaces the enum-based AgentType with a database table for better management
/// </summary>
public class AgentTypeEntity
{
    /// <summary>
    /// Primary key for the agent type
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Agent type name (SBOAgentAppv1, Sales, Admin)
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Friendly display name for the agent type
    /// </summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent share URL to be sent to users in invitation emails
    /// </summary>
    [StringLength(500)]
    public string? AgentShareUrl { get; set; }
    
    /// <summary>
    /// Global Azure AD Security Group ID shared across all organizations
    /// Users assigned to this agent type will be added to this security group
    /// </summary>
    [StringLength(100)]
    public string? GlobalSecurityGroupId { get; set; }
    
    /// <summary>
    /// Microsoft Teams App ID to be installed in Teams when this agent type is assigned
    /// This app will be automatically installed to any Team created for organizations using this agent type
    /// </summary>
    [StringLength(100)]
    public string? TeamsAppId { get; set; }
    
    /// <summary>
    /// Description of what this agent type does
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this agent type is active and available for assignment
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Display order for UI presentation
    /// </summary>
    public int DisplayOrder { get; set; } = 0;
    
    /// <summary>
    /// Whether this agent type requires a supervisor email to be assigned to users
    /// When true, users cannot be assigned this agent type without a valid supervisor email
    /// </summary>
    public bool RequireSupervisorEmail { get; set; } = false;
    
    /// <summary>
    /// When this agent type was created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this agent type was last modified
    /// </summary>
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    
    // Navigation property removed due to database permission constraints
    // Use manual queries to get related OrganizationTeamsGroups if needed
}

/// <summary>
/// Extension methods for agent type management
/// </summary>
public static class AgentTypeExtensions
{
    /// <summary>
    /// Converts legacy enum to new AgentType model lookup
    /// </summary>
    public static string ToAgentTypeName(this LegacyAgentType legacyType)
    {
        return legacyType switch
        {
            LegacyAgentType.SBOAgentAppv1 => "SBOAgentAppv1",
            LegacyAgentType.Sales => "Sales",
            LegacyAgentType.Admin => "Admin",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Gets display name for agent type
    /// </summary>
    public static string GetDisplayName(this LegacyAgentType agentType)
    {
        return agentType switch
        {
            LegacyAgentType.SBOAgentAppv1 => "SBO Agent App v1",
            LegacyAgentType.Sales => "Sales Agent",
            LegacyAgentType.Admin => "Admin Agent",
            _ => "Unknown Agent"
        };
    }
}