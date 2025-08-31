using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

public class OrganizationService : IOrganizationService
{
    private readonly AdminConsoleDbContext _context;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        AdminConsoleDbContext context,
        ILogger<OrganizationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Organization?> GetOrganizationByUserAsync(string userId)
    {
        try
        {
            // CRITICAL FIX: Super Admins must see their organizations regardless of state (active/inactive)  
            // This ensures Super Admins can manage organizations they created even after deactivation
            _logger.LogInformation("Loading organization for user {UserId} (including inactive ones for Super Admin management)", userId);
            
            var organization = await _context.Organizations
                .Where(o => o.CreatedBy == Guid.Parse(userId))
                .FirstOrDefaultAsync();

            if (organization != null)
            {
                organization.SyncLegacyProperties();
            }

            return organization;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get organization for user {UserId}", userId);
            return null;
        }
    }

    public async Task<Organization?> GetByIdAsync(string organizationId)
    {
        _logger.LogInformation("=== OrganizationService.GetByIdAsync ===");
        _logger.LogInformation("  Input OrganizationId: '{OrganizationId}'", organizationId);
        
        try
        {
            Guid orgGuid;
            if (!Guid.TryParse(organizationId, out orgGuid))
            {
                // Generate deterministic GUID from domain-based ID
                _logger.LogInformation("  Not a valid GUID, generating from domain-based ID");
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(organizationId));
                orgGuid = new Guid(hash);
                _logger.LogInformation("  Generated GUID: {GeneratedGuid}", orgGuid);
            }
            else
            {
                _logger.LogInformation("  Using provided GUID: {ProvidedGuid}", orgGuid);
            }

            _logger.LogInformation("  Querying database for OrganizationId: {OrgGuid}", orgGuid);
            var organization = await _context.Organizations
                .AsNoTracking() // Force fresh database query, bypass EF change tracking
                .Where(o => o.OrganizationId == orgGuid)
                .FirstOrDefaultAsync();

            if (organization != null)
            {
                _logger.LogInformation("  Organization FOUND: Name='{Name}', Domain='{Domain}'", organization.Name, organization.Domain);
                organization.SyncLegacyProperties();
            }
            else
            {
                _logger.LogWarning("  Organization NOT FOUND for GUID: {OrgGuid}", orgGuid);
            }

            return organization;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get organization by ID {OrganizationId}", organizationId);
            return null;
        }
    }

    public async Task<Organization> CreateOrganizationAsync(string name, string domain, string adminUserId, string adminEmail, bool allowUserInvitations = true)
    {
        // Generate deterministic GUID from domain (same logic as GetByIdAsync)
        var domainBasedOrgId = domain.Replace(".", "_").ToLowerInvariant();
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(domainBasedOrgId));
        var organizationId = new Guid(hash);
        
        try
        {
            
            _logger.LogInformation("Creating organization with deterministic GUID {OrganizationId} from domain {Domain} (normalized: {NormalizedDomain})", 
                organizationId, domain, domainBasedOrgId);
            
            // Create a valid admin GUID if parsing fails
            var adminGuid = Guid.TryParse(adminUserId, out var parsedGuid) ? parsedGuid : Guid.NewGuid();
            
            var organization = new Organization
            {
                OrganizationId = organizationId,
                Name = name,
                Domain = domain, // Store the original domain
                AdminEmail = adminEmail,
                DatabaseType = OrganizationDatabaseType.SQL,
                StateCode = StateCode.Active,
                StatusCode = StatusCode.Active,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                CreatedBy = adminGuid,
                ModifiedBy = adminGuid,
                OwnerId = adminGuid, // Set required OwnerId field
                OwningUser = adminGuid, // Set owning user as well
                // Set required configuration properties 
                KeyVaultUri = $"https://{domain.Replace(".", "-")}-vault.vault.azure.net/",
                KeyVaultSecretPrefix = domain.Replace(".", "-"),
                // SAP fields should be null by default - only set when explicitly configured by OrgAdmin
                SAPServiceLayerHostname = null,
                SAPAPIGatewayHostname = null, 
                SAPBusinessOneWebClientHost = null,
                DocumentCode = null,
                // USER INVITATION PERMISSIONS - Set based on SuperAdmin choice during invitation
                AllowUserInvitations = allowUserInvitations,
                // Counters
                UserCount = 0,
                SecretCount = 0,
                // Set required legacy properties for backward compatibility
                Id = organizationId.ToString(),
                AdminUserId = adminGuid.ToString(),
                AdminUserName = $"Admin User",
                AdminUserEmail = adminEmail,
                CreatedDate = DateTime.UtcNow,
                IsActive = true
            };

            // Check if organization already exists to avoid duplicate key error
            var existingOrg = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == organizationId);
                
            if (existingOrg != null)
            {
                _logger.LogInformation("Organization {OrganizationId} already exists for domain {Domain}. Returning existing organization.", organizationId, domain);
                return existingOrg;
            }

            _logger.LogInformation("Adding organization to context: {OrganizationId}", organizationId);
            _context.Organizations.Add(organization);
            
            _logger.LogInformation("Attempting to save organization to database: {OrganizationId}", organizationId);
            await _context.SaveChangesAsync();

            organization.SyncLegacyProperties();
            
            _logger.LogInformation("Created organization {OrganizationName} with ID {OrganizationId}", name, organizationId);
            
            return organization;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database update exception when creating organization {OrganizationName}. Inner exception: {InnerException}", 
                name, dbEx.InnerException?.Message);
            
            // Log additional details about the entity state
            _logger.LogError("Entity validation details - Organization properties:");
            _logger.LogError("OrganizationId: {OrganizationId}, Name: {Name}, Domain: {Domain}, AdminEmail: {AdminEmail}", 
                organizationId, name, domain, $"admin@{domain}");
            
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating organization {OrganizationName}: {ErrorMessage}", name, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<Organization>> GetAllOrganizationsAsync()
    {
        try
        {
            // CRITICAL FIX: Super Admins must see ALL organizations (active and inactive) for management purposes
            // This ensures deactivated organizations remain visible for monitoring and management
            _logger.LogInformation("Loading ALL organizations for Super Admin management (including inactive ones)");
            
            var organizations = await _context.Organizations
                .OrderByDescending(o => o.CreatedOn)
                .ToListAsync();

            foreach (var org in organizations)
            {
                org.SyncLegacyProperties();
            }

            return organizations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all organizations");
            return Enumerable.Empty<Organization>();
        }
    }

    public async Task<bool> UpdateOrganizationAsync(Organization organization)
    {
        try
        {
            var existingOrg = await _context.Organizations
                .Where(o => o.OrganizationId == organization.OrganizationId)
                .FirstOrDefaultAsync();

            if (existingOrg != null)
            {
                existingOrg.Name = organization.Name;
                existingOrg.AdminEmail = organization.AdminEmail;
                existingOrg.DatabaseType = organization.DatabaseType;
                existingOrg.KeyVaultUri = organization.KeyVaultUri;
                existingOrg.KeyVaultSecretPrefix = organization.KeyVaultSecretPrefix;
                existingOrg.SAPServiceLayerHostname = organization.SAPServiceLayerHostname;
                existingOrg.SAPAPIGatewayHostname = organization.SAPAPIGatewayHostname;
                existingOrg.SAPBusinessOneWebClientHost = organization.SAPBusinessOneWebClientHost;
                existingOrg.DocumentCode = organization.DocumentCode;
                existingOrg.OrganizationAgentTypeIds = organization.OrganizationAgentTypeIds;
                existingOrg.M365GroupId = organization.M365GroupId;
                existingOrg.StateCode = organization.StateCode;
                existingOrg.StatusCode = organization.StatusCode;
                existingOrg.AllowUserInvitations = organization.AllowUserInvitations; // CRITICAL FIX: Update user invitation permissions
                existingOrg.ModifiedOn = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Updated organization {OrganizationId}", organization.OrganizationId);
                return true;
            }
            else
            {
                _logger.LogWarning("Organization {OrganizationId} not found for update", organization.OrganizationId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update organization {OrganizationId}", organization.OrganizationId);
            return false;
        }
    }

    public async Task<bool> CreateGuestUserRecordAsync(GuestUser guestUser)
    {
        try
        {
            var onboardedUser = new OnboardedUser
            {
                OnboardedUserId = Guid.NewGuid(),
                Email = guestUser.Email,
                FullName = guestUser.DisplayName,
                Name = guestUser.DisplayName,
                AssignedDatabaseIds = new List<Guid>(),
                AgentTypes = new List<LegacyAgentType> { LegacyAgentType.SBOAgentAppv1 },
                OrganizationLookupId = Guid.TryParse(guestUser.OrganizationId, out var orgGuid) ? orgGuid : null,
                StateCode = StateCode.Active,
                StatusCode = StatusCode.Active,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow
            };

            _context.OnboardedUsers.Add(onboardedUser);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Created guest user record for {Email} in organization {OrganizationId}", 
                guestUser.Email, guestUser.OrganizationId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create guest user record for {Email}", guestUser.Email);
            return false;
        }
    }

    public async Task<bool> UpdateOrganizationAgentTypesAsync(string organizationId, List<Guid> agentTypeIds)
    {
        try
        {
            Guid orgGuid;
            if (!Guid.TryParse(organizationId, out orgGuid))
            {
                // Generate deterministic GUID from domain-based ID
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(organizationId));
                orgGuid = new Guid(hash);
            }

            var existingOrg = await _context.Organizations
                .Where(o => o.OrganizationId == orgGuid)
                .FirstOrDefaultAsync();

            if (existingOrg != null)
            {
                existingOrg.SetOrganizationAgentTypeIds(agentTypeIds);
                existingOrg.ModifiedOn = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Updated organization agent types for {OrganizationId}: {AgentTypeIds}", 
                    organizationId, string.Join(", ", agentTypeIds));
                return true;
            }
            else
            {
                _logger.LogWarning("Organization {OrganizationId} not found for agent type update", organizationId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update organization agent types for {OrganizationId}", organizationId);
            return false;
        }
    }

    public async Task<List<Guid>> GetOrganizationAgentTypesAsync(string organizationId)
    {
        try
        {
            var organization = await GetByIdAsync(organizationId);
            return organization?.GetOrganizationAgentTypeIds() ?? new List<Guid>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get organization agent types for {OrganizationId}", organizationId);
            return new List<Guid>();
        }
    }

    public async Task<IEnumerable<GuestUser>> GetGuestUsersByOrganizationAsync(string organizationId)
    {
        try
        {
            if (!Guid.TryParse(organizationId, out var orgGuid))
            {
                // Generate deterministic GUID from domain-based ID
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(organizationId));
                orgGuid = new Guid(hash);
            }

            var users = await _context.OnboardedUsers
                .Where(u => u.OrganizationLookupId == orgGuid)
                .OrderByDescending(u => u.CreatedOn)
                .ToListAsync();

            return users.Select(ConvertToGuestUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guest users for organization {OrganizationId}", organizationId);
            return Enumerable.Empty<GuestUser>();
        }
    }

    private GuestUser ConvertToGuestUser(OnboardedUser user)
    {
        return new GuestUser
        {
            Id = user.OnboardedUserId.ToString(),
            Email = user.Email,
            DisplayName = user.FullName.IsNullOrEmpty() ? user.Email.Split('@')[0] : user.FullName,
            UserPrincipalName = user.Email,
            OrganizationId = user.OrganizationLookupId?.ToString() ?? string.Empty,
            Role = UserRole.User,
            InvitedDateTime = user.CreatedOn,
            InvitationStatus = user.StateCode == StateCode.Active ? "Accepted" : "PendingAcceptance"
        };
    }
}

// Extension method for string null/empty check
public static class StringExtensions
{
    public static bool IsNullOrEmpty(this string? value)
    {
        return string.IsNullOrEmpty(value);
    }
}