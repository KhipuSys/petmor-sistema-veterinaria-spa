using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Petmor.Web.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class TwoFactorAuthModel : PageModel
    {
        private const int RecoveryCodeCount = 10;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public TwoFactorAuthModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty]
        public DisableInputModel DisableInput { get; set; } = new();

        public string SharedKey { get; private set; }
        public string QrCodeDataUri { get; private set; }
        public bool Is2faEnabled { get; private set; }
        public string[] RecoveryCodes { get; private set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {
            [StringLength(7, MinimumLength = 6, ErrorMessage = "Ingrese los seis dígitos que muestra Authy.")]
            public string Code { get; set; }
        }

        public class DisableInputModel
        {
            [StringLength(7, MinimumLength = 6, ErrorMessage = "Ingrese los seis dígitos que muestra Authy.")]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            await LoadAuthenticatorSetupAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostEnableAsync()
        {
            ModelState.Remove("DisableInput");
            ModelState.Remove("DisableInput.Code");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            if (await _userManager.GetTwoFactorEnabledAsync(user))
                return RedirectToPage();

            if (!ModelState.IsValid)
            {
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            var code = NormalizeAuthenticatorCode(Input?.Code);
            if (!IsSixDigitCode(code) || !await IsCurrentAuthenticatorCodeValidAsync(user, code))
            {
                await RecordFailedAttemptAsync(user);
                ModelState.AddModelError("Input.Code", "Código inválido o vencido. Verifique el código actual en Authy.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            var enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enableResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "No se pudo activar la verificación en dos pasos.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            await _signInManager.RefreshSignInAsync(user);
            RecoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount)).ToArray();
            StatusMessage = "Verificación en dos pasos activada correctamente.";
            Is2faEnabled = true;
            SharedKey = null;
            QrCodeDataUri = null;
            return Page();
        }

        public async Task<IActionResult> OnPostDisableAsync()
        {
            ModelState.Remove("Input");
            ModelState.Remove("Input.Code");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            if (!await _userManager.GetTwoFactorEnabledAsync(user))
                return RedirectToPage();

            if (!ModelState.IsValid)
            {
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            var code = NormalizeAuthenticatorCode(DisableInput?.Code);
            if (!IsSixDigitCode(code) || !await IsCurrentAuthenticatorCodeValidAsync(user, code))
            {
                await RecordFailedAttemptAsync(user);
                ModelState.AddModelError("DisableInput.Code", "Código inválido o vencido. Verifique el código actual en Authy.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            var disableResult = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (!disableResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "No se pudo desactivar la verificación en dos pasos.");
                await LoadAuthenticatorSetupAsync(user);
                return Page();
            }

            await _userManager.ResetAuthenticatorKeyAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Verificación en dos pasos desactivada.";
            return RedirectToPage();
        }

        private async Task<bool> IsCurrentAuthenticatorCodeValidAsync(IdentityUser user, string code) =>
            await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                code);

        private async Task RecordFailedAttemptAsync(IdentityUser user)
        {
            if (await _userManager.IsLockedOutAsync(user))
                return;

            await _userManager.AccessFailedAsync(user);
        }

        private async Task LoadAuthenticatorSetupAsync(IdentityUser user)
        {
            Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            SharedKey = null;
            QrCodeDataUri = null;

            if (Is2faEnabled)
                return;

            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                var reset = await _userManager.ResetAuthenticatorKeyAsync(user);
                if (!reset.Succeeded)
                    throw new InvalidOperationException("No se pudo preparar la clave del autenticador.");
                key = await _userManager.GetAuthenticatorKeyAsync(user);
                await _signInManager.RefreshSignInAsync(user);
            }

            SharedKey = FormatKey(key);
            QrCodeDataUri = GenerateQrCodeDataUri(user.Email ?? user.UserName ?? "Petmor", key);
        }

        private static string NormalizeAuthenticatorCode(string code) =>
            (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

        private static bool IsSixDigitCode(string code) =>
            code.Length == 6 && code.All(character => character is >= '0' and <= '9');

        private static string FormatKey(string key)
        {
            var formatted = new StringBuilder();
            for (var position = 0; position < key.Length; position += 4)
            {
                if (formatted.Length > 0)
                    formatted.Append(' ');
                formatted.Append(key.AsSpan(position, Math.Min(4, key.Length - position)));
            }
            return formatted.ToString().ToLowerInvariant();
        }

        private static string GenerateQrCodeDataUri(string account, string key)
        {
            const string issuer = "Petmor";
            var label = Uri.EscapeDataString($"{issuer}:{account}");
            var encodedIssuer = Uri.EscapeDataString(issuer);
            var authenticatorUri = $"otpauth://totp/{label}?secret={Uri.EscapeDataString(key)}&issuer={encodedIssuer}&digits=6&period=30&algorithm=SHA1";

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(authenticatorUri, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(8);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
    }
}
