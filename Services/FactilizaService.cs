using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration; 
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;

namespace Petmor.Web.Services
{
    public class FactilizaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _configurationToken;
        private readonly ApplicationDbContext _db;
        private readonly IDataProtector _tokenProtector;
        private readonly IDataProtector _consultaTokenProtector;

        public FactilizaService(HttpClient httpClient, IConfiguration configuration,
            ApplicationDbContext db = null, IDataProtectionProvider dataProtectionProvider = null)
        {
            _httpClient = httpClient;
            _configurationToken = configuration["FactilizaSettings:Token"] ?? string.Empty;
            _db = db;
            _tokenProtector = dataProtectionProvider?.CreateProtector("Petmor.FactilizaToken.v1");
            _consultaTokenProtector = dataProtectionProvider?.CreateProtector("Petmor.FactilizaConsultaToken.v1");

            _httpClient.BaseAddress = new Uri("https://api.factiliza.com/v1/");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<bool> EmitirBoletaAsync(string numeroDocumento, string clienteNombre, decimal total, decimal igv)
            => (await EmitirBoletaDetalladaAsync(numeroDocumento, clienteNombre, total, igv)).Enviada;

        public async Task<ResultadoEmision> EmitirBoletaDetalladaAsync(string numeroDocumento, string clienteNombre, decimal total, decimal igv)
        {
            try
            {
                var token = await ObtenerTokenFacturacionAsync();
                if (string.IsNullOrWhiteSpace(token))
                    return new ResultadoEmision(false, "No se configuró el token actual de Factiliza para facturación.");
                var payload = new
                {
                    tipo_comprobante = "03", // 03 = Boleta de Venta
                    serie = "B001",
                    cliente = new
                    {
                        tipo_documento = numeroDocumento?.Length == 11 ? "6" : "1", // RUC / DNI
                        numero = numeroDocumento,
                        razon_social = clienteNombre
                    },
                    total = total,
                    igv = igv,
                    moneda = "PEN"
                };

                using var espera = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var request = new HttpRequestMessage(HttpMethod.Post, "comprobantes")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, espera.Token);
                var body = await response.Content.ReadAsStringAsync(espera.Token);
                body = RedactarCredencial(body, token);
                var message = $"HTTP {(int)response.StatusCode}: {body}";
                return new ResultadoEmision(response.IsSuccessStatusCode, message[..Math.Min(4000, message.Length)]);
            }
            catch
            {
                // Manejo de contingencia offline si la API no responde
                return new ResultadoEmision(false, "No se confirmó la respuesta del proveedor. Conciliar antes de volver a emitir.");
            }
        }

