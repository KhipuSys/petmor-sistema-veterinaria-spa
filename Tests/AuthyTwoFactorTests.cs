using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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
        static string GenerateTotp(string formattedKey)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var normalizedKey = formattedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            var secret = new List<byte>();
            uint buffer = 0;
            var bitsInBuffer = 0;
            foreach (var character in normalizedKey)
            {
                var value = alphabet.IndexOf(character);
                if (value < 0) throw new InvalidOperationException("La clave manual no es Base32 válida.");
                buffer = (buffer << 5) | (uint)value;
                bitsInBuffer += 5;
                if (bitsInBuffer >= 8)
                {
                    bitsInBuffer -= 8;
                    secret.Add((byte)(buffer >> bitsInBuffer));
                    buffer &= (1u << bitsInBuffer) - 1;
                }
            }

            var counter = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            var counterBytes = new byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64BigEndian(counterBytes, counter);
            using var hmac = new HMACSHA1(secret.ToArray());
            var hash = hmac.ComputeHash(counterBytes);
            var offset = hash[^1] & 0x0f;
            var binary = ((hash[offset] & 0x7f) << 24) |
                         ((hash[offset + 1] & 0xff) << 16) |
                         ((hash[offset + 2] & 0xff) << 8) |
                         (hash[offset + 3] & 0xff);
            return (binary % 1_000_000).ToString("D6");
        }

        static HttpClient NewClient(CookieContainer cookies = null, bool allowAutoRedirect = true) => new(new HttpClientHandler
        {
            CookieContainer = cookies ?? new CookieContainer(),
            AllowAutoRedirect = allowAutoRedirect
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
            return await PostWithPage(client, baseUrl, path, values, page);
        }

        async Task<(string Html, string Path, HttpStatusCode Status)> PostWithPage(
            HttpClient client, string baseUrl, string path, Dictionary<string, string> values, string page)
        {
            var token = WebUtility.HtmlDecode(Regex.Match(page,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"No se encontró token antifalsificación en {path}.");
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
            Assert(setupPage.Contains("Configurar Authy") && setupPage.Contains("Código QR para configurar Authy en Petmor"),
                "La configuración debe explicar Authy y ofrecer un QR TOTP.");
            Assert(Regex.IsMatch(setupPage, "id=\\\"sharedKey\\\"[^>]*value=\\\"[A-Z2-7 ]+\\\"", RegexOptions.IgnoreCase),
                "La clave manual debe estar disponible durante el enrolamiento.");
            var setupKey = WebUtility.HtmlDecode(Regex.Match(setupPage,
                "id=\\\"sharedKey\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value);
            Assert(!string.IsNullOrWhiteSpace(setupKey), "No se generó la clave manual para Authy.");
            Assert(setupPage.Contains("name=\"Input.Code\"", StringComparison.Ordinal),
                "El campo de confirmación debe enviar Input.Code al handler de activación.");

            var csrfCookies = new CookieContainer();
            using var csrfClient = NewClient(csrfCookies);
            await Post(csrfClient, baseUrl, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password });
            await Get(csrfClient, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            using var noCsrfRedirects = NewClient(csrfCookies, allowAutoRedirect: false);
            var csrfDenied = await noCsrfRedirects.PostAsync(baseUrl + "/Identity/Account/Manage/TwoFactorAuth?handler=Enable",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Code"] = "123456" }));
            Assert(csrfDenied.StatusCode == HttpStatusCode.BadRequest,
                "El enrolamiento debe rechazar POST sin token antifalsificación.");

            var freshSetup = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            var enableCode = GenerateTotp(setupKey);
            var enable = await PostWithPage(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth?handler=Enable",
                new() { ["Input.Code"] = enableCode }, freshSetup.Html);
            Assert(enable.Status == HttpStatusCode.OK && enable.Html.Contains("2FA está activo"),
                "Un código TOTP válido debe activar 2FA.");
            Assert(enable.Html.Contains("Se muestran una sola vez") &&
                   Regex.Matches(enable.Html, "class=\\\"col-6 col-md-3 recovery-code\\\"").Count == 10,
                "La activación debe mostrar diez códigos de recuperación de un solo uso.");
            var recoveryCode = WebUtility.HtmlDecode(Regex.Match(enable.Html,
                "class=\\\"col-6 col-md-3 recovery-code\\\">([^<]+)</div>").Groups[1].Value).Trim();
            Assert(!string.IsNullOrWhiteSpace(recoveryCode), "No se emitió un código de recuperación para la prueba.");

            await using (var db = new ApplicationDbContext(options))
            {
                var userId = await db.Users.Where(user => user.Email == "admin@petmor.pe").Select(user => user.Id).SingleAsync();
                var persistedTokens = await db.UserTokens.Where(token => token.UserId == userId)
                    .Select(token => new { token.LoginProvider, token.Name, token.Value }).ToListAsync();
                var authToken = persistedTokens.SingleOrDefault(token =>
                    token.Name.Contains("AuthenticatorKey", StringComparison.OrdinalIgnoreCase));
                if (authToken == null)
                    throw new InvalidOperationException("No se encontró el token Identity del autenticador: " +
                        string.Join(", ", persistedTokens.Select(token => $"{token.LoginProvider}/{token.Name}")));
                var persistedAuthenticatorKey = authToken.Value;
                Assert(persistedAuthenticatorKey.StartsWith("dp:v1:", StringComparison.Ordinal) &&
                       !persistedAuthenticatorKey.Contains(setupKey.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase),
                    "La clave Authy debe almacenarse cifrada en el token store de Identity.");
            }

            var afterEnable = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(afterEnable.Html.Contains("2FA está activo") && !afterEnable.Html.Contains("id=\"sharedKey\""),
                "Después del enrolamiento no se debe volver a mostrar el secreto Authy.");
            Assert(!afterEnable.Html.Contains(recoveryCode), "Los códigos de recuperación no deben volver a mostrarse.");

            using var challenge = NewClient();
            var unsafeReturnUrl = "/Identity/Account/Login?returnUrl=https%3A%2F%2Fevil.example";
            var loginChallenge = await Post(challenge, baseUrl, unsafeReturnUrl,
                new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = password }, unsafeReturnUrl);
            Assert(loginChallenge.Path.EndsWith("/Identity/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) &&
                   loginChallenge.Html.Contains("Código de Authy"),
                "Una contraseña válida con 2FA habilitado debe dirigir al desafío Authy.");

            var currentAuthCode = GenerateTotp(setupKey);
            var invalidAuthCode = (currentAuthCode[0] == '0' ? '1' : '0') + currentAuthCode[1..];
            var invalidCode = await Post(challenge, baseUrl, "/Identity/Account/LoginWith2fa",
                new() { ["Input.TwoFactorCode"] = invalidAuthCode });
            var challengeErrorBlock = Regex.Match(invalidCode.Html,
                "<div[^>]*role=\\\"alert\\\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            var challengeVisibleError = WebUtility.HtmlDecode(Regex.Replace(challengeErrorBlock, "<[^>]+>", " ")).Trim();
            Assert(invalidCode.Path.EndsWith("/Identity/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) &&
                   challengeVisibleError.Contains("vencido", StringComparison.OrdinalIgnoreCase),
                "El desafío debe rechazar códigos Authy inválidos.");

            var challengePage = await Get(challenge, baseUrl, "/Identity/Account/LoginWith2fa");
            var validCodeValue = GenerateTotp(setupKey);
            var validCode = await PostWithPage(challenge, baseUrl, "/Identity/Account/LoginWith2fa",
                new() { ["Input.TwoFactorCode"] = validCodeValue }, challengePage.Html);
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
                   reused.Html.Contains("utilizado", StringComparison.OrdinalIgnoreCase),
                "Un código de recuperación ya utilizado debe rechazarse.");

            var disablePage = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            var disableCode = GenerateTotp(setupKey);
            var disable = await PostWithPage(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth?handler=Disable",
                new() { ["DisableInput.Code"] = disableCode }, disablePage.Html);
            Assert(disable.Status == HttpStatusCode.OK && disable.Html.Contains("desactivada"),
                "Desactivar 2FA debe exigir y aceptar el código Authy vigente.");

            string authenticatorTokenName;
            string authenticatorLoginProvider;
            await using (var db = new ApplicationDbContext(options))
            {
                var userId = await db.Users.Where(user => user.Email == "admin@petmor.pe").Select(user => user.Id).SingleAsync();
                var authenticatorToken = (await db.UserTokens.Where(token => token.UserId == userId).ToListAsync())
                    .Single(token => token.Name.Contains("AuthenticatorKey", StringComparison.OrdinalIgnoreCase));
                authenticatorTokenName = authenticatorToken.Name;
                authenticatorLoginProvider = authenticatorToken.LoginProvider;
                authenticatorToken.Value = setupKey.Replace(" ", string.Empty, StringComparison.Ordinal);
                await db.SaveChangesAsync();
            }

            var migratedSetup = await Get(admin, baseUrl, "/Identity/Account/Manage/TwoFactorAuth");
            Assert(migratedSetup.Html.Contains("Configurar Authy"), "La clave Identity previa debe seguir permitiendo el enrolamiento.");
            await using (var db = new ApplicationDbContext(options))
            {
                var userId = await db.Users.Where(user => user.Email == "admin@petmor.pe").Select(user => user.Id).SingleAsync();
                var migratedKey = await db.UserTokens.Where(token => token.UserId == userId &&
                        token.LoginProvider == authenticatorLoginProvider && token.Name == authenticatorTokenName)
                    .Select(token => token.Value).SingleAsync();
                Assert(migratedKey.StartsWith("dp:v1:", StringComparison.Ordinal),
                    "Una clave Identity anterior debe migrarse a almacenamiento protegido al consultarse.");
            }

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
