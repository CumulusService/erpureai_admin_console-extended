namespace AdminConsole.Services;

public interface IKeyVaultService
{
    Task<string?> GetSecretAsync(string secretName, string organizationId);
    Task<bool> SetSecretAsync(string secretName, string secretValue, string organizationId);
    Task<string?> GetSecretIdentifierAsync(string secretName, string organizationId);
    Task<(bool Success, string? NewVersionUri)> UpdateSecretByUriAsync(string secretUri, string secretValue, string organizationId);
    Task<(bool Success, string? NewVersionUri)> UpdateSecretMetadataByUriAsync(string secretUri, string secretValue, string organizationId, bool enabled, Dictionary<string, string>? additionalTags = null);
    Task<IEnumerable<string>> GetSecretNamesAsync(string organizationId);
    Task<bool> DeleteSecretAsync(string secretName, string organizationId);
    Task<bool> DeleteSecretByExactNameAsync(string exactSecretName);
    Task<string?> GetSecretByExactNameAsync(string exactSecretName);
    Task<(string? Value, bool? IsEnabled)> GetSecretWithPropertiesAsync(string exactSecretName);
    Task<(bool Success, string? NewVersionUri)> EnableDisabledSecretByUriAsync(string secretUri, string organizationId, Dictionary<string, string>? additionalTags = null);
    Task<(bool Success, string Message)> TestConnectivityAsync();
}