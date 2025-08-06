using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdminConsole.Models;

/// <summary>
/// Tracks user assignments to agent-based security groups
/// This is a pure addition to existing functionality - doesn't replace existing group management
/// </summary>
public class UserAgentTypeGroupAssignment
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Azure AD User ID
    /// </summary>
    [Required]
    [StringLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent type that this assignment is for
    /// </summary>
    [Required]
    public Guid AgentTypeId { get; set; }
    
    /// <summary>
    /// Azure AD Security Group ID from AgentType.GlobalSecurityGroupId
    /// </summary>
    [Required]
    [StringLength(100)]
    public string SecurityGroupId { get; set; } = string.Empty;
    
    /// <summary>
    /// Organization this assignment belongs to (for isolation)
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// When this assignment was made
    /// </summary>
    public DateTime AssignedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who made this assignment (Admin user ID)
    /// </summary>
    [StringLength(100)]
    public string? AssignedBy { get; set; }
    
    /// <summary>
    /// Whether this assignment is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When this record was created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this record was last modified
    /// </summary>
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Navigation property to Agent Type
    /// </summary>
    [ForeignKey(nameof(AgentTypeId))]
    public virtual AgentTypeEntity? AgentType { get; set; }
    
    /// <summary>
    /// Navigation property to Organization
    /// </summary>
    [ForeignKey(nameof(OrganizationId))]
    public virtual Organization? Organization { get; set; }
}

/// <summary>
/// Extension methods for UserAgentTypeGroupAssignment
/// </summary>
public static class UserAgentTypeGroupAssignmentExtensions
{
    /// <summary>
    /// Creates a new assignment record
    /// </summary>
    public static UserAgentTypeGroupAssignment CreateAssignment(
        string userId, 
        Guid agentTypeId, 
        string securityGroupId, 
        Guid organizationId, 
        string assignedBy)
    {
        return new UserAgentTypeGroupAssignment
        {
            UserId = userId,
            AgentTypeId = agentTypeId,
            SecurityGroupId = securityGroupId,
            OrganizationId = organizationId,
            AssignedBy = assignedBy,
            AssignedDate = DateTime.UtcNow,
            IsActive = true
        };
    }
    
    /// <summary>
    /// Deactivates an assignment (for soft delete)
    /// </summary>
    public static void Deactivate(this UserAgentTypeGroupAssignment assignment)
    {
        assignment.IsActive = false;
        assignment.ModifiedDate = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Reactivates an assignment (for user restoration)
    /// </summary>
    public static void Reactivate(this UserAgentTypeGroupAssignment assignment)
    {
        assignment.IsActive = true;
        assignment.ModifiedDate = DateTime.UtcNow;
    }
}