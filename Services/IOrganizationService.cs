using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IOrganizationService
{
    Task<Organization?> GetOrganizationByUserAsync(string userId);
    Task<Organization?> GetByIdAsync(string organizationId);
    Task<Organization> CreateOrganizationAsync(string name, string domain, string adminUserId);
    Task<IEnumerable<Organization>> GetAllOrganizationsAsync();
    Task<bool> UpdateOrganizationAsync(Organization organization);
}