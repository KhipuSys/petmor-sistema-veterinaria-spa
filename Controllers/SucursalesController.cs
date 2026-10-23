using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Petmor.Web.Services;

namespace Petmor.Web.Controllers;

[Authorize(Roles = "Administrador")]
public class SucursalesController(SedeActual sede) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cambiar(int sucursalId, string returnUrl = null)
    {
        if (!sede.Disponibles.Any(x => x.Id == sucursalId)) return Forbid();
        Response.Cookies.Append("petmor-sede", sucursalId.ToString(), new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax,
            IsEssential = true, Path = "/"
        });
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }
}
