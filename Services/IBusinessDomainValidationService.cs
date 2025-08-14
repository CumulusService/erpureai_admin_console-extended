using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for validating business email domains to prevent private email invitations
/// Ensures only verified business domains can be invited to the organization
/// </summary>
public interface IBusinessDomainValidationService
{
    /// <summary>
    /// Validates that an email address uses a business domain (not private domains like gmail.com)
    /// </summary>
    /// <param name="email">Email address to validate</param>
    /// <returns>Validation result with success status and detailed message</returns>
    BusinessDomainValidationResult ValidateBusinessDomain(string email);
    
    /// <summary>
    /// Checks if a domain is considered a private/consumer domain
    /// </summary>
    /// <param name="domain">Domain to check (e.g., "gmail.com")</param>
    /// <returns>True if the domain is a private consumer domain</returns>
    bool IsPrivateDomain(string domain);
    
    /// <summary>
    /// Gets the domain from an email address
    /// </summary>
    /// <param name="email">Email address</param>
    /// <returns>Domain portion of the email (e.g., "company.com")</returns>
    string? ExtractDomain(string email);
    
    /// <summary>
    /// Gets a list of all blocked private domains
    /// </summary>
    /// <returns>List of private domains that are blocked</returns>
    IReadOnlyList<string> GetBlockedPrivateDomains();
}