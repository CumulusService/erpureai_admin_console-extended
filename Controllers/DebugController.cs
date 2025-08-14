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

        public DebugController(
            GraphServiceClient graphClient,
            ILogger<DebugController> logger,
            AdminConsoleDbContext context,
            IDatabaseCredentialService databaseCredentialService)
        {
            _graphClient = graphClient;
            _logger = logger;
            _context = context;
            _databaseCredentialService = databaseCredentialService;
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
                    ConsolidatedSecretName = credential.ConsolidatedSecretName ?? "NULL/EMPTY",
                    PasswordSecretName = credential.PasswordSecretName ?? "NULL/EMPTY",
                    ConnectionStringSecretName = credential.ConnectionStringSecretName ?? "NULL/EMPTY",
                    DatabaseName = credential.DatabaseName,
                    CurrentSchema = credential.CurrentSchema,
                    DatabaseType = credential.DatabaseType.ToString()
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking credential {CredentialId}", credentialId);
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpGet]
        public IActionResult Test()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Test(string email)
        {
            try
            {
                _logger.LogInformation("Testing B2B invitation to {Email}", email);

                var invitation = new Invitation
                {
                    InvitedUserEmailAddress = email,
                    InvitedUserDisplayName = "Test User",
                    InviteRedirectUrl = "http://localhost:5243",
                    SendInvitationMessage = true,
                    InvitedUserMessageInfo = new InvitedUserMessageInfo
                    {
                        MessageLanguage = "en-US",
                        CustomizedMessageBody = "Test invitation from AdminConsole"
                    }
                };

                _logger.LogInformation("Sending invitation with redirect URL: {RedirectUrl}", invitation.InviteRedirectUrl);

                var result = await _graphClient.Invitations.PostAsync(invitation);
                
                ViewBag.Success = $"✅ Invitation sent successfully!";
                ViewBag.InvitationId = result?.Id;
                ViewBag.Status = result?.Status;
                
                return View("Result");
            }
            catch (Exception ex) when (ex.GetType().Name == "ServiceException")
            {
                _logger.LogError(ex, "Graph service exception: {Message}", ex.Message);
                ViewBag.Error = $"Graph Error: {ex.Message}";
                ViewBag.Details = ex.ToString();
                return View("Result");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General exception during invitation");
                ViewBag.Error = $"General Error: {ex.Message}";
                return View("Result");
            }
        }

        [HttpGet]
        public async Task<IActionResult> InvestigateUserGroups(string azureObjectId = "966aeda9-5970-40b2-a6c1-7fedae698229")
        {
            try
            {
                var results = new Dictionary<string, object>();
                
                // Query 1: Find user by Azure Object ID
                _logger.LogInformation("Querying OnboardedUsers for Azure Object ID: {AzureObjectId}", azureObjectId);
                var user = await _context.OnboardedUsers
                    .Where(u => u.AzureObjectId == azureObjectId)
                    .FirstOrDefaultAsync();
                
                if (user == null)
                {
                    results["Error"] = $"No user found with Azure Object ID: {azureObjectId}";
                    return Json(results);
                }
                
                results["User"] = new
                {
                    user.OnboardedUserId,
                    user.Email,
                    user.FullName,
                    user.AzureObjectId,
                    user.OrganizationId,
                    user.IsActive,
                    user.StateCode,
                    user.StatusCode,
                    AgentTypeIds = user.AgentTypeIds,
                    LegacyAgentTypes = user.AgentTypes
                };
                
                // Query 2: Get organization details
                if (user.OrganizationId.HasValue)
                {
                    var organization = await _context.Organizations
                        .Where(o => o.OrganizationId == user.OrganizationId.Value)
                        .FirstOrDefaultAsync();
                    
                    results["Organization"] = organization != null ? new
                    {
                        organization.OrganizationId,
                        organization.Name,
                        organization.M365GroupId
                    } : null;
                }
                
                // Query 3: Get all agent types to understand security group mappings
                var agentTypes = await _context.AgentTypes
                    .Where(at => at.IsActive)
                    .ToListAsync();
                    
                results["AllAgentTypes"] = agentTypes.Select(at => new
                {
                    at.Id,
                    at.Name,
                    at.DisplayName,
                    at.GlobalSecurityGroupId,
                    at.IsActive
                });
                
                // Query 4: Get expected security groups for user's agent types
                var expectedSecurityGroups = new List<object>();
                foreach (var agentTypeId in user.AgentTypeIds)
                {
                    var agentType = agentTypes.FirstOrDefault(at => at.Id == agentTypeId);
                    if (agentType != null)
                    {
                        expectedSecurityGroups.Add(new
                        {
                            AgentTypeId = agentType.Id,
                            AgentTypeName = agentType.Name,
                            SecurityGroupId = agentType.GlobalSecurityGroupId
                        });
                    }
                }
                results["ExpectedSecurityGroups"] = expectedSecurityGroups;
                
                // Query 5: Get current user-to-group assignments
                var currentAssignments = await _context.UserAgentTypeGroupAssignments
                    .Where(ua => ua.UserId == azureObjectId && ua.IsActive)
                    .ToListAsync();
                    
                results["CurrentAssignments"] = currentAssignments.Select(ca => new
                {
                    ca.Id,
                    ca.UserId,
                    ca.AgentTypeId,
                    ca.SecurityGroupId,
                    ca.OrganizationId,
                    ca.IsActive,
                    ca.AssignedDate,
                    ca.AssignedBy
                });
                
                // Analysis: Compare expected vs current
                var expectedGroupIds = expectedSecurityGroups
                    .Select(g => ((dynamic)g).SecurityGroupId)
                    .Where(id => !string.IsNullOrEmpty(id?.ToString()))
                    .ToHashSet();
                    
                var currentGroupIds = currentAssignments
                    .Select(a => a.SecurityGroupId)
                    .ToHashSet();
                    
                results["Analysis"] = new
                {
                    ExpectedGroupCount = expectedGroupIds.Count,
                    CurrentGroupCount = currentGroupIds.Count,
                    MissingGroups = expectedGroupIds.Except(currentGroupIds).ToList(),
                    ExtraGroups = currentGroupIds.Except(expectedGroupIds).ToList(),
                    GroupsMatch = expectedGroupIds.SetEquals(currentGroupIds)
                };
                
                _logger.LogInformation("Investigation completed for user {AzureObjectId}", azureObjectId);
                return Json(results, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error investigating user groups for {AzureObjectId}", azureObjectId);
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// Debug endpoint to investigate database cleanup and duplicate data issues
        /// </summary>
        [HttpGet("cleanup-investigation")]
        [AllowAnonymous]
        public async Task<IActionResult> CleanupInvestigation()
        {
            try
            {
                var results = new Dictionary<string, object>();
                _logger.LogInformation("Starting database cleanup investigation");

                // Query 1: Active credentials per organization
                var orgCredentialCounts = await _context.Organizations
                    .Select(o => new
                    {
                        OrganizationName = o.Name,
                        OrganizationId = o.OrganizationId,
                        ActiveCredentialCount = _context.DatabaseCredentials
                            .Count(dc => dc.OrganizationId == o.OrganizationId && dc.IsActive),
                        TotalCredentialCount = _context.DatabaseCredentials
                            .Count(dc => dc.OrganizationId == o.OrganizationId)
                    })
                    .ToListAsync();
                results["OrganizationCredentialCounts"] = orgCredentialCounts;

                // Query 2: All credentials (active and inactive) with details
                var allCredentials = await _context.DatabaseCredentials
                    .Join(_context.Organizations, dc => dc.OrganizationId, o => o.OrganizationId, (dc, o) => new
                    {
                        OrganizationName = o.Name,
                        FriendlyName = dc.FriendlyName,
                        DatabaseType = dc.DatabaseType,
                        DatabaseName = dc.DatabaseName,
                        CurrentSchema = dc.CurrentSchema,
                        IsActive = dc.IsActive,
                        Id = dc.Id,
                        OrganizationId = dc.OrganizationId
                    })
                    .OrderBy(dc => dc.OrganizationName)
                    .ThenByDescending(dc => dc.IsActive)
                    .ThenBy(dc => dc.FriendlyName)
                    .ToListAsync();
                results["AllCredentials"] = allCredentials;

                // Query 3: Basic user assignment information
                var basicUserInfo = await _context.OnboardedUsers
                    .Where(u => u.AssignedDatabaseIds.Any())
                    .Select(u => new
                    {
                        UserName = u.Name,
                        Email = u.Email,
                        OrganizationId = u.OrganizationId,
                        AssignedDatabaseCount = u.AssignedDatabaseIds.Count,
                        AssignedDatabaseIds = u.AssignedDatabaseIds
                    })
                    .ToListAsync();
                results["BasicUserInfo"] = basicUserInfo;

                // Query 4: Check for orphaned UserDatabaseAssignments
                var orphanedAssignments = await (from uda in _context.UserDatabaseAssignments
                    where uda.IsActive && !_context.DatabaseCredentials.Any(dc => dc.Id == uda.DatabaseCredentialId)
                    join u in _context.OnboardedUsers on uda.UserId equals u.OnboardedUserId
                    select new
                    {
                        UserId = uda.UserId,
                        DatabaseCredentialId = uda.DatabaseCredentialId,
                        UserName = u.Name,
                        UserEmail = u.Email,
                        Status = "ORPHANED - Credential Not Found"
                    }).ToListAsync();
                results["OrphanedAssignments"] = orphanedAssignments;

                // Query 5: Summary statistics
                var summary = new
                {
                    TotalOrganizations = await _context.Organizations.CountAsync(),
                    TotalActiveCredentials = await _context.DatabaseCredentials.CountAsync(dc => dc.IsActive),
                    TotalInactiveCredentials = await _context.DatabaseCredentials.CountAsync(dc => !dc.IsActive),
                    TotalUsers = await _context.OnboardedUsers.CountAsync(),
                    UsersWithAssignments = await _context.OnboardedUsers.CountAsync(u => u.AssignedDatabaseIds.Any()),
                    TotalUserDatabaseAssignments = await _context.UserDatabaseAssignments.CountAsync(uda => uda.IsActive),
                    OrphanedAssignmentsCount = orphanedAssignments.Count
                };
                results["Summary"] = summary;

                _logger.LogInformation("Database cleanup investigation completed");
                return Json(results, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database cleanup investigation");
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }
        
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ForceCacheClear()
        {
            try
            {
                var orgId = Guid.Parse("ac2987b9-d357-738f-a737-92733866ce54");
                
                // Force clear cache using reflection
                var cacheField = _databaseCredentialService.GetType()
                    .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (cacheField != null)
                {
                    var cache = cacheField.GetValue(_databaseCredentialService) as Microsoft.Extensions.Caching.Memory.IMemoryCache;
                    if (cache != null)
                    {
                        // Clear both cache keys
                        cache.Remove($"org_credentials_{orgId}");
                        cache.Remove($"org_active_credentials_{orgId}");
                    }
                }
                
                // Direct database query - bypass cache completely
                var allCredentials = await _context.DatabaseCredentials
                    .Where(d => d.OrganizationId == orgId)
                    .Select(d => new {
                        d.Id,
                        d.FriendlyName,
                        d.DatabaseName,
                        d.DatabaseType,
                        d.IsActive,
                        d.CreatedOn
                    })
                    .OrderBy(d => d.CreatedOn)
                    .ToListAsync();
                
                var activeCredentials = allCredentials.Where(d => d.IsActive).ToList();
                
                // Also test the service method
                var serviceResult = await _databaseCredentialService.GetActiveByOrganizationAsync(orgId);
                
                var result = new
                {
                    CacheCleared = true,
                    OrganizationId = orgId,
                    DirectDatabaseQuery = new
                    {
                        TotalCredentials = allCredentials.Count,
                        ActiveCredentials = activeCredentials.Count,
                        InactiveCredentials = allCredentials.Count - activeCredentials.Count,
                        AllCredentials = allCredentials
                    },
                    ServiceMethodResult = new
                    {
                        Count = serviceResult?.Count ?? 0,
                        Credentials = serviceResult?.Select(s => new
                        {
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
        public async Task<IActionResult> RunCleanupAll()
        {
            try
            {
                var orgId = Guid.Parse("ac2987b9-d357-738f-a737-92733866ce54");
                
                // Get valid database IDs
                var validDatabaseIds = await _context.DatabaseCredentials
                    .Where(d => d.OrganizationId == orgId && d.IsActive)
                    .Select(d => d.Id)
                    .ToListAsync();
                
                // Get users with stale database assignments
                var usersToUpdate = await _context.OnboardedUsers
                    .Where(u => u.OrganizationLookupId == orgId && u.AssignedDatabaseIds.Any())
                    .ToListAsync();
                
                var results = new List<object>();
                int updatedCount = 0;
                
                foreach (var user in usersToUpdate)
                {
                    var originalCount = user.AssignedDatabaseIds.Count;
                    var validAssignments = user.AssignedDatabaseIds.Where(id => validDatabaseIds.Contains(id)).ToList();
                    var removedCount = originalCount - validAssignments.Count;
                    
                    if (removedCount > 0)
                    {
                        user.AssignedDatabaseIds = validAssignments;
                        updatedCount++;
                        
                        results.Add(new {
                            UserId = user.OnboardedUserId,
                            Email = user.Email,
                            OriginalCount = originalCount,
                            ValidCount = validAssignments.Count,
                            RemovedCount = removedCount,
                            ValidDatabaseIds = validAssignments
                        });
                    }
                }
                
                await _context.SaveChangesAsync();
                
                return Json(new {
                    Success = true,
                    ValidDatabaseIds = validDatabaseIds,
                    UsersUpdated = updatedCount,
                    UpdatedUsers = results
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return Json(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

    }
}