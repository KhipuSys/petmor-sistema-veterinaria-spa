using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Petmor.Web.Controllers;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Services;

static class FactilizaSettingsTests
{
    public static async Task Run()
    {
        var database = "Petmor_FactilizaConfigTest_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var dataProtection = new EphemeralDataProtectionProvider();
        var legacyProtector = dataProtection.CreateProtector("Petmor.FactilizaToken.v1");
        var queryProtector = dataProtection.CreateProtector("Petmor.FactilizaConsultaToken.v1");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["FactilizaSettings:Token"] = "legacy-appsettings-token"
        }).Build();

        try
        {
            await using (var db = new ApplicationDbContext(options))
            {
                await ActualizacionComercial.Aplicar(db);
                await ActualizacionVeterinaria.Aplicar(db);
                db.Configuraciones.Add(new Configuracion
                {
                    NombreEmpresa = "Petmor QA",
                    Ruc = "20600000001",
                    Direccion = "Dirección de prueba",
                    Telefono = "999999999",
                    EmailContacto = "admin@petmor.pe",
                    IgvPorcentaje = 18,
                    MonedaSimbolo = "S/.",
                    FactilizaTokenCifrado = legacyProtector.Protect("legacy-billing-token")
                });
                await db.SaveChangesAsync();

                var controller = CreateController(db, dataProtection, configuration);
                var initialModel = (ConfiguracionViewModel)((ViewResult)await controller.Index()).Model;
                Assert(string.IsNullOrEmpty(initialModel.ConsultaDniRucToken),
                    "El GET de Configuración no debe precargar secretos en el formulario.");
                Assert(!initialModel.ConsultaDniRucTokenConfigurado && initialModel.FactilizaTokenLegadoDisponible,
                    "Debe diferenciar un token propio de consulta del token genérico legado.");

                var saveModel = ValidSettings();
                saveModel.ConsultaDniRucToken = "query-token-first";
                await controller.Guardar(saveModel);

                var stored = await db.Configuraciones.SingleAsync();
                var queryCipher = stored.ConsultaDniRucTokenCifrado;
                Assert(!string.IsNullOrWhiteSpace(queryCipher) && queryProtector.Unprotect(queryCipher) == "query-token-first",
                    "El token de consulta debe guardarse protegido con un propósito separado.");
                Assert(legacyProtector.Unprotect(stored.FactilizaTokenCifrado) == "legacy-billing-token",
                    "Guardar el token de consulta no debe modificar el token de facturación existente.");

                var blankModel = ValidSettings();
                blankModel.ConsultaDniRucToken = string.Empty;
                await controller.Guardar(blankModel);
                await db.Entry(stored).ReloadAsync();
                Assert(stored.ConsultaDniRucTokenCifrado == queryCipher,
                    "Enviar vacío debe conservar el token dedicado de consulta.");
                Assert(legacyProtector.Unprotect(stored.FactilizaTokenCifrado) == "legacy-billing-token",
                    "El token de facturación legado debe permanecer intacto al guardar vacío.");

                var configured = (ConfiguracionViewModel)((ViewResult)await controller.Index()).Model;
                Assert(string.IsNullOrEmpty(configured.ConsultaDniRucToken) && configured.ConsultaDniRucTokenConfigurado,
                    "La configuración debe indicar que el token de consulta existe sin devolver su valor.");

                var invalidModel = ValidSettings();
                invalidModel.ConsultaDniRucToken = "never-echo-this-token";
                var invalidController = CreateController(db, dataProtection, configuration);
                invalidController.ModelState.AddModelError(nameof(invalidModel.NombreEmpresa), "Error de prueba.");
                var invalidResult = (ViewResult)await invalidController.Guardar(invalidModel);
                var returnedModel = (ConfiguracionViewModel)invalidResult.Model;
                Assert(string.IsNullOrEmpty(returnedModel.ConsultaDniRucToken),
                    "Una respuesta de validación no debe reflejar el token enviado.");
            }

            await using (var db = new ApplicationDbContext(options))
            {
                var handler = new RecordingHandler();
                using var http = new HttpClient(handler);
                var service = new FactilizaService(http, configuration, db, dataProtection);

                var lookup = await service.ConsultarDocumentoAsync("12345678");
                Assert(lookup.Exito && lookup.Nombre == "CLIENTE DE PRUEBA",
                    "La consulta DNI debe completar el flujo con la respuesta de Factiliza.");
                Assert(handler.AuthorizationTokens.Last() == "query-token-first",
                    "La consulta DNI/RUC debe usar el token dedicado de Configuración.");

                var invoice = await service.EmitirBoletaDetalladaAsync("12345678", "Cliente de prueba", 100, 18);
                Assert(invoice.Enviada && handler.AuthorizationTokens.Last() == "legacy-billing-token",
                    "La emisión debe conservar el token genérico de facturación.");

                var config = await db.Configuraciones.SingleAsync();
                config.ConsultaDniRucTokenCifrado = null;
                await db.SaveChangesAsync();
                var legacyLookup = await service.ConsultarDocumentoAsync("12345678");
                Assert(legacyLookup.Exito && handler.AuthorizationTokens.Last() == "legacy-billing-token",
                    "Instalaciones existentes deben conservar la consulta por el token legado hasta configurar uno propio.");
            }

            Console.WriteLine("PASS FACTILIZA SETTINGS: token de consulta separado/protegido, campo vacío conserva, facturación se mantiene y fallback legado funciona.");
        }
        finally
        {
            await using var db = new ApplicationDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static ConfiguracionViewModel ValidSettings() => new()
    {
        NombreEmpresa = "Petmor QA",
        Ruc = "20600000001",
        Direccion = "Dirección de prueba",
        Telefono = "999999999",
        EmailContacto = "admin@petmor.pe",
        IgvPorcentaje = 18,
        MonedaSimbolo = "S/."
    };

    private static ConfiguracionController CreateController(
        ApplicationDbContext db,
        IDataProtectionProvider dataProtection,
        IConfiguration configuration)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        return new ConfiguracionController(db, new TestWebHostEnvironment(), dataProtection, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Petmor.FactilizaTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> AuthorizationTokens { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationTokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            var content = request.Method == HttpMethod.Get
                ? "{\"data\":{\"nombre_completo\":\"CLIENTE DE PRUEBA\"}}"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
