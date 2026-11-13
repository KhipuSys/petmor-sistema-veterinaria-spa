using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Petmor.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginWith2faModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese el código de Authy.")]
            [StringLength(7, ErrorMessage = "Ingrese los seis dígitos que muestra Authy.", MinimumLength = 6)]
            public string TwoFactorCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null)
        {
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
                return RedirectToPage("./Login");

            ReturnUrl = NormalizeReturnUrl(returnUrl);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            ReturnUrl = NormalizeReturnUrl(returnUrl);
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
                return RedirectToPage("./Login");

            if (!ModelState.IsValid)
                return Page();

            var code = NormalizeCode(Input?.TwoFactorCode);
            if (code.Length != 6 || !code.All(character => character is >= '0' and <= '9'))
            {
                ModelState.AddModelError("Input.TwoFactorCode", "Ingrese un código Authy de seis dígitos.");
                return Page();
            }

            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                code,
                isPersistent: false,
                rememberClient: false);

            if (result.Succeeded)
                return LocalRedirect(ReturnUrl);

            if (result.IsLockedOut)
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente por varios intentos fallidos.");
            else
                ModelState.AddModelError(string.Empty, "Código inválido o vencido. Verifique el código actual en Authy.");

            return Page();
        }

        private string NormalizeReturnUrl(string returnUrl) =>
            string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Content("~/") : returnUrl;

        private static string NormalizeCode(string code) =>
            (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
