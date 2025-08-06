using AdminConsole.Models;
using System.Security.Claims;

namespace AdminConsole.Services;

/// <summary>
/// Advanced tenant isolation validator implementation
/// Provides strict validation of cross-organization access attempts
/// </summary>
public class TenantIsolationValidator : ITenantIsolationValidator
{
    private readonly IDataIsolationService _dataIsolationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantIsolationValidator> _logger;

    public TenantIsolationValidator(
        IDataIsolationService dataIsolationService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<TenantIsolationValidator> logger)
    {
        _dataIsolationService = dataIsolationService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task ValidateOrganizationAccessAsync(string organizationId, string operation = "access")
    {
        if (string.IsNullOrEmpty(organizationId))
        {
            throw new TenantIsolationValidationException(
                "Organization ID cannot be null or empty", 
                organizationId ?? "null", 
                operation, 
                "organization");
        }

        var currentUser = GetCurrentUserInfo();
        
        // Super admins can access all organizations for management operations
        if (_dataIsolationService.IsCurrentUserSuperAdmin())
        {
            _logger.LogInformation("Super admin {Email} accessing organization {OrganizationId} for operation {Operation}", 
                currentUser.Email, organizationId, operation);
            return;
        }

        // Validate that user belongs to the organization
        var hasAccess = await _dataIsolationService.ValidateOrganizationAccessAsync(organizationId);
        
        if (!hasAccess)
        {
            var userOrgId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            
            _logger.LogWarning("SECURITY VIOLATION: User {Email} from organization {UserOrgId} attempted {Operation} on organization {TargetOrgId}", 
                currentUser.Email, userOrgId, operation, organizationId);
                
            throw new TenantIsolationValidationException(
                $"Access denied. You cannot {operation} resources from organization {organizationId}",
                organizationId,
                operation,
                "organization");
        }

        _logger.LogDebug("Organization access validated for user {Email} to organization {OrganizationId} for operation {Operation}",
            currentUser.Email, organizationId, operation);
    }

    public async Task ValidateResourceAccessAsync<T>(T resource, Func<T, string> getOrganizationId, string operation = "access")
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        var resourceOrgId = getOrganizationId(resource);
        await ValidateOrganizationAccessAsync(resourceOrgId, operation);
    }

    public async Task ValidateResourceCollectionAccessAsync<T>(IEnumerable<T> resources, Func<T, string> getOrganizationId, string operation = "access")
    {
        if (resources == null)
        {
            return;
        }

        var currentUserOrgId = await GetValidatedOrganizationIdAsync();
        var isSuperAdmin = _dataIsolationService.IsCurrentUserSuperAdmin();

        foreach (var resource in resources)
        {
            var resourceOrgId = getOrganizationId(resource);
            
            if (!isSuperAdmin && !string.Equals(resourceOrgId, currentUserOrgId, StringComparison.OrdinalIgnoreCase))
            {
                var currentUser = GetCurrentUserInfo();
                
                _logger.LogWarning("SECURITY VIOLATION: User {Email} attempted {Operation} on resource from organization {ResourceOrgId} while belonging to {UserOrgId}", 
                    currentUser.Email, operation, resourceOrgId, currentUserOrgId);
                    
                throw new TenantIsolationValidationException(
                    $"Access denied. Collection contains resources from organization {resourceOrgId} which you cannot access",
                    resourceOrgId,
                    operation,
                    typeof(T).Name);
            }
        }
    }

    public Task ValidateCrossOrganizationOperationAsync(string sourceOrgId, string targetOrgId, string operation)
    {
        // Only super admins can perform cross-organization operations
        if (!_dataIsolationService.IsCurrentUserSuperAdmin())
        {
            var currentUser = GetCurrentUserInfo();
            
            _logger.LogWarning("SECURITY VIOLATION: Non-super admin {Email} attempted cross-organization operation {Operation} from {SourceOrgId} to {TargetOrgId}", 
                currentUser.Email, operation, sourceOrgId, targetOrgId);
                
            throw new TenantIsolationValidationException(
                $"Cross-organization operations require Super Admin privileges",
                targetOrgId,
                operation,
                "cross-organization");
        }

        _logger.LogInformation("Super admin cross-organization operation validated: {Operation} from {SourceOrgId} to {TargetOrgId}", 
            operation, sourceOrgId, targetOrgId);
        return Task.CompletedTask;
    }

    public async Task ValidateUserAccessAsync(string targetUserId, string operation = "access")
    {
        if (string.IsNullOrEmpty(targetUserId))
        {
            throw new ArgumentException("Target user ID cannot be null or empty", nameof(targetUserId));
        }

        // Super admins can access all users
        if (_dataIsolationService.IsCurrentUserSuperAdmin())
        {
            return;
        }

        // TODO: Implement user-to-user access validation
        // This would typically involve checking if both users belong to the same organization
        // For now, we'll just validate that the current user has proper organization access
        
        var currentUserOrgId = await GetValidatedOrganizationIdAsync();
        _logger.LogDebug("User access validation for target user {TargetUserId} by organization {OrganizationId}", 
            targetUserId, currentUserOrgId);
    }

