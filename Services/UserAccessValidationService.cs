using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

public class UserAccessValidationService : IUserAccessValidationService
{
    private readonly AdminConsoleDbContext _context;
    private readonly ILogger<UserAccessValidationService> _logger;

    public UserAccessValidationService(AdminConsoleDbContext context, ILogger<UserAccessValidationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserAccessResult> ValidateUserAccessAsync(string userId, string email)
    {
        try
        {
            _logger.LogInformation("Validating access for user {UserId} with email {Email}", userId, email);

            // First check if user is a super admin via database role - they always have access
            var superAdminCheck = await CheckIfSuperAdminAsync(email);
            if (superAdminCheck)
            {
                _logger.LogInformation("User {UserId} is super admin (database role) - access granted", userId);
                return new UserAccessResult 
                { 
                    HasAccess = true, 
                    Reason = "Super admin access (database role)" 
                };
            }

            // Look up user in database by email (more reliable than userId for B2B users)
            OnboardedUser? user = null;
            if (!string.IsNullOrEmpty(email))
            {
                user = await _context.OnboardedUsers
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            }

            // If not found by email, try by user ID (fallback)
            if (user == null && !string.IsNullOrEmpty(userId))
            {
                // For B2B users, the user ID might be stored differently
                user = await _context.OnboardedUsers
                    .FirstOrDefaultAsync(u => u.OnboardedUserId.ToString() == userId);
            }

            if (user == null)
            {
                _logger.LogWarning("User not found in database - {UserId}, {Email}", userId, email);
                return new UserAccessResult 
                { 
                    HasAccess = false, 
                    Reason = "User not found in system" 
                };
            }

            // Check if user is deleted
            if (user.IsDeleted)
            {
                _logger.LogWarning("User {UserId} ({Email}) access denied - user is deleted", userId, email);
                return new UserAccessResult 
                { 
                    HasAccess = false, 
                    Reason = "User account has been deleted",
                    User = user
                };
            }

            // Check if user is active
            if (!user.IsActive)
            {
                _logger.LogWarning("User {UserId} ({Email}) access denied - user is not active", userId, email);
                return new UserAccessResult 
                { 
                    HasAccess = false, 
                    Reason = "User account is not active",
                    User = user
                };
            }

            // Check status code
            if (user.StatusCode != StatusCode.Active)
            {
                _logger.LogWarning("User {UserId} ({Email}) access denied - status is {Status}", userId, email, user.StatusCode);
                return new UserAccessResult 
                { 
                    HasAccess = false, 
                    Reason = $"User account status is {user.StatusCode}",
                    User = user
                };
            }

            // Check if organization is active
            if (user.Organization != null && !user.Organization.IsActive)
            {
                _logger.LogWarning("User {UserId} ({Email}) access denied - organization {OrgId} is not active", 
                    userId, email, user.Organization.OrganizationId);
                return new UserAccessResult 
                { 
                    HasAccess = false, 
                    Reason = "Organization is not active",
                    User = user,
                    Organization = user.Organization
                };
            }

            _logger.LogInformation("User {UserId} ({Email}) access granted - all checks passed", userId, email);
            return new UserAccessResult 
            { 
                HasAccess = true, 
                Reason = "Access granted",
                User = user,
                Organization = user.Organization
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for {UserId} ({Email})", userId, email);
            return new UserAccessResult 
            { 
                HasAccess = false, 
                Reason = "System error during access validation" 
            };
        }
    }

    public async Task<bool> RevokeUserAccessAsync(string userId, string revokedBy)
    {
        try
        {
            _logger.LogInformation("Revoking access for user {UserId} by {RevokedBy}", userId, revokedBy);

            // Find user by ID or email
            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId.ToString() == userId || u.Email == userId);

            if (user == null)
            {
                _logger.LogWarning("Cannot revoke access - user {UserId} not found", userId);
                return false;
            }

            // Update user status to revoke access
            user.IsActive = false;
            user.StatusCode = StatusCode.Inactive;
            user.ModifiedOn = DateTime.UtcNow;
            
            // SECURITY CRITICAL: Clear ALL agent type assignments when user is disabled/revoked
            // This ensures the database reflects that the user has no agent access
            _logger.LogInformation("Clearing all agent type assignments for revoked user {UserId} ({Email})", userId, user.Email);
            user.AgentTypeIds = new List<Guid>(); // Clear new agent type IDs
            user.AgentTypes = new List<LegacyAgentType>(); // Clear legacy agent types
            
            // Note: We don't set IsDeleted=true as that's for actual deletion

            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully revoked access for user {UserId} ({Email})", userId, user.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking access for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> RestoreUserAccessAsync(string userId, string restoredBy)
    {
        try
        {
            _logger.LogInformation("Restoring access for user {UserId} by {RestoredBy}", userId, restoredBy);

            // Find user by ID or email
            var user = await _context.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId.ToString() == userId || u.Email == userId);

            if (user == null)
            {
                _logger.LogWarning("Cannot restore access - user {UserId} not found", userId);
                return false;
            }

            // Update user status to restore access
            user.IsActive = true;
            user.StatusCode = StatusCode.Active;
            user.ModifiedOn = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully restored access for user {UserId} ({Email})", userId, user.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring access for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Check if user has SuperAdmin role via database instead of hardcoded domain check
    /// </summary>
    private async Task<bool> CheckIfSuperAdminAsync(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return false;
        }

        try
        {
            var user = await _context.OnboardedUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

            if (user == null)
            {
                return false;
            }

            // Use the extension method to get the role (handles both new and legacy systems)
            var userRole = user.GetUserRole();
            return userRole == UserRole.SuperAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking SuperAdmin status for user {Email}", email);
            return false;
        }
    }
}