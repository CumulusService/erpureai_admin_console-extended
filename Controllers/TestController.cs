using Microsoft.AspNetCore.Mvc;
using AdminConsole.Services;
using AdminConsole.Models;

namespace AdminConsole.Controllers
{
    public class TestController : Controller
    {
        private readonly IGraphService _graphService;
        private readonly IOrganizationService _organizationService;
        private readonly ISecurityGroupService _securityGroupService;
        private readonly ILogger<TestController> _logger;

        public TestController(
            IGraphService graphService,
            IOrganizationService organizationService,
            ISecurityGroupService securityGroupService,
            ILogger<TestController> logger)
        {
            _graphService = graphService;
            _organizationService = organizationService;
            _securityGroupService = securityGroupService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Invite()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Invite(string adminEmail, string adminName, string orgName, string orgDomain)
        {
            try
            {
                _logger.LogInformation("Starting invitation process for {Email}", adminEmail);

                // Step 1: Create organization
                var organization = await _organizationService.CreateOrganizationAsync(orgName, orgDomain, "temp-id");
                ViewBag.Step1 = $"✅ Created organization: {organization.OrganizationId}";

                // Step 2: Create security group
                var groupId = await _securityGroupService.CreateOrganizationSecurityGroupAsync(organization);
                ViewBag.Step2 = $"✅ Created security group: {groupId}";

                // Step 3: Send invitation
                var invitedUser = await _graphService.InviteAdminUserAsync(adminEmail, adminName, orgName);
                ViewBag.Step3 = $"✅ Invited user: {invitedUser?.Id}";

                // Step 4: Add to group
                if (!string.IsNullOrEmpty(invitedUser?.Id) && !string.IsNullOrEmpty(groupId))
                {
                    var addResult = await _securityGroupService.AddUserToOrganizationGroupAsync(
                        invitedUser.Id, organization.OrganizationId.ToString());
                    ViewBag.Step4 = $"✅ Added to group: {addResult}";
                }

                ViewBag.Success = $"SUCCESS! Invitation sent to {adminEmail}";
                return View("Result");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invitation failed");
                ViewBag.Error = $"ERROR: {ex.Message}";
                return View("Result");
            }
        }
    }
}