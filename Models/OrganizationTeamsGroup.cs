using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdminConsole.Models;

/// <summary>
/// Represents organization-specific MS Teams groups for agent types
/// Each organization gets their own Teams workspace per agent type (when MSTeams = true)
/// </summary>
public class OrganizationTeamsGroup
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Organization this Teams group belongs to
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// Agent type this Teams group is for
    /// </summary>
    [Required]
    public Guid AgentTypeId { get; set; }
    
    /// <summary>
    /// Microsoft 365 Teams Group ID
    /// </summary>
    [Required]
    [StringLength(100)]
    public string TeamsGroupId { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name of the Teams group (e.g., "Contoso-SBO-Team")
    /// </summary>
    [Required]
    [StringLength(255)]
    public string TeamName { get; set; } = string.Empty;
    
    /// <summary>
    /// Direct URL to access the Teams group
    /// </summary>
    [StringLength(500)]
    public string? TeamUrl { get; set; }
    
    /// <summary>
    /// Teams group description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this Teams group is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When this Teams group was created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who created this Teams group (Azure AD User ID)
    /// </summary>
    [StringLength(100)]
    public string? CreatedBy { get; set; }
    
    /// <summary>
    /// When this Teams group was last modified
    /// </summary>
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Navigation property to Organization
    /// </summary>
    [ForeignKey(nameof(OrganizationId))]
    public virtual Organization? Organization { get; set; }
    
    /// <summary>
    /// Navigation property to AgentTypeEntity
    /// </summary>
    [ForeignKey(nameof(AgentTypeId))]
    public virtual AgentTypeEntity? AgentType { get; set; }
}

/// <summary>
/// Extension methods for OrganizationTeamsGroup
/// </summary>
public static class OrganizationTeamsGroupExtensions
{
    /// <summary>
    /// Generates a standard Teams group name based on organization and agent type
    /// </summary>
    public static string GenerateTeamName(string organizationName, string agentTypeName)
    {
        // Clean organization name for Teams group naming
        var cleanOrgName = organizationName
            .Replace(" ", "")
            .Replace(".", "")
            .Replace("-", "")
            .Replace("_", "");
        
        return $"{cleanOrgName}-{agentTypeName}-Team";
    }
    
    /// <summary>
    /// Generates a description for the Teams group
    /// </summary>
    public static string GenerateDescription(string organizationName, string agentTypeName)
    {
        return $"Teams workspace for {organizationName} users working with {agentTypeName} agents";
    }
    
    /// <summary>
    /// Gets the Teams deep link URL
    /// </summary>
    public static string GetTeamsUrl(this OrganizationTeamsGroup teamsGroup)
    {
        if (!string.IsNullOrEmpty(teamsGroup.TeamUrl))
        {
            return teamsGroup.TeamUrl;
        }
        
        // Generate default Teams URL if not stored
        return $"https://teams.microsoft.com/l/team/{teamsGroup.TeamsGroupId}";
    }
}