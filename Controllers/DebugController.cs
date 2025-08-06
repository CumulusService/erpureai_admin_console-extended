using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace AdminConsole.Controllers
{
    public class DebugController : Controller
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<DebugController> _logger;

        public DebugController(
            GraphServiceClient graphClient,
            ILogger<DebugController> logger)
        {
            _graphClient = graphClient;
            _logger = logger;
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
                    InviteRedirectUrl = "http://localhost:5242",
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
    }
}