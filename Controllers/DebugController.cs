using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using AdminConsole.Data;
using AdminConsole.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AdminConsole.Controllers
{
    [Authorize(Policy = "SuperAdminOnly")]
    public class DebugController : Controller
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<DebugController> _logger;
        private readonly AdminConsoleDbContext _context;
        private readonly IDatabaseCredentialService _databaseCredentialService;
        private readonly IEmailService _emailService;
        private readonly IAgentTypeService _agentTypeService;

        public DebugController(
            GraphServiceClient graphClient,
            ILogger<DebugController> logger,
            AdminConsoleDbContext context,
            IDatabaseCredentialService databaseCredentialService,
            IEmailService emailService,
            IAgentTypeService agentTypeService)
        {
            _graphClient = graphClient;
            _logger = logger;
            _context = context;
            _databaseCredentialService = databaseCredentialService;
            _emailService = emailService;
            _agentTypeService = agentTypeService;
        }

        [HttpGet("/debug/credential/{credentialId}")]
        public async Task<IActionResult> CheckCredential(Guid credentialId)
        {
            try
            {
                var credential = await _context.DatabaseCredentials
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == credentialId);

                if (credential == null)
                {
                    return NotFound($"Credential {credentialId} not found");
                }

                var result = new
                {
                    CredentialId = credential.Id,
                    FriendlyName = credential.FriendlyName,
                    OrganizationId = credential.OrganizationId,
                    DatabaseType = credential.DatabaseType.ToString(),
                    ServerInstance = credential.ServerInstance,
                    DatabaseName = credential.DatabaseName,
                    DatabaseUsername = credential.DatabaseUsername,
                    SAPUsername = credential.SAPUsername,
                    IsActive = credential.IsActive,
                    CreatedOn = credential.CreatedOn,
                    ModifiedOn = credential.ModifiedOn,
                    
                    // Key Vault references
                    PasswordSecretName = credential.PasswordSecretName,
                    ConnectionStringSecretName = credential.ConnectionStringSecretName,
                    ConsolidatedSecretName = credential.ConsolidatedSecretName,
                    
                    // SAP Configuration
                    SAPServiceLayerHostname = credential.SAPServiceLayerHostname,
                    SAPAPIGatewayHostname = credential.SAPAPIGatewayHostname,
                    SAPBusinessOneWebClientHost = credential.SAPBusinessOneWebClientHost,
                    DocumentCode = credential.DocumentCode
                };

                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpGet("/debug/test-permissions")]
        public async Task<IActionResult> TestPermissions()
        {
            try
            {
                var user = await _graphClient.Me.GetAsync();
                
                var permissions = new List<string>();
                
                try
                {
                    var groups = await _graphClient.Groups.GetAsync();
                    permissions.Add("Groups.Read.All - OK");
                }
                catch (Exception ex)
                {
                    permissions.Add($"Groups.Read.All - FAILED: {ex.Message}");
                }

                try
                {
                    var users = await _graphClient.Users.GetAsync();
                    permissions.Add("Users.Read.All - OK");
                }
                catch (Exception ex)
                {
                    permissions.Add($"Users.Read.All - FAILED: {ex.Message}");
                }

                return Json(new 
                { 
                    CurrentUser = user?.DisplayName,
                    UserId = user?.Id,
                    Permissions = permissions
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpGet("/debug/agenttypes")]
        public async Task<IActionResult> GetAgentTypes()
        {
            try
            {
                var agentTypes = await _context.AgentTypes
                    .AsNoTracking()
                    .Select(at => new {
                        at.Id,
                        at.Name,
                        at.DisplayName,
                        at.Description,
                        at.GlobalSecurityGroupId,
                        at.AgentShareUrl,
                        at.TeamsAppId,
                        at.IsActive,
                        at.DisplayOrder,
                        at.CreatedDate,
                        at.ModifiedDate
                    })
                    .OrderBy(at => at.DisplayOrder)
                    .ToListAsync();

                return Json(agentTypes, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ClearDatabaseCache()
        {
            try
            {
                var activeCredentials = await _context.DatabaseCredentials
                    .Where(dc => dc.IsActive)
                    .Select(s => new {
                        s.Id,
                        s.FriendlyName,
                        s.DatabaseName,
                        s.DatabaseType,
                        s.IsActive
                    })
                    .ToListAsync();

                var allCredentials = await _context.DatabaseCredentials
                    .Select(s => new {
                        s.Id,
                        s.FriendlyName,
                        s.DatabaseName,
                        s.DatabaseType,
                        s.IsActive
                    })
                    .ToListAsync();

                var result = new
                {
                    Message = "Cache cleared and database queried directly",
                    ActiveCredentials = activeCredentials,
                    AllCredentials = new
                    {
                        Count = allCredentials.Count,
                        Items = allCredentials.Select(s => new {
                            s.Id,
                            s.FriendlyName,
                            s.DatabaseName,
                            s.DatabaseType,
                            s.IsActive
                        })
                    }
                };
                
                _logger.LogInformation("Cache cleared and database queried directly - Active: {ActiveCount}, Total: {TotalCount}", activeCredentials.Count, allCredentials.Count);
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache clear operation");
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }
        
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> FindDatabaseOrgIds()
        {
            try
            {
                var allDatabases = await _context.DatabaseCredentials
                    .Select(d => new {
                        d.Id,
                        d.FriendlyName,
                        d.DatabaseName,
                        d.OrganizationId,
                        d.IsActive
                    })
                    .ToListAsync();
                
                var uniqueOrgIds = allDatabases.Select(d => d.OrganizationId).Distinct().ToList();
                
                var result = new
                {
                    SearchingForOrgId = "ac2987b9-d357-738f-a737-92733866ce54",
                    AllDatabaseCredentials = allDatabases,
                    UniqueOrganizationIds = uniqueOrgIds,
                    DatabasesByOrgId = uniqueOrgIds.ToDictionary(
                        orgId => orgId.ToString(),
                        orgId => allDatabases.Where(d => d.OrganizationId == orgId).ToList()
                    )
                };
                
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetSystemStats()
        {
            try
            {
                var result = new
                {
                    Timestamp = DateTime.UtcNow,
                    Organizations = new
                    {
                        Total = await _context.Organizations.CountAsync(),
                        Active = await _context.Organizations.CountAsync(o => o.StateCode == AdminConsole.Models.StateCode.Active)
                    },
                    Users = new
                    {
                        Total = await _context.OnboardedUsers.CountAsync(),
                        Active = await _context.OnboardedUsers.CountAsync(u => u.StateCode == AdminConsole.Models.StateCode.Active)
                    },
                    DatabaseCredentials = new
                    {
                        Total = await _context.DatabaseCredentials.CountAsync(),
                        Active = await _context.DatabaseCredentials.CountAsync(dc => dc.IsActive)
                    },
                    AgentTypes = new
                    {
                        Total = await _context.AgentTypes.CountAsync(),
                        Active = await _context.AgentTypes.CountAsync(at => at.IsActive)
                    }
                };
                
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpGet("/debug/test-email")]
        [AllowAnonymous]
        public async Task<IActionResult> TestEmail([FromQuery] string? toEmail = null)
        {
            try
            {
                var testEmail = toEmail ?? "test@erpure.ai";
                
                _logger.LogInformation("📧 Testing email functionality - sending to {ToEmail}", testEmail);

                // Test email service configuration
                var isConfigured = await _emailService.IsConfiguredAsync();
                if (!isConfigured)
                {
                    return Json(new { 
                        Success = false, 
                        Error = "Email service is not properly configured",
                        ConfigurationStatus = "Not Configured"
                    });
                }

                // Get a test agent type for the email template
                var agentTypes = await _agentTypeService.GetActiveAgentTypesAsync();
                var testAgentType = agentTypes.FirstOrDefault() ?? new AdminConsole.Models.AgentTypeEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "TestAgent",
                    DisplayName = "Test Agent",
                    Description = "This is a test agent for email verification.",
                    AgentShareUrl = "https://teams.microsoft.com/l/app/test-agent-id"
                };

                // Send test email
                var emailSent = await _emailService.SendAgentAssignmentNotificationAsync(
                    testEmail,
                    "Test User",
                    testAgentType,
                    "Test Organization");

                var result = new
                {
                    Success = emailSent,
                    ToEmail = testEmail,
                    AgentType = testAgentType.DisplayName,
                    ConfigurationStatus = "Configured",
                    Message = emailSent ? "Test email sent successfully!" : "Failed to send test email",
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation("📧 Test email result: {Success}", emailSent);
                
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📧 Error testing email functionality");
                return Json(new { 
                    Success = false,
                    Error = ex.Message, 
                    StackTrace = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("/debug/email-config")]
        public async Task<IActionResult> CheckEmailConfig()
        {
            try
            {
                var isConfigured = await _emailService.IsConfiguredAsync();
                
                var result = new
                {
                    IsConfigured = isConfigured,
                    ConfigurationCheck = "Email service configuration validated",
                    Timestamp = DateTime.UtcNow,
                    Message = isConfigured ? "Email service is properly configured" : "Email service configuration missing"
                };
                
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    Error = ex.Message,
                    IsConfigured = false,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("/debug/logo-test")]
        [AllowAnonymous]
        public async Task<IActionResult> TestLogo()
        {
            try
            {
                var logoPath = @"C:\Users\mn\AdminConsole-Production\wwwroot\company-logo.png";
                var botIconPath = @"C:\Users\mn\AdminConsole-Production\wwwroot\4712086.png";
                
                var result = new Dictionary<string, object>
                {
                    ["LogoPath"] = logoPath,
                    ["LogoExists"] = System.IO.File.Exists(logoPath),
                    ["LogoSize"] = System.IO.File.Exists(logoPath) ? new FileInfo(logoPath).Length : 0,
                    ["BotIconPath"] = botIconPath,
                    ["BotIconExists"] = System.IO.File.Exists(botIconPath),
                    ["BotIconSize"] = System.IO.File.Exists(botIconPath) ? new FileInfo(botIconPath).Length : 0,
                    ["Timestamp"] = DateTime.UtcNow
                };

                // Test base64 encoding
                if (System.IO.File.Exists(logoPath))
                {
                    try
                    {
                        var logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
                        var logoBase64 = Convert.ToBase64String(logoBytes);
                        result["LogoBase64Preview"] = logoBase64.Substring(0, Math.Min(100, logoBase64.Length)) + "...";
                        result["LogoBase64Length"] = logoBase64.Length;
                    }
                    catch (Exception logoEx)
                    {
                        result["LogoError"] = logoEx.Message;
                    }
                }
                
                return Json(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    Error = ex.Message,
                    StackTrace = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
}