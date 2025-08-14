using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IUserAccessValidationService
{
    Task<UserAccessResult> ValidateUserAccessAsync(string userId, string email);
    Task<bool> RevokeUserAccessAsync(string userId, string revokedBy);
    Task<bool> RestoreUserAccessAsync(string userId, string restoredBy);
}

public class UserAccessResult
{
    public bool HasAccess { get; set; }
    public string Reason { get; set; } = string.Empty;
    public OnboardedUser? User { get; set; }
    public Organization? Organization { get; set; }
}