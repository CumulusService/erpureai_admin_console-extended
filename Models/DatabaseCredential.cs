using System.ComponentModel.DataAnnotations;

namespace AdminConsole.Models;

/// <summary>
/// Represents shared database credentials for SAP Business One access
/// These are organization-level credentials that all users in the org share
/// </summary>
public class DatabaseCredential
{
    /// <summary>
    /// Unique identifier for this database credential set
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Organization this credential belongs to
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// Database type (MSSQL or HANA)
    /// </summary>
    [Required]
    public DatabaseType DatabaseType { get; set; }
    
    /// <summary>
    /// Database server hostname or IP address
    /// </summary>
    [Required]
    [StringLength(255)]
    public string ServerInstance { get; set; } = string.Empty;
    
    /// <summary>
    /// Database instance name (SQL Server only, e.g., "FP2502" for "SERVER\\FP2502")
    /// </summary>
    [StringLength(128)]
    public string? InstanceName { get; set; } = string.Empty;
    
    /// <summary>
    /// Database name/schema
    /// </summary>
    [Required]
    [StringLength(128)]
    public string DatabaseName { get; set; } = string.Empty;
    
    /// <summary>
    /// Friendly name for this database configuration (e.g., "Production", "Test", "Development")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string FriendlyName { get; set; } = string.Empty;
    
    /// <summary>
    /// Database username for connecting to SQL Server/HANA
    /// </summary>
    [Required]
    [StringLength(128)]
    public string DatabaseUsername { get; set; } = string.Empty;
    
    /// <summary>
    /// SAP Business One username (shared across all users in org)
    /// </summary>
    [Required]
    [StringLength(128)]
    public string SAPUsername { get; set; } = string.Empty;
    
    /// <summary>
    /// Reference to the Key Vault secret containing the SAP password (main password field)
    /// </summary>
    [Required]
    public string PasswordSecretName { get; set; } = string.Empty;
    
    /// <summary>
    /// Reference to the Key Vault secret containing the database connection string
    /// </summary>
    public string? ConnectionStringSecretName { get; set; } = string.Empty;
    
