using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ConfiguracionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly IDataProtector _factilizaProtector;
        private readonly IConfiguration _configuration;

        public ConfiguracionController(ApplicationDbContext context, IWebHostEnvironment hostEnvironment,
            IDataProtectionProvider dataProtectionProvider, IConfiguration configuration)
        {
            _context = context;
            _hostEnvironment = hostEnvironment;
            _factilizaProtector = dataProtectionProvider.CreateProtector("Petmor.FactilizaToken.v1");
            _configuration = configuration;
        }

        // GET: Muestra el formulario con los datos actuales
        [HttpGet]
        public async Task<IActionResult> HoraSistema()
        {
            var origen = await _context.Database.SqlQueryRaw<string>("SELECT ZonaOrigen AS Value FROM AjustesHorario WHERE Nombre='PeruV1'").SingleOrDefaultAsync();
            return Json(new { zona = "America/Lima", desfase = "UTC-05:00",
                ahoraPeru = HoraPeru.Ahora.ToString("yyyy-MM-dd HH:mm:ss"),
                utc = DateTimeOffset.UtcNow, zonaServidor = TimeZoneInfo.Local.Id,
                origenFechasAnteriores = origen });
        }

        public async Task<IActionResult> Index()
        {
            var configEntidad = await _context.Configuraciones.FirstOrDefaultAsync();
            var model = new ConfiguracionViewModel();

            if (configEntidad != null)
            {
                // Cargar datos de la BD al formulario
                model.NombreEmpresa = configEntidad.NombreEmpresa;
                model.Ruc = configEntidad.Ruc;
                model.Direccion = configEntidad.Direccion;
                model.Telefono = configEntidad.Telefono;
                model.EmailContacto = configEntidad.EmailContacto;
                model.IgvPorcentaje = configEntidad.IgvPorcentaje;
                model.MonedaSimbolo = configEntidad.MonedaSimbolo;
                model.CurrentLogoUrl = configEntidad.LogoUrl;
                model.FactilizaTokenConfigurado = !string.IsNullOrWhiteSpace(configEntidad.FactilizaTokenCifrado)
                    || !string.IsNullOrWhiteSpace(_configuration["FactilizaSettings:Token"]);
            }
            else
            {
                // Valores por defecto si es la primera vez
                model.NombreEmpresa = "Petmor";
                model.Ruc = "00000000000";
                model.IgvPorcentaje = 18;
                model.MonedaSimbolo = "S/.";
                model.FactilizaTokenConfigurado = !string.IsNullOrWhiteSpace(_configuration["FactilizaSettings:Token"]);
            }

            return View(model);
        }

        // POST: Guarda los cambios
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)]
        public async Task<IActionResult> Guardar(ConfiguracionViewModel model)
        {
            string extension = null;
            try { extension = await MiniMarket.Web.Services.ImagenSubida.Extension(model.LogoUpload); }
            catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(model.LogoUpload), ex.Message); }
            if (ModelState.IsValid)
            {
                var configEntidad = await _context.Configuraciones.FirstOrDefaultAsync();
                var logoAnterior = configEntidad?.LogoUrl;

                // Procesamiento del archivo de logo
                string uniqueFileName = configEntidad?.LogoUrl; // Mantener existente por defecto
                
                if (model.LogoUpload != null)
                {
                    string uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, "images", "logos");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    uniqueFileName = Guid.NewGuid().ToString("N") + extension;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.LogoUpload.CopyToAsync(fileStream);
                    }
                    uniqueFileName = "/images/logos/" + uniqueFileName;
                }

                if (configEntidad == null)
                {
                    // Si no existe, CREAMOS uno nuevo
                    configEntidad = new Configuracion
                    {
                        NombreEmpresa = model.NombreEmpresa,
                        Ruc = model.Ruc,
                        Direccion = model.Direccion,
                        Telefono = model.Telefono,
                        EmailContacto = model.EmailContacto,
                        IgvPorcentaje = model.IgvPorcentaje,
                        MonedaSimbolo = model.MonedaSimbolo,
                        LogoUrl = uniqueFileName,
                        FactilizaTokenCifrado = CifrarToken(model.FactilizaToken)
                    };
                    _context.Configuraciones.Add(configEntidad);
                }
                else
                {
                    // Si ya existe, ACTUALIZAMOS los datos
                    configEntidad.NombreEmpresa = model.NombreEmpresa;
                    configEntidad.Ruc = model.Ruc;
                    configEntidad.Direccion = model.Direccion;
                    configEntidad.Telefono = model.Telefono;
                    configEntidad.EmailContacto = model.EmailContacto;
                    configEntidad.IgvPorcentaje = model.IgvPorcentaje;
                    configEntidad.MonedaSimbolo = model.MonedaSimbolo;
                    if (!string.IsNullOrWhiteSpace(model.FactilizaToken))
                        configEntidad.FactilizaTokenCifrado = CifrarToken(model.FactilizaToken);
                    if (model.LogoUpload != null)
                    {
                        configEntidad.LogoUrl = uniqueFileName;
                    }
                    
                    _context.Configuraciones.Update(configEntidad);
                }

                await _context.SaveChangesAsync();
                if (model.LogoUpload != null)
                    MiniMarket.Web.Services.ImagenSubida.BorrarAnterior(_hostEnvironment.WebRootPath, logoAnterior, "images/logos", HttpContext.RequestServices.GetRequiredService<ILogger<ConfiguracionController>>());
                
                TempData["MensajeExito"] = "¡Configuración guardada correctamente en la Base de Datos!";
                if (!string.IsNullOrWhiteSpace(model.FactilizaToken))
                    TempData["MensajeExito"] = "¡Configuración y token de Factiliza guardados correctamente!";
                // Actualizar la URL del logo visualizado
                model.CurrentLogoUrl = configEntidad.LogoUrl;
                return RedirectToAction(nameof(Index));
            }

            model.CurrentLogoUrl = (await _context.Configuraciones.AsNoTracking().FirstOrDefaultAsync())?.LogoUrl;
            return View("Index", model);
        }

        private string CifrarToken(string token)
            => string.IsNullOrWhiteSpace(token) ? null : _factilizaProtector.Protect(token.Trim());
    }
}
