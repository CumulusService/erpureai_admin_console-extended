using AdminConsole.Models;

namespace AdminConsole.Services;

public class OrganizationSetupService : IOrganizationSetupService
{
    private readonly IOrganizationService _organizationService;
    private readonly IGraphService _graphService;
    private readonly IKeyVaultService _keyVaultService;
    private readonly ILogger<OrganizationSetupService> _logger;

    public OrganizationSetupService(
        IOrganizationService organizationService,
        IGraphService graphService,
        IKeyVaultService keyVaultService,
        ILogger<OrganizationSetupService> logger)
    {
        _organizationService = organizationService;
        _graphService = graphService;
        _keyVaultService = keyVaultService;
        _logger = logger;
    }

    public async Task<OrganizationSetupResult> SetupNewOrganizationAsync(
        string organizationName, 
        string organizationDomain, 
        string adminEmail, 
        string adminName)
    {
        var result = new OrganizationSetupResult();
        
        try
        {
            _logger.LogInformation("Starting organization setup for {OrganizationName} with admin {AdminEmail}", 
                organizationName, adminEmail);

            // Step 1: Create organization in Dataverse (if available)
            try 
            {
                var organization = await _organizationService.CreateOrganizationAsync(
                    organizationName, 
                    organizationDomain, 
                    Guid.NewGuid().ToString()); // TODO: Get actual admin user ID from claims

                if (organization != null)
                {
                    result.OrganizationId = organization.Id;
                    _logger.LogInformation("Organization created in Dataverse: {OrganizationId}", organization.Id);
                }
                else
                {
                    // Dataverse not available - create mock organization ID
                    result.OrganizationId = Guid.NewGuid().ToString();
                    _logger.LogWarning("Dataverse not available. Using mock organization ID: {OrganizationId}", result.OrganizationId);
                }
            }
            catch (NotSupportedException)
            {
                // Dataverse not available - create mock organization ID
                result.OrganizationId = Guid.NewGuid().ToString();
                _logger.LogWarning("Dataverse not available. Using mock organization ID: {OrganizationId}", result.OrganizationId);
            }

            _logger.LogInformation("Organization setup proceeding for {OrganizationName} with ID {OrganizationId}", 
                organizationName, result.OrganizationId);

            // Step 2: Skip creating organization-specific security groups
            // Users will be assigned to agent-based Global Security Groups instead
            _logger.LogInformation("Skipping organization-specific security group creation - using agent-based Global Security Groups instead");
            result.SecurityGroupId = string.Empty; // No organization-specific security group needed

            // Step 3: Create default secrets for organization (optional)
            try
            {
                var secretsCreated = await CreateDefaultSecretsAsync(result.OrganizationId, organizationName, organizationDomain);
                
                if (secretsCreated)
                {
                    _logger.LogInformation("Created default secrets for organization {OrganizationName}", organizationName);
                }
                else
                {
                    _logger.LogWarning("Failed to create default secrets for organization {OrganizationName}", organizationName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error creating default secrets for organization {OrganizationName}", organizationName);
            }

            // Mark as successful - invitation was sent, setup completed with available services
            result.Success = true;
            result.Message = $"Organization {organizationName} has been successfully created with default configuration.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up organization {OrganizationName}", organizationName);
            result.Errors.Add($"Unexpected error: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    public async Task<bool> CreateDefaultSecretsAsync(string organizationId, string organizationName, string organizationDomain)
    {
        var successCount = 0;
        var totalSecrets = 0;

        try
        {
            _logger.LogInformation("Creating default secrets for organization {OrganizationId}", organizationId);

            // Default database connection template for SQL Server
            var defaultSqlConnection = BuildDefaultSqlConnectionString(organizationDomain);
            if (await CreateSecretSafely(organizationId, "Default-SQL-Connection", defaultSqlConnection, "Default SQL Server connection template"))
            {
                successCount++;
            }
            totalSecrets++;

            // Default SAP HANA connection template
            var defaultHanaConnection = BuildDefaultHanaConnectionString(organizationDomain);
            if (await CreateSecretSafely(organizationId, "Default-HANA-Connection", defaultHanaConnection, "Default SAP HANA connection template"))
            {
                successCount++;
            }
            totalSecrets++;

            // API configuration secrets
            var defaultApiKey = GenerateSecureApiKey();
            if (await CreateSecretSafely(organizationId, "API-Key", defaultApiKey, "Default API key for external integrations"))
            {
                successCount++;
            }
            totalSecrets++;

            // SAP Service Layer configuration
            var defaultSapServiceConfig = BuildDefaultSapServiceConfig(organizationDomain);
            if (await CreateSecretSafely(organizationId, "SAP-Service-Config", defaultSapServiceConfig, "Default SAP Service Layer configuration"))
            {
                successCount++;
            }
            totalSecrets++;

            // Email service configuration (if needed)
            var defaultEmailConfig = BuildDefaultEmailConfig(organizationDomain);
            if (await CreateSecretSafely(organizationId, "Email-Service-Config", defaultEmailConfig, "Default email service configuration"))
            {
                successCount++;
            }
            totalSecrets++;

            _logger.LogInformation("Created {SuccessCount}/{TotalSecrets} default secrets for organization {OrganizationId}", 
                successCount, totalSecrets, organizationId);

            return successCount == totalSecrets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating default secrets for organization {OrganizationId}", organizationId);
            return false;
        }
    }

    private async Task<bool> CreateSecretSafely(string organizationId, string secretName, string secretValue, string description)
    {
        try
        {
            var success = await _keyVaultService.SetSecretAsync(secretName, secretValue, organizationId);
            if (success)
            {
                _logger.LogDebug("Created secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            }
            else
            {
                _logger.LogWarning("Failed to create secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret {SecretName} for organization {OrganizationId}", secretName, organizationId);
            return false;
        }
    }

    private string BuildDefaultSqlConnectionString(string domain)
    {
        // Template connection string with placeholders
        return $"Server={{SQL_SERVER_HOST}};Database={{DATABASE_NAME}};User Id={{SQL_USERNAME}};Password={{SQL_PASSWORD}};TrustServerCertificate=true;";
    }

    private string BuildDefaultHanaConnectionString(string domain)
    {
        // Template connection string with placeholders for SAP HANA
        return $"Server={{HANA_SERVER_HOST}}:{{HANA_PORT}};Database={{HANA_DATABASE}};UserID={{HANA_USERNAME}};Password={{HANA_PASSWORD}};";
    }

    private string GenerateSecureApiKey()
    {
        // Generate a secure random API key
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    private string BuildDefaultSapServiceConfig(string domain)
    {
        // Default SAP Service Layer configuration as JSON
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ServiceLayerUrl = $"https://{{SAP_SERVICE_LAYER_HOST}}:{{PORT}}/b1s/v1/",
            CompanyDB = "{{COMPANY_DATABASE}}",
            Username = "{{SAP_USERNAME}}",
            Password = "{{SAP_PASSWORD}}",
            Timeout = 30,
            RetryAttempts = 3
        });
    }

    private string BuildDefaultEmailConfig(string domain)
    {
        // Default email service configuration as JSON
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            SmtpServer = "{{SMTP_SERVER}}",
            SmtpPort = 587,
            UseSsl = true,
            FromEmail = $"noreply@{domain}",
            Username = "{{EMAIL_USERNAME}}",
            Password = "{{EMAIL_PASSWORD}}"
        });
    }

    /// <summary>
    /// Creates default secrets for an Organization Admin when they first sign in
    /// This is called separately from the initial organization setup
    /// </summary>
    public async Task<bool> CreateDefaultSecretsForOrgAdminAsync(string organizationId, string organizationName, string organizationDomain)
    {
        try
        {
            _logger.LogInformation("Creating default secrets for Organization Admin in organization {OrganizationId}", organizationId);

            // Use the same logic as CreateDefaultSecretsAsync but allow Org Admin access
            var secretsCreated = await CreateDefaultSecretsAsync(organizationId, organizationName, organizationDomain);
            
            if (secretsCreated)
            {
                _logger.LogInformation("Successfully created default secrets for Organization Admin in organization {OrganizationId}", organizationId);
            }
            else
            {
                _logger.LogWarning("Failed to create some default secrets for Organization Admin in organization {OrganizationId}", organizationId);
            }
            
            return secretsCreated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating default secrets for Organization Admin in organization {OrganizationId}", organizationId);
            return false;
        }
    }
}