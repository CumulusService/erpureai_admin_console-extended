using System.ComponentModel.DataAnnotations;

namespace AdminConsole.Models;

/// <summary>
/// Represents the assignment of a user to specific database credentials
/// Allows tracking which databases each user can access within their organization
/// </summary>
public class UserDatabaseAssignment
{
    /// <summary>
    /// Unique identifier for this assignment
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// User ID (OnboardedUser)
    /// </summary>
    [Required]
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Database credential ID that the user can access
    /// </summary>
    [Required]
    public Guid DatabaseCredentialId { get; set; }
    
    /// <summary>
    /// Organization ID for multi-tenant isolation
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// When this assignment was created
    /// </summary>
    public DateTime AssignedOn { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who assigned this database access
    /// </summary>
    public string AssignedBy { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this assignment is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public OnboardedUser? User { get; set; }
    public DatabaseCredential? DatabaseCredential { get; set; }
}

/// <summary>
/// Model for creating/updating user database assignments
/// </summary>
public class UserDatabaseAssignmentModel
{
    [Required(ErrorMessage = "User ID is required")]
    public Guid UserId { get; set; }
    
    [Required(ErrorMessage = "Database credential ID is required")]
    public List<Guid> DatabaseCredentialIds { get; set; } = new();
    
    public string Notes { get; set; } = string.Empty;
}