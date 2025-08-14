namespace AdminConsole.Models;

/// <summary>
/// Result of business domain validation for email invitations
/// </summary>
public class BusinessDomainValidationResult
{
    /// <summary>
    /// Whether the email domain is valid for business invitations
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Detailed message about the validation result
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// The domain that was validated
    /// </summary>
    public string? Domain { get; set; }
    
    /// <summary>
    /// The email address that was validated
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Type of validation failure (if any)
    /// </summary>
    public BusinessDomainValidationFailureType? FailureType { get; set; }
}

/// <summary>
/// Types of business domain validation failures
/// </summary>
public enum BusinessDomainValidationFailureType
{
    /// <summary>
    /// Email format is invalid
    /// </summary>
    InvalidEmailFormat,
    
    /// <summary>
    /// Domain is a private consumer domain (gmail, yahoo, etc.)
    /// </summary>
    PrivateDomain,
    
    /// <summary>
    /// Domain appears to be suspicious or temporary
    /// </summary>
    SuspiciousDomain,
    
    /// <summary>
    /// Domain is missing or empty
    /// </summary>
    MissingDomain
}