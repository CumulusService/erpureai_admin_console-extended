namespace AdminConsole.Models;

public enum UserRole
{
    SuperAdmin = 0,  // Owner - manages all admins and organizations
    OrgAdmin = 1,    // Organization admin - manages users in their org
    User = 2,        // Regular user - read-only access
    Developer = 3    // Developer - manages system configuration and agent types
}

public static class UserRoleExtensions
{
    public static string GetDisplayName(this UserRole role)
    {
        return role switch
        {
            UserRole.SuperAdmin => "Super Admin",
            UserRole.OrgAdmin => "Organization Admin",
            UserRole.User => "User",
            UserRole.Developer => "Developer",
            _ => "Unknown"
        };
    }

    public static string GetBadgeClass(this UserRole role)
    {
        return role switch
        {
            UserRole.SuperAdmin => "bg-danger",
            UserRole.OrgAdmin => "bg-warning",
            UserRole.User => "bg-info",
            UserRole.Developer => "bg-success",
            _ => "bg-secondary"
        };
    }

    public static string GetIcon(this UserRole role)
    {
        return role switch
        {
            UserRole.SuperAdmin => "fas fa-crown",
            UserRole.OrgAdmin => "fas fa-user-shield",
            UserRole.User => "fas fa-user",
            UserRole.Developer => "fas fa-code",
            _ => "fas fa-question"
        };
    }
}