namespace AdminConsole.Models;

/// <summary>
/// Legacy GuestUser class for backward compatibility
/// Now wraps the new OnboardedUser model that matches Dataverse schema
/// </summary>
public class GuestUser
{
    private readonly OnboardedUser _onboardedUser;
    private string? _overrideInvitationStatus; // Field to override invitation status with real Azure AD status
    private bool? _entraIdEnabled; // Field to store real Entra ID enabled status (separate from database IsActive)
    private string? _userType; // Field to store actual user type from Azure AD
    
    public GuestUser() 
    {
        _onboardedUser = new OnboardedUser();
    }
    
    public GuestUser(OnboardedUser onboardedUser)
    {
        _onboardedUser = onboardedUser ?? new OnboardedUser();
    }
    
    // Legacy properties mapped to OnboardedUser
    public string Id 
    { 
        get => _onboardedUser.OnboardedUserId.ToString(); 
        set => _onboardedUser.OnboardedUserId = Guid.TryParse(value, out var guid) ? guid : Guid.NewGuid(); 
    }
    
    public string Email 
    { 
        get => _onboardedUser.Email; 
        set => _onboardedUser.Email = value; 
    }
    
    public string DisplayName 
    { 
        get => _onboardedUser.Name; 
        set => _onboardedUser.Name = value; 
    }
    
    public string UserPrincipalName 
    { 
        get => _onboardedUser.GetUserPrincipalName(); 
        set { /* UPN is computed, not set directly */ }
    }
    
    public string OrganizationId 
    { 
        get => _onboardedUser.OrganizationLookupId?.ToString() ?? string.Empty; 
        set => _onboardedUser.OrganizationLookupId = Guid.TryParse(value, out var guid) ? guid : null; 
    }
    
    public string OrganizationName 
    { 
        get => _onboardedUser.Organization?.Name ?? string.Empty; 
        set { /* OrganizationName comes from linked Organization */ }
    }
    
    public UserRole Role 
    { 
        get => _onboardedUser.GetUserRole(); 
        set { /* Role is computed from AgentTypes */ }
    }
    
    public DateTime InvitedDateTime 
    { 
        get => _onboardedUser.CreatedOn; 
        set => _onboardedUser.CreatedOn = value; 
    }
    
    public string InvitationStatus 
    { 
        get => _overrideInvitationStatus ?? _onboardedUser.GetInvitationStatus(); 
        set => _overrideInvitationStatus = value;
    }
    
    // Legacy property for backward compatibility
    public bool IsAdmin => Role == UserRole.OrgAdmin || Role == UserRole.SuperAdmin;
    
    // Additional properties needed for SystemUserManagementService
    public string UserType 
    { 
        get => _userType ?? "Guest"; // Default to Guest for external users
        set => _userType = value;
    }
    public bool IsEnabled 
    { 
        get => _entraIdEnabled ?? (_onboardedUser.IsActive && _onboardedUser.StateCode == StateCode.Active); 
        set 
        {
            // If setting from Graph API (Entra ID), store in _entraIdEnabled
            // If setting from database operations, update _onboardedUser.IsActive
            _entraIdEnabled = value;
        } 
    }
    public DateTime CreatedOn 
    { 
        get => _onboardedUser.CreatedOn; 
        set => _onboardedUser.CreatedOn = value; 
    }
    
    // Access to underlying OnboardedUser for full functionality
    public OnboardedUser OnboardedUser => _onboardedUser;
    
    // Implicit conversion operators for seamless integration
    public static implicit operator OnboardedUser(GuestUser guestUser) => guestUser._onboardedUser;
    public static implicit operator GuestUser(OnboardedUser onboardedUser) => new GuestUser(onboardedUser);
}