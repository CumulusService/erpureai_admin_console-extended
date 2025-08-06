using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing shared database credentials for SAP Business One access
/// These credentials are shared across all users in an organization for a given database type
/// </summary>
public interface IDatabaseCredentialService
{
    /// <summary>
    /// Gets all database credentials for an organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <returns>List of database credentials (passwords retrieved from Key Vault separately)</returns>
    Task<List<DatabaseCredential>> GetByOrganizationAsync(Guid organizationId);
    
    /// <summary>
    /// Gets database credentials for a specific organization and database type
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <param name="databaseType">Database type (MSSQL or HANA)</param>
    /// <returns>List of database credentials matching the type</returns>
    Task<List<DatabaseCredential>> GetByOrganizationAndTypeAsync(Guid organizationId, DatabaseType databaseType);
    
    /// <summary>
    /// Creates new shared database credentials for an organization
    /// </summary>
    /// <param name="organizationId">Organization ID</param>
    /// <param name="model">Credential model including password</param>
    /// <param name="createdBy">User ID who is creating the credentials</param>
    /// <returns>Created database credential</returns>
    Task<DatabaseCredential> CreateAsync(Guid organizationId, DatabaseCredentialModel model, Guid createdBy);
    
    /// <summary>
    /// Updates existing database credentials
    /// </summary>
    /// <param name="credentialId">Credential ID to update</param>
    /// <param name="model">Updated credential model</param>
    /// <param name="modifiedBy">User ID who is updating the credentials</param>
    /// <returns>Updated database credential</returns>
    Task<DatabaseCredential> UpdateAsync(Guid credentialId, DatabaseCredentialModel model, Guid modifiedBy);
    
    /// <summary>
    /// Updates general settings (non-password properties) and syncs with Key Vault metadata
    /// </summary>
    /// <param name="credentialId">Credential ID to update</param>
    /// <param name="model">General settings model</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is updating the settings</param>
    /// <returns>True if updated successfully</returns>
    Task<bool> UpdateGeneralSettingsAsync(Guid credentialId, DatabaseCredentialGeneralSettingsModel model, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Soft deletes database credentials (sets IsActive to false)
    /// </summary>
    /// <param name="credentialId">Credential ID to delete</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteAsync(Guid credentialId, Guid organizationId);
    
    /// <summary>
    /// Hard deletes database credentials, removes all secrets from Key Vault, and removes from database
    /// </summary>
    /// <param name="credentialId">Credential ID to delete</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> HardDeleteAsync(Guid credentialId, Guid organizationId);
    
    /// <summary>
    /// Tests database connection using the credentials
    /// </summary>
    /// <param name="credentialId">Credential ID to test</param>
    /// <returns>Connection test result</returns>
    Task<DatabaseConnectionTestResult> TestConnectionAsync(Guid credentialId);
    
    /// <summary>
    /// Tests database connection before creating credentials (for validation during creation)
    /// </summary>
    /// <param name="model">Database credential model with connection details</param>
    /// <returns>Connection test result with detailed information</returns>
    Task<DatabaseConnectionTestResult> TestConnectionBeforeCreateAsync(DatabaseCredentialModel model);
    
    /// <summary>
    /// Gets the SAP password for a credential from Key Vault
    /// </summary>
    /// <param name="credentialId">Credential ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>Decrypted SAP password</returns>
    Task<string?> GetSAPPasswordAsync(Guid credentialId, Guid organizationId);
    
    /// <summary>
    /// Updates just the SAP password for existing credentials
    /// </summary>
    /// <param name="credentialId">Credential ID</param>
    /// <param name="newPassword">New SAP password</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <param name="modifiedBy">User ID who is updating the password</param>
    /// <returns>True if updated successfully</returns>
    Task<bool> UpdateSAPPasswordAsync(Guid credentialId, string newPassword, Guid organizationId, Guid modifiedBy);
    
    /// <summary>
    /// Gets a specific database credential by ID
    /// </summary>
    /// <param name="credentialId">Credential ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>Database credential if found and belongs to organization</returns>
    Task<DatabaseCredential?> GetByIdAsync(Guid credentialId, Guid organizationId);
    
    /// <summary>
    /// Builds a complete connection string for a specific database credential
    /// </summary>
    /// <param name="credentialId">Database credential ID</param>
    /// <param name="organizationId">Organization ID for security validation</param>
    /// <returns>Complete connection string with password</returns>
    Task<string?> BuildConnectionStringAsync(Guid credentialId, Guid organizationId);
}

/// <summary>
/// Result of testing a database connection
/// </summary>
public class DatabaseConnectionTestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    public string? DatabaseVersion { get; set; }
    public string? ServerInfo { get; set; }
    public string? AdditionalInfo { get; set; }
}