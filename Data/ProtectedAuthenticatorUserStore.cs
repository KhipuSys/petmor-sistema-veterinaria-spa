using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Petmor.Web.Data
{
    /// <summary>
    /// Protects authenticator keys and recovery-code tokens at rest while
    /// preserving the ASP.NET Core Identity APIs used by the standard TOTP flow.
    /// </summary>
    public sealed class ProtectedAuthenticatorUserStore : UserStore<IdentityUser, IdentityRole, ApplicationDbContext>
    {
        private const string ProtectedPrefix = "dp:v1:";
        private const string RecoveryCodesTokenName = "RecoveryCodes";
        private readonly IDataProtector _authenticatorKeyProtector;
        private readonly IDataProtector _recoveryCodesProtector;

        public ProtectedAuthenticatorUserStore(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IdentityErrorDescriber describer = null)
            : base(context, describer)
        {
            _authenticatorKeyProtector = dataProtectionProvider.CreateProtector("Petmor.Identity.AuthenticatorKey.v1");
            _recoveryCodesProtector = dataProtectionProvider.CreateProtector("Petmor.Identity.RecoveryCodes.v1");
        }

        public override async Task<string> GetAuthenticatorKeyAsync(
            IdentityUser user,
            CancellationToken cancellationToken = default)
        {
            var value = await base.GetAuthenticatorKeyAsync(user, cancellationToken);
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return _authenticatorKeyProtector.Unprotect(value[ProtectedPrefix.Length..]);

            // Migrate an existing Identity token from plaintext the first time it is read.
            await base.SetAuthenticatorKeyAsync(user, Protect(_authenticatorKeyProtector, value), cancellationToken);
            await Context.SaveChangesAsync(cancellationToken);
            return value;
        }

        public override Task SetAuthenticatorKeyAsync(
            IdentityUser user,
            string key,
            CancellationToken cancellationToken = default)
        {
            var protectedKey = string.IsNullOrEmpty(key) || key.StartsWith(ProtectedPrefix, StringComparison.Ordinal)
                ? key
                : Protect(_authenticatorKeyProtector, key);
            return base.SetAuthenticatorKeyAsync(user, protectedKey, cancellationToken);
        }

        public override async Task<string> GetTokenAsync(
            IdentityUser user,
            string loginProvider,
            string name,
            CancellationToken cancellationToken = default)
        {
            var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
            if (!IsRecoveryCodes(name) || string.IsNullOrEmpty(value))
                return value;

            if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return _recoveryCodesProtector.Unprotect(value[ProtectedPrefix.Length..]);

            await base.SetTokenAsync(user, loginProvider, name, Protect(_recoveryCodesProtector, value), cancellationToken);
            await Context.SaveChangesAsync(cancellationToken);
            return value;
        }

        public override Task SetTokenAsync(
            IdentityUser user,
            string loginProvider,
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            if (IsRecoveryCodes(name) && !string.IsNullOrEmpty(value) &&
                !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                value = Protect(_recoveryCodesProtector, value);
            }

            return base.SetTokenAsync(user, loginProvider, name, value, cancellationToken);
        }

        private static string Protect(IDataProtector protector, string value) => ProtectedPrefix + protector.Protect(value);

        private static bool IsRecoveryCodes(string name) =>
            string.Equals(name, RecoveryCodesTokenName, StringComparison.Ordinal);
    }
}
