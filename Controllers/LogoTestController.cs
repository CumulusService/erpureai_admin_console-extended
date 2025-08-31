using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminConsole.Controllers;

[AllowAnonymous]
public class LogoTestController : Controller
{
    [HttpGet("/logo-test")]
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
            
            return Json(result);
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