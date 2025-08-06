using System.Diagnostics;
using System.Text.Json;

namespace AdminConsole.Services;

/// <summary>
/// Service for executing PowerShell scripts, specifically for Teams App permission policies
/// </summary>
public interface IPowerShellExecutionService
{
    Task<PowerShellResult> ExecuteTeamsAppPermissionPolicyAsync(string tenantId, string groupId, string groupName, List<string> teamsAppIds);
}

/// <summary>
/// Result of PowerShell script execution
/// </summary>
public class PowerShellResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public Dictionary<string, object> ResultData { get; set; } = new();
    public int ExitCode { get; set; }
}

/// <summary>
/// PowerShell execution service implementation
/// </summary>
public class PowerShellExecutionService : IPowerShellExecutionService
{
    private readonly ILogger<PowerShellExecutionService> _logger;
    private readonly IConfiguration _configuration;

    public PowerShellExecutionService(ILogger<PowerShellExecutionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Executes the Teams App permission policy PowerShell script
    /// </summary>
    /// <param name="tenantId">Azure AD Tenant ID</param>
    /// <param name="groupId">Azure AD Group ID</param>
    /// <param name="groupName">Group display name</param>
    /// <param name="teamsAppIds">List of Teams App IDs to configure policies for</param>
    /// <returns>PowerShell execution result</returns>
    public async Task<PowerShellResult> ExecuteTeamsAppPermissionPolicyAsync(string tenantId, string groupId, string groupName, List<string> teamsAppIds)
    {
        var result = new PowerShellResult();
        
        try
        {
            _logger.LogInformation("Installing Teams Apps directly to specific team {GroupId} using Graph API for {AppCount} apps", 
                groupId, teamsAppIds.Count);

            // Use Graph API approach for direct team app installation (works with service principal)
            var graphScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Set-TeamsAppPermissionPolicies-Graph.ps1");
            var legacyScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Set-TeamsAppPermissionPolicies.ps1");
            
            // Prefer Graph API script (team-specific app installation), fallback to legacy
            var scriptPath = File.Exists(graphScriptPath) ? graphScriptPath : legacyScriptPath;
            
            var scriptType = scriptPath.Contains("-Graph.ps1") ? "Microsoft Graph API" : "Legacy Teams PowerShell";
            
            _logger.LogInformation("Using {ApproachType} for team-specific app installation: {ScriptPath}", 
                scriptType, Path.GetFileName(scriptPath));
            
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"PowerShell script not found at: {scriptPath}");
            }

            // Get Azure credentials
            var clientId = _configuration["AzureAd:ClientId"];
            var clientSecret = _configuration["AzureAd:ClientSecret"];

            // Prepare PowerShell command
            // Create proper PowerShell array parameter  
            var teamsAppIdsArray = string.Join(",", teamsAppIds.Select(id => $"'{id}'"));
            var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                           $"-TenantId \"{tenantId}\" " +
                           $"-GroupId \"{groupId}\" " +
                           $"-GroupName \"{groupName}\" " +
                           $"-TeamsAppIds {teamsAppIdsArray}";
            
            // Add client credentials if available
            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                arguments += $" -ClientId \"{clientId}\" -ClientSecret \"{clientSecret}\"";
            }

            _logger.LogInformation("PowerShell command: powershell.exe {Arguments}", arguments.Replace(clientSecret ?? "", "[REDACTED]"));

            // Create process start info
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            // Set environment variables for authentication (if needed)
            if (!string.IsNullOrEmpty(clientId))
            {
                processStartInfo.EnvironmentVariables["AZURE_CLIENT_ID"] = clientId;
            }
            
            if (!string.IsNullOrEmpty(clientSecret))
            {
                processStartInfo.EnvironmentVariables["AZURE_CLIENT_SECRET"] = clientSecret;
            }

            // Execute PowerShell script
            using var process = new Process { StartInfo = processStartInfo };
            
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug("PowerShell Output: {Output}", e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogWarning("PowerShell Error: {Error}", e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion with timeout
            var timeout = TimeSpan.FromMinutes(10); // 10 minute timeout
            if (!await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds)))
            {
                _logger.LogError("PowerShell script timed out after {Timeout} minutes", timeout.TotalMinutes);
                process.Kill();
                throw new TimeoutException($"PowerShell script execution timed out after {timeout.TotalMinutes} minutes");
            }

            result.ExitCode = process.ExitCode;
            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();

            // Parse JSON result if present
            var output = result.Output;
            var jsonStartIndex = output.IndexOf("RESULT_JSON_START");
            var jsonEndIndex = output.IndexOf("RESULT_JSON_END");
            
            if (jsonStartIndex != -1 && jsonEndIndex != -1)
            {
                var jsonStart = jsonStartIndex + "RESULT_JSON_START".Length;
                var jsonLength = jsonEndIndex - jsonStart;
                var jsonContent = output.Substring(jsonStart, jsonLength).Trim();
                
                try
                {
                    var jsonResult = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                    if (jsonResult != null)
                    {
                        result.ResultData = jsonResult;
                        
                        // Extract success status
                        if (jsonResult.TryGetValue("Success", out var successValue) && successValue is JsonElement successElement)
                        {
                            result.Success = successElement.GetBoolean();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse PowerShell JSON result: {JsonContent}", jsonContent);
                }
            }

            // Log results
            if (result.Success)
            {
                _logger.LogInformation("Teams Apps successfully installed to team {GroupId}", groupId);
                
                if (result.ResultData.TryGetValue("SuccessfulOperations", out var successCountValue) && 
                    result.ResultData.TryGetValue("TotalApps", out var totalAppsValue))
                {
                    _logger.LogInformation("App installation results: {SuccessCount}/{TotalCount} apps successfully installed to team", 
                        successCountValue, totalAppsValue);
                }
                else if (result.ResultData.TryGetValue("SuccessfulPolicies", out successCountValue) && 
                         result.ResultData.TryGetValue("TotalApps", out totalAppsValue))
                {
                    // Legacy script compatibility
                    _logger.LogInformation("App installation results: {SuccessCount}/{TotalCount} apps successfully processed", 
                        successCountValue, totalAppsValue);
                }
            }
            else
            {
                _logger.LogError("Teams App installation failed for group {GroupId}. Exit code: {ExitCode}", groupId, result.ExitCode);
                
                if (!string.IsNullOrEmpty(result.Error))
                {
                    _logger.LogError("Installation Error: {Error}", result.Error);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute Teams App permission policy PowerShell script for group {GroupId}", groupId);
            
            result.Success = false;
            result.Error = ex.Message;
            result.ExitCode = -1;
            
            return result;
        }
    }
}