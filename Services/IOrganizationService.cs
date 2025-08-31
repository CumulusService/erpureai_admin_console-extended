using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IOrganizationService
{
    Task<Organization?> GetOrganizationByUserAsync(string userId);
    Task<Organization?> GetByIdAsync(string organizationId);
    Task<Organization> CreateOrganizationAsync(string name, string domain, string adminUserId, string adminEmail, bool allowUserInvitations = true);
    Task<IEnumerable<Organization>> GetAllOrganizationsAsync();
    Task<bool> UpdateOrganizationAsync(Organization organization);
    Task<bool> UpdateOrganizationAgentTypesAsync(string organizationId, List<Guid> agentTypeIds);
    Task<List<Guid>> GetOrganizationAgentTypesAsync(string organizationId);
}