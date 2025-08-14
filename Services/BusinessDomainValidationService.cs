using AdminConsole.Models;
using System.Text.RegularExpressions;

namespace AdminConsole.Services;

/// <summary>
/// Service for validating business email domains to prevent private email invitations
/// Ensures only verified business domains can be invited to the organization
/// </summary>
public class BusinessDomainValidationService : IBusinessDomainValidationService
{
    private readonly ILogger<BusinessDomainValidationService> _logger;
    
    /// <summary>
    /// Comprehensive list of private/consumer email domains that should be blocked
    /// </summary>
    private static readonly HashSet<string> PrivateDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Major consumer email providers
        "gmail.com", "googlemail.com",
        "yahoo.com", "yahoo.co.uk", "yahoo.ca", "yahoo.com.au", "yahoo.fr", "yahoo.de", "yahoo.es", "yahoo.it",
        "ymail.com", "rocketmail.com",
        "outlook.com", "hotmail.com", "live.com", "msn.com",
        "aol.com", "aim.com",
        "icloud.com", "me.com", "mac.com",
        
        // Other popular consumer providers
        "protonmail.com", "proton.me",
        "tutanota.com", "tuta.io",
        "zoho.com", "zohomail.com",
        "mail.com",
        "gmx.com", "gmx.de", "gmx.net",
        "web.de",
        "yandex.com", "yandex.ru",
        "mail.ru", "list.ru", "bk.ru", "inbox.ru",
        
        // Temporary and disposable email providers
        "10minutemail.com", "tempmail.org", "guerrillamail.com",
        "mailinator.com", "throwaway.email", "temp-mail.org",
        "sharklasers.com", "guerrillamailblock.com",
        
        // Other consumer domains
        "rediffmail.com", "gmail.co.in",
        "qq.com", "163.com", "126.com",
        "naver.com", "daum.net",
        "fastmail.com", "fastmail.fm",
        "hushmail.com",
        
        // Social media email addresses  
        "facebook.com", "twitter.com", "linkedin.com"
    };

    /// <summary>
    /// Patterns that indicate suspicious or temporary domains
    /// </summary>
    private static readonly Regex[] SuspiciousDomainPatterns = 
    {
        new Regex(@"^\d+[a-z]*\.com$", RegexOptions.IgnoreCase), // Numbers + letters + .com
        new Regex(@"^[a-z]{1,3}\d+\.com$", RegexOptions.IgnoreCase), // Few letters + numbers + .com  
        new Regex(@"temp.*mail", RegexOptions.IgnoreCase), // Contains "temp" and "mail"
        new Regex(@"disposable.*mail", RegexOptions.IgnoreCase), // Contains "disposable" and "mail"
        new Regex(@"\d{8,}\.com$", RegexOptions.IgnoreCase), // Long numeric domains
    };

    public BusinessDomainValidationService(ILogger<BusinessDomainValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates that an email address uses a business domain (not private domains like gmail.com)
    /// </summary>
    public BusinessDomainValidationResult ValidateBusinessDomain(string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new BusinessDomainValidationResult
                {
                    IsValid = false,
                    Message = "Email address is required",
                    Email = email,
                    FailureType = BusinessDomainValidationFailureType.InvalidEmailFormat
                };
            }

            // Basic email format validation
            if (!IsValidEmailFormat(email))
            {
                _logger.LogWarning("Invalid email format provided for business domain validation: {Email}", email);
                return new BusinessDomainValidationResult
                {
                    IsValid = false,
                    Message = "Email address format is invalid",
                    Email = email,
                    FailureType = BusinessDomainValidationFailureType.InvalidEmailFormat
                };
            }

            var domain = ExtractDomain(email);
            if (string.IsNullOrWhiteSpace(domain))
            {
                _logger.LogWarning("Could not extract domain from email: {Email}", email);
                return new BusinessDomainValidationResult
                {
                    IsValid = false,
                    Message = "Could not extract domain from email address",
                    Email = email,
                    FailureType = BusinessDomainValidationFailureType.MissingDomain
                };
            }

            // Check if domain is a known private domain
            if (IsPrivateDomain(domain))
            {
                _logger.LogWarning("🚫 BLOCKED PRIVATE DOMAIN: Attempted invitation to private email domain {Domain} for email {Email}", domain, email);
                return new BusinessDomainValidationResult
                {
                    IsValid = false,
                    Message = $"Private email domains like '{domain}' are not allowed. Please use a business email address.",
                    Email = email,
                    Domain = domain,
                    FailureType = BusinessDomainValidationFailureType.PrivateDomain
                };
            }

            // Check for suspicious domain patterns
            if (IsSuspiciousDomain(domain))
            {
                _logger.LogWarning("🚫 BLOCKED SUSPICIOUS DOMAIN: Attempted invitation to suspicious domain {Domain} for email {Email}", domain, email);
                return new BusinessDomainValidationResult
                {
                    IsValid = false,
                    Message = $"The domain '{domain}' appears to be a temporary or disposable email service. Please use a business email address.",
                    Email = email,
                    Domain = domain,
                    FailureType = BusinessDomainValidationFailureType.SuspiciousDomain
                };
            }

            // Validation passed - domain appears to be a business domain
            _logger.LogInformation("✅ BUSINESS DOMAIN VALIDATED: Email {Email} with domain {Domain} passed validation", email, domain);
            return new BusinessDomainValidationResult
            {
                IsValid = true,
                Message = $"Business domain '{domain}' is valid for invitation",
                Email = email,
                Domain = domain
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating business domain for email {Email}", email);
            return new BusinessDomainValidationResult
            {
                IsValid = false,
                Message = "An error occurred while validating the email domain",
                Email = email,
                FailureType = BusinessDomainValidationFailureType.InvalidEmailFormat
            };
        }
    }

    /// <summary>
    /// Checks if a domain is considered a private/consumer domain
    /// </summary>
    public bool IsPrivateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        return PrivateDomains.Contains(domain.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Gets the domain from an email address
    /// </summary>
    public string? ExtractDomain(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var atIndex = email.LastIndexOf('@');
        if (atIndex == -1 || atIndex == email.Length - 1)
            return null;

        return email.Substring(atIndex + 1).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Gets a list of all blocked private domains
    /// </summary>
    public IReadOnlyList<string> GetBlockedPrivateDomains()
    {
        return PrivateDomains.OrderBy(d => d).ToList().AsReadOnly();
    }

    /// <summary>
    /// Basic email format validation using regex
    /// </summary>
    private static bool IsValidEmailFormat(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // Basic email regex - not perfect but catches most invalid formats
            var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
            return emailRegex.IsMatch(email.Trim());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if domain matches suspicious patterns
    /// </summary>
    private static bool IsSuspiciousDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        return SuspiciousDomainPatterns.Any(pattern => pattern.IsMatch(domain));
    }
}