        public async Task<ConsultaDocumentoResult> ConsultarDocumentoAsync(string documento)
        {
            documento = documento?.Trim() ?? string.Empty;

            if ((documento.Length != 8 && documento.Length != 11) || documento.Any(c => c < '0' || c > '9'))
                return ConsultaDocumentoResult.Fallo("Ingrese un DNI de 8 dígitos o RUC de 11 dígitos.");

            var token = await ObtenerTokenConsultaAsync();
            if (string.IsNullOrWhiteSpace(token))
                return ConsultaDocumentoResult.Fallo("No se configuró el token de Factiliza.");

            try
            {
                var tipo = documento.Length == 8 ? "dni" : "ruc";
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{tipo}/info/{documento}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var detalle = RedactarCredencial(await LeerMensajeErrorAsync(response), token);
                    var mensaje = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                            => "El token de Factiliza no está autorizado o ya no es válido.",
                        System.Net.HttpStatusCode.TooManyRequests
                            => "Se alcanzó el límite de consultas de Factiliza.",
                        System.Net.HttpStatusCode.NotFound
                            => "No se encontró información para el documento ingresado.",
                        _ => string.IsNullOrWhiteSpace(detalle)
                            ? $"Factiliza respondió con HTTP {(int)response.StatusCode}."
                            : $"Factiliza: {detalle}"
                    };
                    return ConsultaDocumentoResult.Fallo(mensaje);
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var json = await JsonDocument.ParseAsync(stream);

                var root = json.RootElement;
                var data = TryGetProperty(root, "data", out var dataElement) ? dataElement : root;

                var nombre = documento.Length == 8
                    ? FirstString(data, "nombre_completo", "nombre", "nombres")
                    : FirstString(data, "nombre_o_razon_social", "razon_social", "nombre", "nombre_completo");

                if (string.IsNullOrWhiteSpace(nombre))
                    return ConsultaDocumentoResult.Fallo("La API respondió, pero no devolvió el nombre del cliente.");

                var direccion = FirstString(data, "direccion_completa", "direccion", "domicilio_fiscal");
                var nombres = documento.Length == 8 ? FirstString(data, "nombres", "prenombres") : "";
                var apellidos = documento.Length == 8 ? string.Join(" ", new[] {
                    FirstString(data, "apellido_paterno", "apellidoPaterno"),
                    FirstString(data, "apellido_materno", "apellidoMaterno")
                }.Where(a => !string.IsNullOrWhiteSpace(a))) : "";
                if (documento.Length == 8 && !string.IsNullOrWhiteSpace(nombres) && !string.IsNullOrWhiteSpace(apellidos))
                    nombre = $"{nombres} {apellidos}";

                return new ConsultaDocumentoResult
                {
                    Exito = true,
                    Documento = documento,
                    Nombre = nombre,
                    Nombres = nombres,
                    Apellidos = apellidos,
                    Direccion = direccion
                };
            }
            catch
            {
                return ConsultaDocumentoResult.Fallo("No se pudo consultar la API de documentos.");
            }
        }

        private static string FirstString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
                    return property.GetString();
            }

            return string.Empty;
        }

        private async Task<string> ObtenerTokenFacturacionAsync()
        {
            if (_db != null && _tokenProtector != null)
            {
                var cifrado = await _db.Configuraciones.AsNoTracking()
                    .Select(x => x.FactilizaTokenCifrado)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(cifrado))
                {
                    try { return _tokenProtector.Unprotect(cifrado); }
                    catch (Exception) { return string.Empty; }
                }
            }
            return _configurationToken;
        }

        private async Task<string> ObtenerTokenConsultaAsync()
        {
            if (_db != null)
            {
                var tokens = await _db.Configuraciones.AsNoTracking()
                    .Select(configuracion => new
                    {
                        configuracion.ConsultaDniRucTokenCifrado,
                        configuracion.FactilizaTokenCifrado
                    })
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(tokens?.ConsultaDniRucTokenCifrado))
                {
                    if (_consultaTokenProtector == null) return string.Empty;
                    try { return _consultaTokenProtector.Unprotect(tokens.ConsultaDniRucTokenCifrado); }
                    catch (System.Security.Cryptography.CryptographicException) { return string.Empty; }
                }

                if (!string.IsNullOrWhiteSpace(tokens?.FactilizaTokenCifrado))
                {
                    if (_tokenProtector == null) return string.Empty;
                    try { return _tokenProtector.Unprotect(tokens.FactilizaTokenCifrado); }
                    catch (System.Security.Cryptography.CryptographicException) { return string.Empty; }
                }
            }

            return _configurationToken;
        }

        private static string RedactarCredencial(string mensaje, string token)
        {
            if (string.IsNullOrEmpty(mensaje) || string.IsNullOrEmpty(token))
                return mensaje;

            var redacted = mensaje.Replace(token, "[credencial omitida]", StringComparison.Ordinal);
            var encodedToken = Uri.EscapeDataString(token);
            return encodedToken == token
                ? redacted
                : redacted.Replace(encodedToken, "[credencial omitida]", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> LeerMensajeErrorAsync(HttpResponseMessage response)
        {
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var json = await JsonDocument.ParseAsync(stream);
                var root = json.RootElement;
                var data = TryGetProperty(root, "data", out var dataElement) ? dataElement : root;
                return FirstString(root, "message", "mensaje", "error")
                    ?? FirstString(data, "message", "mensaje", "error");
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
                return true;

            property = default;
            return false;
        }
    }

    public record ResultadoEmision(bool Enviada, string Respuesta);

    public class ConsultaDocumentoResult
    {
        public bool Exito { get; set; }
        public string Documento { get; set; }
        public string Nombre { get; set; }
        public string Nombres { get; set; }
        public string Apellidos { get; set; }
        public string Direccion { get; set; }
        public string Mensaje { get; set; }

        public static ConsultaDocumentoResult Fallo(string mensaje)
        {
            return new ConsultaDocumentoResult { Exito = false, Mensaje = mensaje };
        }
    }
}
