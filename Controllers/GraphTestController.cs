using Microsoft.AspNetCore.Mvc;
using AdminConsole.Services;

namespace AdminConsole.Controllers
{
    public class GraphTestController : Controller
    {
        private readonly IGraphService _graphService;
        private readonly ILogger<GraphTestController> _logger;

        public GraphTestController(
            IGraphService graphService,
            ILogger<GraphTestController> logger)
        {
            _graphService = graphService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Test()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Test(string adminEmail, string adminName, string orgName)
        {
            try
            {
                _logger.LogInformation("Testing GraphService.InviteAdminUserAsync for {Email}", adminEmail);

                // Use the exact same GraphService method as SimpleTest
                var invitedUser = await _graphService.InviteAdminUserAsync(adminEmail, adminName, orgName);
                
                ViewBag.Success = $"✅ SUCCESS! GraphService invitation sent to {adminEmail}";
                ViewBag.UserDetails = $"Invited User ID: {invitedUser?.Id}";
                ViewBag.Email = invitedUser?.Email;
                ViewBag.Status = invitedUser?.InvitationStatus;
                
                return View("TestResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GraphService invitation test failed");
                ViewBag.Error = $"❌ ERROR: {ex.Message}";
                ViewBag.Details = ex.ToString();
                return View("TestResult");
            }
        }
    }
}