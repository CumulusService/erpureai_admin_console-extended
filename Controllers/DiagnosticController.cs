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
            
            // Test 1: Get current user org ID
            results.Add("=== Testing Current User Organization ID ===");
            var orgId = await _dataIsolationService.GetCurrentUserOrganizationIdAsync();
            results.Add($"Organization ID: {orgId}");
            
            // Test 2: Test organization lookup
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
            
            // Test 3: Test Key Vault connectivity
            results.Add("=== Testing Key Vault Connectivity ===");
            var kvTest = await _keyVaultService.TestConnectivityAsync();
            results.Add($"Key Vault connectivity: {kvTest.Success}");
            results.Add($"Key Vault message: {kvTest.Message}");
            
            // Test 4: Try to set a test secret with string format
            results.Add("=== Testing Secret Creation (String Format) ===");
            if (!string.IsNullOrEmpty(orgId))
            {
                var testResult = await _keyVaultService.SetSecretAsync("diagnostic-test", "test-value", orgId);
                results.Add($"Test secret creation (string): {testResult}");
            }
            
            // Test 5: Try to set a test secret with GUID format (like database credentials do)
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
            
            return Ok(new { success = true, results = results });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}