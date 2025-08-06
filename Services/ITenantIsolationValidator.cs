using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Advanced tenant isolation validator to ensure strict data separation
/// between organizations at all service layers
/// </summary>
public interface ITenantIsolationValidator
{
    /// <summary>
    /// Validates that the current user can access the specified organization
    /// Throws UnauthorizedAccessException if access is denied
    /// </summary>
    Task ValidateOrganizationAccessAsync(string organizationId, string operation = "access");

    /// <summary>
    /// Validates that the current user can access the specified resource
    /// </summary>
    Task ValidateResourceAccessAsync<T>(T resource, Func<T, string> getOrganizationId, string operation = "access");

    /// <summary>
    /// Validates that all resources in a collection belong to the current user's organization
    /// </summary>
    Task ValidateResourceCollectionAccessAsync<T>(IEnumerable<T> resources, Func<T, string> getOrganizationId, string operation = "access");

    /// <summary>
    /// Validates cross-organization operations (for super admins only)
    /// </summary>
    Task ValidateCrossOrganizationOperationAsync(string sourceOrgId, string targetOrgId, string operation);

    /// <summary>
    /// Validates user access to another user within the same organization
    /// </summary>
    Task ValidateUserAccessAsync(string targetUserId, string operation = "access");

    /// <summary>
    /// Ensures organization-scoped data queries
    /// </summary>
    Task<string> GetValidatedOrganizationIdAsync();

    /// <summary>
    /// Validates secret access with additional security checks
    /// </summary>
    Task ValidateSecretAccessAsync(string secretName, string organizationId, string operation = "read");

    /// <summary>
    /// Validates bulk operations to ensure they don't cross organization boundaries
    /// </summary>
    Task ValidateBulkOperationAsync<T>(IEnumerable<T> resources, Func<T, string> getOrganizationId, string operation);
}

public class TenantIsolationValidationException : UnauthorizedAccessException
{
    public string OrganizationId { get; }
    public string Operation { get; }
    public string ResourceType { get; }

    public TenantIsolationValidationException(string message, string organizationId, string operation, string resourceType = "resource") 
        : base(message)
    {
        OrganizationId = organizationId;
        Operation = operation;
        ResourceType = resourceType;
    }
}