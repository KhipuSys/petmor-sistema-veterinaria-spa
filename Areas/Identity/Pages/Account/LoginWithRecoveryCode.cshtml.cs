using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Petmor.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginWithRecoveryCodeModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese un código de recuperación.")]
            [StringLength(32, MinimumLength = 6, ErrorMessage = "Ingrese un código de recuperación válido.")]
            public string RecoveryCode { get; set; }
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
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
                return RedirectToPage("./Login");

            if (!ModelState.IsValid)
                return Page();

            var recoveryCode = (Input?.RecoveryCode ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
            if (result.Succeeded)
                return LocalRedirect(ReturnUrl);

            if (result.IsLockedOut)
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente por varios intentos fallidos.");
            else
                ModelState.AddModelError(string.Empty, "Código de recuperación inválido o ya utilizado.");

            return Page();
        }

        private string NormalizeReturnUrl(string returnUrl) =>
            string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Content("~/") : returnUrl;
    }
}
