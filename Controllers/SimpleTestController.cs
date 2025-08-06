using Microsoft.AspNetCore.Mvc;
using AdminConsole.Services;

namespace AdminConsole.Controllers
{
    public class SimpleTestController : Controller
    {
        private readonly IGraphService _graphService;
        private readonly ISecurityGroupService _securityGroupService;
        private readonly ILogger<SimpleTestController> _logger;

        public SimpleTestController(
            IGraphService graphService,
            ISecurityGroupService securityGroupService,
            ILogger<SimpleTestController> logger)
        {
            _graphService = graphService;
            _securityGroupService = securityGroupService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Test()
        {
            return View();
        }

        [HttpGet]
        [Route("api/claims")]
        public IActionResult Claims()
        {
            var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();
            
            // Check SuperAdmin policy manually
            var email = User.FindFirst("email")?.Value ?? 
                       User.FindFirst("preferred_username")?.Value ?? 
                       User.FindFirst("upn")?.Value ?? 
                       User.FindFirst("unique_name")?.Value ??
                       User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
            
            var result = new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated,
                AuthenticationType = User.Identity?.AuthenticationType,
                Name = User.Identity?.Name,
                ExtractedEmail = email,
                IsErpureUser = email?.EndsWith("@erpure.ai") == true,
                Claims = claims
            };
            
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> Test(string adminEmail, string adminName, string orgName)
        {
            try
            {
                _logger.LogInformation("Testing Graph invitation for {Email}", adminEmail);

                // Test Microsoft Graph B2B invitation only (skip Dataverse)
                var invitedUser = await _graphService.InviteAdminUserAsync(adminEmail, adminName, orgName);
                
                ViewBag.Success = $"✅ SUCCESS! B2B invitation sent to {adminEmail}";
                ViewBag.UserDetails = $"Invited User ID: {invitedUser?.Id}";
                ViewBag.Email = invitedUser?.Email;
                
                return View("TestResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Graph invitation test failed");
                ViewBag.Error = $"❌ ERROR: {ex.Message}";
                return View("TestResult");
            }
        }
    }
}