    public async Task<string> GetValidatedOrganizationIdAsync()
    {
        var organizationId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
        
        if (string.IsNullOrEmpty(organizationId))
        {
            var currentUser = GetCurrentUserInfo();
            
            _logger.LogError("Unable to determine organization for authenticated user {Email}", currentUser.Email);
            
            throw new TenantIsolationValidationException(
                "Unable to determine your organization. Please contact support.",
                "unknown",
                "access",
                "organization");
        }

        return organizationId;
    }

    public async Task ValidateSecretAccessAsync(string secretName, string organizationId, string operation = "read")
    {
        // Additional security check for secret access
        var currentUserRole = _dataIsolationService.GetCurrentUserRole();
        
        // Super admins are allowed access for database credential management operations
        if (currentUserRole == UserRole.SuperAdmin)
        {
            // Allow database credential-related secret operations for Super Admins
            if (secretName.StartsWith("sap-password", StringComparison.OrdinalIgnoreCase) || 
                secretName.Contains("database") || 
                operation.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                var currentUser = GetCurrentUserInfo();
                _logger.LogInformation("Super admin {Email} accessing database credential secret {SecretName} in organization {OrganizationId} for operation {Operation}", 
                    currentUser.Email, secretName, organizationId, operation);
            }
            else
            {
                // Block other secret access for compliance
                var currentUser = GetCurrentUserInfo();
                
                _logger.LogWarning("SECURITY VIOLATION: Super admin {Email} attempted {Operation} access to non-database secret {SecretName} in organization {OrganizationId}", 
                    currentUser.Email, operation, secretName, organizationId);
                    
                throw new TenantIsolationValidationException(
                    "Super Admins can only access database credential secrets",
                    organizationId,
                    operation,
                    "secret");
            }
        }
        
        // Organization Admins can access secrets in their own organization
        if (currentUserRole == UserRole.OrgAdmin)
        {
            _logger.LogDebug("Organization Admin accessing secret {SecretName} in organization {OrganizationId} for operation {Operation}", 
                secretName, organizationId, operation);
        }

        // Regular organization access validation
        await ValidateOrganizationAccessAsync(organizationId, operation);

        // Additional validation for sensitive operations
        if (operation.Equals("delete", StringComparison.OrdinalIgnoreCase) && currentUserRole != UserRole.OrgAdmin)
        {
            throw new TenantIsolationValidationException(
                "Secret deletion requires Organization Admin privileges",
                organizationId,
                operation,
                "secret");
        }

        _logger.LogDebug("Secret access validated for user role {UserRole} to secret {SecretName} in organization {OrganizationId}",
            currentUserRole, secretName, organizationId);
    }

    public async Task ValidateBulkOperationAsync<T>(IEnumerable<T> resources, Func<T, string> getOrganizationId, string operation)
    {
        if (resources == null || !resources.Any())
        {
            return;
        }

        var currentUserOrgId = await GetValidatedOrganizationIdAsync();
        var isSuperAdmin = _dataIsolationService.IsCurrentUserSuperAdmin();

        // Check that all resources belong to the same organization
        var organizationIds = resources.Select(getOrganizationId).Distinct().ToList();
        
        if (organizationIds.Count > 1)
        {
            var currentUser = GetCurrentUserInfo();
            
            _logger.LogWarning("SECURITY VIOLATION: User {Email} attempted bulk {Operation} across multiple organizations: {Organizations}", 
                currentUser.Email, operation, string.Join(", ", organizationIds));
                
            throw new TenantIsolationValidationException(
                "Bulk operations cannot span multiple organizations",
                string.Join(", ", organizationIds),
                operation,
                "bulk-operation");
        }

        // Validate access to the single organization
        var targetOrgId = organizationIds.First();
        
        if (!isSuperAdmin && !string.Equals(targetOrgId, currentUserOrgId, StringComparison.OrdinalIgnoreCase))
        {
            var currentUser = GetCurrentUserInfo();
            
            _logger.LogWarning("SECURITY VIOLATION: User {Email} from organization {UserOrgId} attempted bulk {Operation} on organization {TargetOrgId}", 
                currentUser.Email, currentUserOrgId, operation, targetOrgId);
                
            throw new TenantIsolationValidationException(
                $"Access denied. Bulk operation targets organization {targetOrgId} which you cannot access",
                targetOrgId,
                operation,
                "bulk-operation");
        }

        _logger.LogInformation("Bulk operation validated: {Operation} on {Count} resources in organization {OrganizationId}",
            operation, resources.Count(), targetOrgId);
    }

    private (string Email, string UserId) GetCurrentUserInfo()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var email = user?.FindFirst(ClaimTypes.Email)?.Value ?? 
                   user?.FindFirst("preferred_username")?.Value ?? 
                   "unknown";
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        
        return (email, userId);
    }
}