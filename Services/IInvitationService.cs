using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for handling secure, domain-based user invitations
/// Ensures organization admins can only invite users from their own domain
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Validates if an organization admin can invite a user with the given email
    /// </summary>
    /// <param name="organizationId">The organization ID of the admin</param>
    /// <param name="emailToInvite">Email address to validate</param>
    /// <returns>True if invitation is allowed</returns>
    Task<bool> CanInviteEmailAsync(Guid organizationId, string emailToInvite);
    
    /// <summary>
    /// Sends a B2B invitation to a user for a specific organization
    /// </summary>
    /// <param name="organizationId">Target organization</param>
    /// <param name="emailToInvite">Email to invite</param>
    /// <param name="invitedBy">ID of the user sending the invitation</param>
    /// <param name="agentTypes">Agent types to assign to the user (legacy)</param>
    /// <returns>The invitation result</returns>
    Task<InvitationResult> InviteUserAsync(Guid organizationId, string emailToInvite, Guid invitedBy, List<LegacyAgentType> agentTypes);
    
    /// <summary>
    /// Enhanced invitation method that supports both legacy and new agent-based group assignment
    /// </summary>
    /// <param name="organizationId">Target organization</param>
    /// <param name="emailToInvite">Email to invite</param>
    /// <param name="invitedBy">ID of the user sending the invitation</param>
    /// <param name="agentTypes">Legacy agent types for backward compatibility</param>
    /// <param name="agentTypeIds">New database-driven agent type IDs for enhanced group assignment</param>
    /// <param name="selectedDatabaseIds">Database credential IDs to assign to the user</param>
    /// <param name="assignedRole">User role to assign (OrgAdmin, User, etc.)</param>
    /// <param name="currentUserEmail">Current user's email for self-invitation prevention</param>
    /// <returns>The invitation result</returns>
    Task<InvitationResult> InviteUserAsync(Guid organizationId, string emailToInvite, Guid invitedBy, List<LegacyAgentType> agentTypes, List<Guid> agentTypeIds, List<Guid> selectedDatabaseIds, UserRole assignedRole = UserRole.User, string? currentUserEmail = null);
    
    /// <summary>
    /// Gets all pending invitations for an organization
    /// </summary>
    Task<List<InvitationRecord>> GetPendingInvitationsAsync(Guid organizationId);
    
    /// <summary>
    /// Resends an invitation to a user
    /// </summary>
    Task<InvitationResult> ResendInvitationAsync(Guid invitationId);
    
    /// <summary>
    /// Cancels a pending invitation
    /// </summary>
    Task<bool> CancelInvitationAsync(Guid invitationId);
}

/// <summary>
/// Result of an invitation operation
/// </summary>
public class InvitationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string InvitationId { get; set; } = string.Empty;
    public string InvitationUrl { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Represents an invitation record for tracking
/// </summary>
public class InvitationRecord
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public Guid InvitedBy { get; set; }
    public string InvitedByEmail { get; set; } = string.Empty;
    public DateTime InvitedDate { get; set; }
    public string Status { get; set; } = string.Empty; // Pending, Accepted, Expired, Cancelled
    public DateTime? AcceptedDate { get; set; }
    public List<LegacyAgentType> AgentTypes { get; set; } = new();
}