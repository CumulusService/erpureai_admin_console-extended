using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminConsole.Controllers
{
    [Authorize]
    [Route("claims")]
    public class ClaimsDebugController : Controller
    {
        [Route("")]
        [Route("index")]
        public IActionResult Index()
        {
            var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();
            
            ViewBag.Claims = claims;
            ViewBag.IsAuthenticated = User.Identity?.IsAuthenticated;
            ViewBag.AuthenticationType = User.Identity?.AuthenticationType;
            ViewBag.Name = User.Identity?.Name;
            
            return View();
        }
    }
}