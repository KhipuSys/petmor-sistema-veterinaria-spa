using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Petmor.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese su usuario.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Ingrese su contraseña.")]
            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl = NormalizeReturnUrl(returnUrl);

            // Limpiar cookie externa para asegurar un login limpio
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl = NormalizeReturnUrl(returnUrl);

            if (ModelState.IsValid)
            {
                Input.Email = NormalizarUsuario(Input.Email);
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, false, lockoutOnFailure: true);
                
                if (result.Succeeded)
                {
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl });
                }
                if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente por varios intentos fallidos.");
                    return Page();
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Intento de inicio de sesión no válido.");
                    return Page();
                }
            }

            return Page();
        }

        private string NormalizeReturnUrl(string returnUrl) =>
            string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Content("~/") : returnUrl;

        private static string NormalizarUsuario(string usuario)
        {
            usuario = (usuario ?? string.Empty).Trim();
            return usuario.Contains('@') ? usuario : usuario + "@petmor.pe";
        }
    }
}
