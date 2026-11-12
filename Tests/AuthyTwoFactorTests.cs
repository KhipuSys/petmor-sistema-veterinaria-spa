using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Petmor.Web.Data;

static class AuthyTwoFactorTests
{
    public static async Task Run()
    {
        var password = Environment.GetEnvironmentVariable("PETMOR_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Define PETMOR_TEST_PASSWORD para ejecutar el flujo de autenticación.");

        var database = "Petmor_AuthyTest_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        var configuration = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyConfigurationAttribute>(typeof(ApplicationDbContext).Assembly)?.Configuration ?? "Debug";
        var startInfo = new ProcessStartInfo("dotnet", $"bin/{configuration}/net9.0/Petmor.Web.dll")
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = connection;

        using var server = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar Petmor para las pruebas.");
        var stdout = server.StandardOutput.ReadToEndAsync();
        var stderr = server.StandardError.ReadToEndAsync();
        var identityCollection = new ServiceCollection();
        identityCollection.AddLogging();
        identityCollection.AddDataProtection();
        identityCollection.AddDbContext<ApplicationDbContext>(db => db.UseSqlServer(connection));
        identityCollection.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        using var identityServices = identityCollection.BuildServiceProvider();

        async Task<string> AuthenticatorCode()
        {
            using var scope = identityServices.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync("admin@petmor.pe")
                ?? throw new InvalidOperationException("No se encontró el usuario QA.");
            return await users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider);
        }

