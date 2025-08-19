using Microsoft.AspNetCore.Mvc;
using AdminConsole.Services;
using AdminConsole.Models;

namespace AdminConsole.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticController : ControllerBase
{
    private readonly IKeyVaultService _keyVaultService;
    private readonly IOrganizationService _organizationService;
    private readonly IDataIsolationService _dataIsolationService;
    private readonly ILogger<DiagnosticController> _logger;

    public DiagnosticController(
        IKeyVaultService keyVaultService,
        IOrganizationService organizationService,
        IDataIsolationService dataIsolationService,
        ILogger<DiagnosticController> logger)
    {
        _keyVaultService = keyVaultService;
        _organizationService = organizationService;
        _dataIsolationService = dataIsolationService;
        _logger = logger;
    }

    [HttpGet("keyvault-test2")]
    public async Task<IActionResult> TestKeyVault()
    {
        try
        {
            var results = new List<string>();
            
            // Test 1: Environment and Authentication Info
            results.Add("=== Environment and Authentication Diagnostics ===");
            results.Add($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
            results.Add($"Machine Name: {Environment.MachineName}");
            results.Add($"User Domain Name: {Environment.UserDomainName}");
            results.Add($"User Name: {Environment.UserName}");
            
            // Check for Azure App Service environment variables
            var appServiceName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            results.Add($"Azure App Service Name: {appServiceName ?? "Not running in Azure App Service"}");
            
            var managedIdentityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            results.Add($"Managed Identity Endpoint: {managedIdentityEndpoint ?? "Managed Identity not available"}");
            
            var managedIdentityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            results.Add($"Managed Identity Header: {managedIdentityHeader ?? "Managed Identity header not set"}");
            
            // Test 2: Get current user org ID
            results.Add("=== Testing Current User Organization ID ===");
            var orgId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            results.Add($"Organization ID: {orgId}");
            
            // Test 3: Test organization lookup
            results.Add("=== Testing Organization Lookup ===");
            if (!string.IsNullOrEmpty(orgId))
            {
                var org = await _organizationService.GetByIdAsync(orgId);
                results.Add($"Organization found: {org != null}");
                if (org != null)
                {
                    results.Add($"Organization name: {org.Name}");
                    results.Add($"Organization domain: {org.Domain}");
                }
            }
            
            // Test 4: Test Key Vault connectivity
            results.Add("=== Testing Key Vault Connectivity ===");
            var kvTest = await _keyVaultService.TestConnectivityAsync();
            results.Add($"Key Vault connectivity: {kvTest.Success}");
            results.Add($"Key Vault message: {kvTest.Message}");
            
            // Test 5: Try to set a test secret with string format
            results.Add("=== Testing Secret Creation (String Format) ===");
            if (!string.IsNullOrEmpty(orgId))
            {
                var testResult = await _keyVaultService.SetSecretAsync("diagnostic-test", "test-value", orgId);
                results.Add($"Test secret creation (string): {testResult}");
            }
            
            // Test 6: Try to set a test secret with GUID format (like database credentials do)
            results.Add("=== Testing Secret Creation (GUID Format) ===");
            if (!string.IsNullOrEmpty(orgId))
            {
                // Convert to GUID like ManageDatabaseCredentials does
                var orgIdBytes = System.Text.Encoding.UTF8.GetBytes(orgId);
                var hash = System.Security.Cryptography.MD5.HashData(orgIdBytes);
                var guidOrgId = new Guid(hash);
                results.Add($"GUID Organization ID: {guidOrgId}");
                
                var testResult2 = await _keyVaultService.SetSecretAsync("diagnostic-test-guid", "test-value", guidOrgId.ToString());
                results.Add($"Test secret creation (GUID): {testResult2}");
            }
            
            // Test 7: Try to set a consolidated secret like database credentials do
            results.Add("=== Testing Consolidated Secret Creation (Like Database Credentials) ===");
            if (!string.IsNullOrEmpty(orgId))
            {
                // Use GUID format like database credentials do
                var orgIdBytes = System.Text.Encoding.UTF8.GetBytes(orgId);
                var hash = System.Security.Cryptography.MD5.HashData(orgIdBytes);
                var guidOrgId = new Guid(hash);
                results.Add($"Using GUID Organization ID for consolidated test: {guidOrgId}");
                
                var consolidatedTags = new Dictionary<string, string>
                {
                    { "connectionString", "Server=test;Database=test;User=test;Password=test" },
                    { "secretType", "consolidated" },
                    { "sapServiceLayer", "test.hostname.com" }
                };
                
                results.Add($"Consolidated tags count: {consolidatedTags.Count}");
                foreach (var tag in consolidatedTags)
                {
                    results.Add($"  Tag: {tag.Key} = {tag.Value} (length: {tag.Value.Length})");
                }
                
                var testResult3 = await _keyVaultService.SetSecretWithTagsAsync("diagnostic-consolidated-test", "sap-test-password", guidOrgId.ToString(), consolidatedTags);
                results.Add($"Test consolidated secret creation (GUID org): {testResult3}");
                
                if (testResult3)
                {
                    results.Add("✅ Consolidated secret creation SUCCEEDED - this should work for database credentials");
                }
                else
                {
                    results.Add("🚨 Consolidated secret creation FAILED - this explains why database credentials have empty ConsolidatedSecretName");
                }
            }
            
            return Ok(new { success = true, results = results });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}