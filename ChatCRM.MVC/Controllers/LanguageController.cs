using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers;

[Route("language")]
public sealed class LanguageController : Controller
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "en", "ru", "ro", "tr" };

    [HttpPost("set")]
    [ValidateAntiForgeryToken]
    public IActionResult Set([FromForm] string culture, [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(culture) || !Allowed.Contains(culture))
            return BadRequest();

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture, culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = false
            });

        // Only follow the returnUrl if it's a local path — never an absolute external URL.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return LocalRedirect("/");
    }
}