        static HttpClient NewClient() => new(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true
        });

        async Task<(string Html, string Path)> Get(HttpClient client, string baseUrl, string path)
        {
            var response = await client.GetAsync(baseUrl + path);
            var html = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"GET {path} respondió {(int)response.StatusCode}.");
            return (html, response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty);
        }

        async Task<(string Html, string Path, HttpStatusCode Status)> Post(
            HttpClient client, string baseUrl, string path, Dictionary<string, string> values, string pagePath = null)
        {
            var (page, _) = await Get(client, baseUrl, pagePath ?? path.Split('?')[0]);
            var token = WebUtility.HtmlDecode(Regex.Match(page,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"No se encontró token antifalsificación en {pagePath ?? path}.");
            values["__RequestVerificationToken"] = token;
            var response = await client.PostAsync(baseUrl + path, new FormUrlEncodedContent(values));
            var html = await response.Content.ReadAsStringAsync();
            return (html, response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty, response.StatusCode);
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        try
        {
            var baseUrl = $"http://127.0.0.1:{port}";
            using var admin = NewClient();
            var ready = false;
            for (var attempt = 0; attempt < 45; attempt++)
            {
                if (server.HasExited)
                    throw new InvalidOperationException("El servidor de prueba terminó: " + await stdout + await stderr);
                try
                {
                    await Get(admin, baseUrl, "/Identity/Account/Login");
                    ready = true;
                    break;
                }
                catch (HttpRequestException) { await Task.Delay(1000); }
            }
            if (!ready) throw new InvalidOperationException("Petmor no inició para las pruebas de 2FA.");

            var before2fa = await Post(admin, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            Assert(before2fa.Html.Contains("2FA/Authy") && before2fa.Html.Contains("Cerrar Sesión"),
                "La barra autenticada debe mostrar los accesos 2FA/Authy y Cerrar Sesión.");
            Assert(!before2fa.Html.Contains("Código Authy"), "Una cuenta sin 2FA no debe pasar por el desafío Authy.");

            var (setupPage, setupPath) = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(setupPath.EndsWith("/Identity/Account/Manage/TwoFactorAuth", StringComparison.OrdinalIgnoreCase),
                "El usuario autenticado debe acceder a la configuración 2FA.");
            Assert(setupPage.Contains("Configurar Authy") && setupPage.Contains("otpauth"),
                "La configuración debe explicar Authy y ofrecer un QR TOTP.");
            Assert(Regex.IsMatch(setupPage, "id=\\\"sharedKey\\\"[^>]*value=\\\"[A-Z2-7 ]+\\\"", RegexOptions.IgnoreCase),
                "La clave manual debe estar disponible durante el enrolamiento.");

            var csrfDenied = await admin.PostAsync(baseUrl + "/Identity/Account/Manage/TwoFactorAuth?handler=Enable",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Code"] = "123456" }));
            Assert(csrfDenied.StatusCode == HttpStatusCode.BadRequest,
                "El enrolamiento debe rechazar POST sin token antifalsificación.");

            var enable = await Post(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth?handler=Enable",
                new() { ["Input.Code"] = await AuthenticatorCode() }, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(enable.Status == HttpStatusCode.OK && enable.Html.Contains("2FA está activo"),
                "Un código TOTP válido debe activar 2FA.");
            Assert(enable.Html.Contains("Se muestran una sola vez") &&
                   Regex.Matches(enable.Html, "class=\\\"col-6 col-md-3\\\"").Count == 10,
                "La activación debe mostrar diez códigos de recuperación de un solo uso.");
            var recoveryCode = WebUtility.HtmlDecode(Regex.Match(enable.Html,
                "class=\\\"col-6 col-md-3\\\">([^<]+)</div>").Groups[1].Value).Trim();
            Assert(!string.IsNullOrWhiteSpace(recoveryCode), "No se emitió un código de recuperación para la prueba.");

            var afterEnable = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(afterEnable.Html.Contains("2FA está activo") && !afterEnable.Html.Contains("id=\"sharedKey\""),
                "Después del enrolamiento no se debe volver a mostrar el secreto Authy.");
            Assert(!afterEnable.Html.Contains(recoveryCode), "Los códigos de recuperación no deben volver a mostrarse.");

            using var challenge = NewClient();
            var loginChallenge = await Post(challenge, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            Assert(loginChallenge.Path.EndsWith("/Identity/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) &&
                   loginChallenge.Html.Contains("Código de Authy"),
                "Una contraseña válida con 2FA habilitado debe dirigir al desafío Authy.");

            var invalidCode = await Post(challenge, baseUrl, "/Identity/Account/LoginWith2fa",
                new() { ["Input.TwoFactorCode"] = "000000" });
            Assert(invalidCode.Path.EndsWith("/Identity/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) &&
                   invalidCode.Html.Contains("Código inválido"),
                "El desafío debe rechazar códigos Authy inválidos.");

            var validCode = await Post(challenge, baseUrl, "/Identity/Account/LoginWith2fa",
                new() { ["Input.TwoFactorCode"] = await AuthenticatorCode() });
            Assert(validCode.Path == "/" && validCode.Html.Contains("Mascotas activas"),
                "Un código Authy válido debe completar el inicio de sesión.");

            using var recoveryLogin = NewClient();
            await Post(recoveryLogin, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            var recovery = await Post(recoveryLogin, baseUrl, "/Identity/Account/LoginWithRecoveryCode",
                new() { ["Input.RecoveryCode"] = recoveryCode });
            Assert(recovery.Path == "/" && recovery.Html.Contains("Mascotas activas"),
                "Un código de recuperación válido debe permitir ingresar una vez.");

            using var reusedRecovery = NewClient();
            await Post(reusedRecovery, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            var reused = await Post(reusedRecovery, baseUrl, "/Identity/Account/LoginWithRecoveryCode",
                new() { ["Input.RecoveryCode"] = recoveryCode });
            Assert(reused.Path.EndsWith("/Identity/Account/LoginWithRecoveryCode", StringComparison.OrdinalIgnoreCase) &&
                   reused.Html.Contains("inválido"),
                "Un código de recuperación ya utilizado debe rechazarse.");

            var disable = await Post(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth?handler=Disable",
                new() { ["DisableInput.Code"] = await AuthenticatorCode() }, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(disable.Status == HttpStatusCode.OK && disable.Html.Contains("desactivada"),
                "Desactivar 2FA debe exigir y aceptar el código Authy vigente.");

            using var passwordOnly = NewClient();
            var loginAfterDisable = await Post(passwordOnly, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            Assert(loginAfterDisable.Path == "/" && loginAfterDisable.Html.Contains("Mascotas activas"),
                "Después de desactivar 2FA, el usuario debe volver al inicio de sesión normal.");

            Console.WriteLine("PASS AUTHY 2FA: acceso opt-in, enrolamiento Authy, QR/clave, CSRF, challenge, recovery de un uso y desactivación.");
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            await using var db = new ApplicationDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
