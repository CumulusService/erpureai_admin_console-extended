using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;

namespace AdminConsole.Controllers
{
    public class PermissionTestController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<PermissionTestController> _logger;

        public PermissionTestController(
            GraphServiceClient graphServiceClient,
            ILogger<PermissionTestController> logger)
        {
            _graphServiceClient = graphServiceClient;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Test()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RunTest()
        {
            try
            {
                _logger.LogInformation("Testing Graph permissions");

                // Test 1: Try to read current application
                try
                {
                    var app = await _graphServiceClient.Applications.GetAsync();
                    ViewBag.Test1 = "✅ Can read applications";
                }
                catch (Exception ex)
                {
                    ViewBag.Test1 = $"❌ Cannot read applications: {ex.Message}";
                }

                // Test 2: Try to read users
                try
                {
                    var users = await _graphServiceClient.Users.GetAsync(config => {
                        config.QueryParameters.Top = 1;
                    });
                    ViewBag.Test2 = $"✅ Can read users (found {users?.Value?.Count ?? 0})";
                }
                catch (Exception ex)
                {
                    ViewBag.Test2 = $"❌ Cannot read users: {ex.Message}";
                }

                // Test 3: Try to read groups
                try
                {
                    var groups = await _graphServiceClient.Groups.GetAsync(config => {
                        config.QueryParameters.Top = 1;
                    });
                    ViewBag.Test3 = $"✅ Can read groups (found {groups?.Value?.Count ?? 0})";
                }
                catch (Exception ex)
                {
                    ViewBag.Test3 = $"❌ Cannot read groups: {ex.Message}";
                }

                return View("TestResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Permission test failed");
                ViewBag.Error = $"❌ Overall test failed: {ex.Message}";
                return View("TestResult");
            }
        }
    }
}