    /// <summary>
    /// Full connection string for this database (used with EF Core) - DEPRECATED: Use ConnectionStringSecretName instead
    /// </summary>
    [StringLength(1000)]
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional description for this database configuration
    /// </summary>
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this credential set is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When these credentials were created
    /// </summary>
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When these credentials were last updated
    /// </summary>
    public DateTime ModifiedOn { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who created these credentials
    /// </summary>
    public Guid CreatedBy { get; set; }
    
    /// <summary>
    /// Who last modified these credentials
    /// </summary>
    public Guid ModifiedBy { get; set; }
    
    /// <summary>
    /// Port for database connection
    /// </summary>
    public int? Port { get; set; }
    
    /// <summary>
    /// Additional connection properties for HANA
    /// </summary>
    [StringLength(128)]
    public string? CurrentSchema { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether to encrypt the connection (HANA)
    /// </summary>
    public bool Encrypt { get; set; } = true;
    
    /// <summary>
    /// Whether to validate SSL certificates (HANA)
    /// </summary>
    public bool SSLValidateCertificate { get; set; } = false;
    
    /// <summary>
    /// Whether to trust server certificate (MSSQL)
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;
}

/// <summary>
/// Model for creating/updating database credentials
/// </summary>
public class DatabaseCredentialModel
{
    [Required(ErrorMessage = "Database type is required")]
    public DatabaseType? DatabaseType { get; set; }
    
    [Required(ErrorMessage = "Server host/IP is required")]
    [StringLength(255, ErrorMessage = "Server host/IP cannot exceed 255 characters")]
    [RegularExpression(@"^([a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?\.)*[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?$|^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9]$|^[a-zA-Z0-9]$", 
        ErrorMessage = "Server host/IP must be a valid hostname, FQDN, or IP address")]
    public string ServerInstance { get; set; } = string.Empty;
    
    [StringLength(128, ErrorMessage = "Instance name cannot exceed 128 characters")]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9_\\@:.-]*$", ErrorMessage = "Instance name can contain letters, numbers, underscores, backslashes, @ symbols, colons, dots, and hyphens")]
    public string? InstanceName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Database name is required")]
    [StringLength(128, ErrorMessage = "Database name cannot exceed 128 characters")]
    public string DatabaseName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Friendly name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Friendly name must be between 2 and 100 characters")]
    public string FriendlyName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Database username is required")]
    [StringLength(128, ErrorMessage = "Database username cannot exceed 128 characters")]
    public string DatabaseUsername { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Database password is required")]
    [StringLength(256, ErrorMessage = "Database password cannot exceed 256 characters")]
    public string DatabasePassword { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "SAP username is required")]
    [StringLength(128, ErrorMessage = "SAP username cannot exceed 128 characters")]
    public string SAPUsername { get; set; } = string.Empty;
    
    [StringLength(256, ErrorMessage = "SAP password cannot exceed 256 characters")]
    public string SAPPassword { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string Description { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    // Port for database connections
    public int? Port { get; set; }
    
    // Additional connection properties for HANA
    [StringLength(128, ErrorMessage = "Current schema cannot exceed 128 characters")]
    public string? CurrentSchema { get; set; } = string.Empty;
    
    public bool Encrypt { get; set; } = true;
    
    public bool SSLValidateCertificate { get; set; } = false;
    
    // Additional connection properties for MSSQL
    public bool TrustServerCertificate { get; set; } = true;
}

/// <summary>
/// Extension methods for DatabaseCredential
/// </summary>
public static class DatabaseCredentialExtensions
{
    /// <summary>
    /// Generates the Key Vault secret name for storing the SAP password
    /// Uses credential ID to ensure uniqueness across multiple databases
    /// </summary>
    public static string GeneratePasswordSecretName(this DatabaseCredential credential)
    {
        var dbTypeString = credential.DatabaseType.ToString().ToLowerInvariant();
        var friendlyName = credential.FriendlyName.Replace(" ", "-").Replace("_", "-").ToLowerInvariant();
        return $"sap-password-{dbTypeString}-{friendlyName}-{credential.Id.ToString("N")[..8]}";
    }
    
    /// <summary>
    /// Generates the Key Vault secret name for storing the database connection string
    /// Uses credential ID to ensure uniqueness across multiple databases
    /// </summary>
    public static string GenerateConnectionStringSecretName(this DatabaseCredential credential)
    {
        var dbTypeString = credential.DatabaseType.ToString().ToLowerInvariant();
        var friendlyName = credential.FriendlyName.Replace(" ", "-").Replace("_", "-").ToLowerInvariant();
        return $"connection-string-{dbTypeString}-{friendlyName}-{credential.Id.ToString("N")[..8]}";
    }
    
    /// <summary>
    /// Gets a display name for this credential set
    /// </summary>
    public static string GetDisplayName(this DatabaseCredential credential)
    {
        return $"{credential.FriendlyName} ({credential.DatabaseType} - {credential.DatabaseName})";
    }
    
    /// <summary>
    /// Builds a connection string using these credentials
    /// Note: Password will need to be retrieved from Key Vault separately
    /// </summary>
    public static string BuildConnectionStringTemplate(this DatabaseCredential credential)
    {
        return credential.DatabaseType switch
        {
            DatabaseType.MSSQL => 
                $"Server={credential.ServerInstance}{(credential.Port.HasValue ? $",{credential.Port}" : "")};Database={credential.DatabaseName};User Id={credential.DatabaseUsername};Password={{password}};TrustServerCertificate={credential.TrustServerCertificate.ToString().ToLower()};",
            
            DatabaseType.HANA => 
                $"Server={credential.ServerInstance}{(credential.Port.HasValue ? $":{credential.Port}" : "")};Database={credential.DatabaseName};UID={credential.DatabaseUsername};Password={{password}};{(!string.IsNullOrEmpty(credential.CurrentSchema) ? $"CurrentSchema={credential.CurrentSchema};" : "")}Encrypt={credential.Encrypt.ToString().ToLower()};SSLValidateCertificate={credential.SSLValidateCertificate.ToString().ToLower()};",
            
            _ => throw new ArgumentException($"Unsupported database type: {credential.DatabaseType}")
        };
    }
    
    /// <summary>
    /// Builds a connection string template from a model (for testing before creation)
    /// </summary>
    public static string BuildConnectionStringTemplate(this DatabaseCredentialModel model)
    {
        return model.DatabaseType switch
        {
            DatabaseType.MSSQL => 
                $"Server={model.ServerInstance}{(model.Port.HasValue ? $",{model.Port}" : "")};Database={model.DatabaseName};User Id={model.DatabaseUsername};Password={{password}};TrustServerCertificate={model.TrustServerCertificate.ToString().ToLower()};",
            
            DatabaseType.HANA => 
                $"Server={model.ServerInstance}{(model.Port.HasValue ? $":{model.Port}" : "")};Database={model.DatabaseName};UID={model.DatabaseUsername};Password={{password}};{(!string.IsNullOrEmpty(model.CurrentSchema) ? $"CurrentSchema={model.CurrentSchema};" : "")}Encrypt={model.Encrypt.ToString().ToLower()};SSLValidateCertificate={model.SSLValidateCertificate.ToString().ToLower()};",
            
            _ => throw new ArgumentException($"Unsupported database type: {model.DatabaseType}")
        };
    }
}

/// <summary>
/// Model for updating general settings (non-password properties)
/// </summary>
public class DatabaseCredentialGeneralSettingsModel
{
    [Required]
    [StringLength(100)]
    public string FriendlyName { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    [Required]
    [StringLength(128)]
    public string SAPUsername { get; set; } = string.Empty;
    
    [Required]
    [StringLength(128)]  
    public string DatabaseUsername { get; set; } = string.Empty;
    
    [Required]
    [StringLength(255)]
    public string ServerInstance { get; set; } = string.Empty;
    
    [StringLength(128)]
    public string? InstanceName { get; set; } = string.Empty;
    
    [Required]
    [StringLength(128)]
    public string DatabaseName { get; set; } = string.Empty;
    
    public int? Port { get; set; }
    
    [StringLength(128)]
    public string? CurrentSchema { get; set; } = string.Empty;
    
    public bool Encrypt { get; set; } = true;
    
    public bool SSLValidateCertificate { get; set; } = false;
    
    public bool TrustServerCertificate { get; set; } = true;
}