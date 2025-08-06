using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IOrganizationSetupService
{
    Task<OrganizationSetupResult> SetupNewOrganizationAsync(string organizationName, string organizationDomain, string adminEmail, string adminName);
    Task<bool> CreateDefaultSecretsAsync(string organizationId, string organizationName, string organizationDomain);
    Task<bool> CreateDefaultSecretsForOrgAdminAsync(string organizationId, string organizationName, string organizationDomain);
}

public class OrganizationSetupResult
{
    public bool Success { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
    public string SecurityGroupId { get; set; } = string.Empty;
    public List<string> CreatedSecrets { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}