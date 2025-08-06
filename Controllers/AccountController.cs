using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminConsole.Controllers;

[Route("[controller]")]
public class AccountController : Controller
{
    [HttpGet("Challenge")]
    [AllowAnonymous]
    public new IActionResult Challenge()
    {
        var redirectUrl = Url.Action("SignedIn", "Account");
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("SignedIn")]
    public IActionResult SignedIn()
    {
        // Redirect to home page after successful sign-in
        return Redirect("/");
    }

    [HttpGet("SignOut")]
    [AllowAnonymous]
    public new IActionResult SignOut()
    {
        var callbackUrl = Url.Action("SignedOut", "Account", values: null, protocol: Request.Scheme);
        return SignOut(
            new AuthenticationProperties { RedirectUri = callbackUrl },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme
        );
    }

    [HttpGet("SignedOut")]
    [AllowAnonymous]
    public IActionResult SignedOut()
    {
        return Redirect("/");
    }
}