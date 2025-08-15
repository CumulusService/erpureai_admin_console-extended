using AdminConsole.Models;

namespace AdminConsole.Services;

public class OrganizationSetupService : IOrganizationSetupService
{
    private readonly IOrganizationService _organizationService;
    private readonly IGraphService _graphService;
    private readonly IKeyVaultService _keyVaultService;
    private readonly ITeamsGroupService _teamsGroupService;
    private readonly ILogger<OrganizationSetupService> _logger;

    public OrganizationSetupService(
        IOrganizationService organizationService,
        IGraphService graphService,
        IKeyVaultService keyVaultService,
        ITeamsGroupService teamsGroupService,
        ILogger<OrganizationSetupService> logger)
    {
        _organizationService = organizationService;
        _graphService = graphService;
        _keyVaultService = keyVaultService;
        _teamsGroupService = teamsGroupService;
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

            Organization? organization = null;
            
            // Step 1: Create organization in Dataverse (if available)
            try 
            {
                organization = await _organizationService.CreateOrganizationAsync(
                    organizationName, 
                    organizationDomain, 
                    Guid.NewGuid().ToString(), // TODO: Get actual admin user ID from claims
                    adminEmail);

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

            // Step 2: Create Teams group for organization and populate M365GroupId
            try
            {
                if (Guid.TryParse(result.OrganizationId, out var orgGuid))
                {
                    _logger.LogInformation("Creating Teams group for organization {OrganizationName} (ID: {OrganizationId})", 
                        organizationName, result.OrganizationId);
                        
                    var teamsGroup = await _teamsGroupService.CreateOrganizationTeamsGroupAsync(orgGuid, Guid.NewGuid());
                    
                    if (teamsGroup != null)
                    {
                        _logger.LogInformation("Successfully created Teams group {GroupId} for organization {OrganizationId}", 
                            teamsGroup.TeamsGroupId, result.OrganizationId);
                            
                        // Update organization with M365GroupId
                        if (organization != null)
                        {
                            organization.M365GroupId = teamsGroup.TeamsGroupId;
                            var updateSuccess = await _organizationService.UpdateOrganizationAsync(organization);
                            
                            if (updateSuccess)
                            {
                                _logger.LogInformation("Successfully updated organization {OrganizationId} with M365GroupId {GroupId}", 
                                    result.OrganizationId, teamsGroup.TeamsGroupId);
                            }
                            else
                            {
                                _logger.LogError("Failed to update organization {OrganizationId} with M365GroupId {GroupId}", 
                                    result.OrganizationId, teamsGroup.TeamsGroupId);
                            }
                        }
                        
                        result.SecurityGroupId = teamsGroup.TeamsGroupId;
                    }
                    else
                    {
                        _logger.LogError("Failed to create Teams group for organization {OrganizationId}", result.OrganizationId);
                        result.Errors.Add("Failed to create Teams group for organization");
                    }
                }
                else
                {
                    _logger.LogError("Invalid organization ID format: {OrganizationId}", result.OrganizationId);
                    result.Errors.Add("Invalid organization ID format");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Teams group for organization {OrganizationName}", organizationName);
                result.Errors.Add($"Failed to create Teams group: {ex.Message}");
            }

            // Step 3: Skip creating organization-specific security groups (now handled by Teams group above)
            // Users will be assigned to agent-based Global Security Groups instead
            _logger.LogInformation("Teams group created - organization-specific security setup completed");

            // Step 4: Create default secrets for organization (optional)
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

    public Task<bool> CreateDefaultSecretsAsync(string organizationId, string organizationName, string organizationDomain)
    {
        try
        {
            _logger.LogInformation("Skipping default template secret creation for organization {OrganizationId} - using per-database consolidated secrets instead", organizationId);
            
            // REMOVED: Template secrets that are no longer needed:
            // - Default-SQL-Connection (just template with placeholders)
            // - Default-HANA-Connection (just template with placeholders) 
            // - SAP-Service-Config (SAP config now stored per-database)
            // - Email-Service-Config (not related to database credentials)
            // - API-Key (removed - not used by current application)
            
            // All database-specific configuration is now stored as:
            // 1. Database credentials in DatabaseCredentials table
            // 2. Consolidated secrets with SAP configuration as tags
            // 3. Organization-level SAP settings as fallback
            
            _logger.LogInformation("Organization {OrganizationId} setup completed without template secrets - using consolidated per-database approach", organizationId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateDefaultSecretsAsync for organization {OrganizationId}", organizationId);
            return Task.FromResult(false);
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
    public Task<bool> CreateDefaultSecretsForOrgAdminAsync(string organizationId, string organizationName, string organizationDomain)
    {
        try
        {
            _logger.LogInformation("Skipping template secret creation for Organization Admin in organization {OrganizationId} - using consolidated per-database secrets", organizationId);

            // UPDATED: No longer creating template secrets as we use consolidated per-database approach
            // Template secrets were: Default-SQL-Connection, Default-HANA-Connection, SAP-Service-Config, etc.
            // These are replaced by consolidated secrets created per database credential
            
            // Organization Admin setup is considered successful without template secrets
            // Database-specific secrets will be created when database credentials are added
            
            _logger.LogInformation("Organization Admin setup completed for organization {OrganizationId} - ready for database credential creation", organizationId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateDefaultSecretsForOrgAdminAsync for organization {OrganizationId}", organizationId);
            return Task.FromResult(false);
        }
    }
}