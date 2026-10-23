using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Services;

namespace Petmor.Web.Controllers;

[Authorize(Roles = "Administrador,Recepcionista")]
public class VentasController(ApplicationDbContext context, FactilizaService factilizaService,
    ILogger<VentasController> logger, EmisionVentaService emision = null) : Controller
{
    public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta)
    {
        var ventas = context.Ventas.Include(v => v.Usuario).Include(v => v.Cliente).AsQueryable();
        if (!User.IsInRole("Administrador")) ventas = ventas.Where(v => v.Usuario.UserName == User.Identity.Name);
        if (desde.HasValue) ventas = ventas.Where(v => v.Fecha >= desde.Value.Date);
        if (hasta.HasValue) ventas = ventas.Where(v => v.Fecha < hasta.Value.Date.AddDays(1));
        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;
        return View(await ventas.OrderByDescending(v => v.Fecha).ToListAsync());
    }

    public IActionResult Create()
    {
        if (!context.AperturasCaja.Any(c => c.UsuarioId == User.Identity.Name && c.Estado))
        {
            TempData["MensajeError"] = "Debes ABRIR CAJA antes de poder vender.";
            return RedirectToAction("Index", "Caja");
        }
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> BuscarProductos(string term, int page = 1)
    {
        term = (term ?? "").Trim();
        page = Math.Clamp(page, 1, 10000);
        var productos = await context.Productos.AsNoTracking()
            .Where(p => !p.ConCaducidad || (p.FechaVencimiento != null && p.FechaVencimiento >= HoraPeru.Hoy))
            .Where(p => p.Estado && !p.UsoInterno && !p.EsServicio && (term == "" || p.Nombre.Contains(term) || p.CodigoBarras.Contains(term) || p.Codigos.Any(c => c.Codigo.Contains(term))))
            .OrderByDescending(p => term != "" && (p.CodigoBarras == term || p.Codigos.Any(c => c.Codigo == term))).ThenBy(p => p.Nombre).ThenBy(p => p.Id)
            .Skip((page - 1) * 20).Take(21)
            .Select(p => new { id = p.Id, text = p.Nombre + " | Stock: " + p.Existencias.Sum(s => s.Cantidad), nombre = p.Nombre,
                codigo = p.CodigoBarras, precio = p.Precio, stock = p.EsServicio ? 100000 : p.Existencias.Sum(s => s.Cantidad), disabled = !p.EsServicio && p.Existencias.Sum(s => s.Cantidad) <= 0 }).ToListAsync();
        return Json(new { results = productos.Take(20), pagination = new { more = productos.Count > 20 } });
    }

    [HttpGet]
    public async Task<IActionResult> BuscarClientes(string term, int page = 1)
    {
        term = (term ?? "").Trim();
        page = Math.Clamp(page, 1, 10000);
        var clientes = await context.Clientes.AsNoTracking()
            .Where(c => c.Estado && (term == "" || c.Nombre.Contains(term) || c.Documento.Contains(term)))
            .OrderBy(c => c.Nombre).ThenBy(c => c.Id).Skip((page - 1) * 20).Take(21)
            .Select(c => new { id = c.Id, text = c.Nombre + " | " + c.Documento }).ToListAsync();
        return Json(new { results = clientes.Take(20), pagination = new { more = clientes.Count > 20 } });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar([FromBody] VentaRequest request)
    {
        if (!ModelState.IsValid) return Json(new { exito = false, mensaje = "Datos inválidos." });
        Venta venta;
        bool repetida;
        try
        {
            (venta, repetida) = await new OperacionVentaService(context).Registrar(request, User.Identity.Name, User.IsInRole("Administrador"));
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { exito = false, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error procesando operación {Operacion}", request?.OperacionId);
            // Resultado incierto: el cliente conserva el mismo identificador para consultar/reintentar.
            return StatusCode(503, new { exito = false, mensaje = "No se pudo confirmar el resultado. Reintenta la misma operación." });
        }

        // La venta ya está confirmada. Un fallo externo nunca provoca otro cobro ni revierte inventario.
        if (!repetida)
        {
            await (emision ?? new EmisionVentaService(context, factilizaService, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmisionVentaService>.Instance)).Intentar(venta.Id);
        }
        return Json(new { exito = true, mensaje = "Venta registrada. Consulta el estado del comprobante en el historial.",
            idVenta = venta.Id, repetida });
    }

    public async Task<IActionResult> Ticket(int id)
    {
        var venta = await context.Ventas.Include(v => v.Usuario).Include(v => v.Cliente)
            .Include(v => v.Detalles).ThenInclude(d => d.Producto).FirstOrDefaultAsync(v => v.Id == id);
        if (venta == null) return NotFound();
        if (!User.IsInRole("Administrador") && venta.Usuario?.UserName != User.Identity.Name) return Forbid();
        var perfilVendedor = await context.UsuarioPerfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == venta.UsuarioId);
        ViewBag.NombreCajero = perfilVendedor == null ? "Nombre pendiente de registrar" :
            string.Join(" ", new[] {
                perfilVendedor.Nombres.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                perfilVendedor.Apellidos.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            }.Where(parte => !string.IsNullOrWhiteSpace(parte)));
        ViewBag.Configuracion = await context.Configuraciones.FirstOrDefaultAsync() ?? new Configuracion
            { NombreEmpresa = "Petmor", Ruc = "00000000000", Direccion = "Configure el sistema", IgvPorcentaje = 18, MonedaSimbolo = "S/." };
        return View(venta);
    }

    [HttpPost]
    [Authorize(Roles = "Administrador")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Anular(int id, string motivo)
    {
        try
        {
            await new OperacionVentaService(context).Anular(id, User.Identity.Name, motivo);
            return Json(new { exito = true, mensaje = "Venta anulada y stock restituido. Si la caja estaba cerrada, se registró un ajuste posterior; la devolución de dinero y la anulación del comprobante externo deben conciliarse." });
        }
        catch (InvalidOperationException ex) { return Json(new { exito = false, mensaje = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error anulando venta {VentaId}", id);
            return StatusCode(503, new { exito = false, mensaje = "No se pudo confirmar la anulación. Reintenta." });
        }
    }
}

public class VentaRequest
{
    public int? ReservaId { get; set; }
    public int? AtencionId { get; set; }
    public Guid OperacionId { get; set; }
    public int? ClienteId { get; set; }
    public decimal Total { get; set; }
    public decimal Descuento { get; set; }
    public decimal Recargo { get; set; }
    public string MetodoPago { get; set; }
    public List<DetalleRequest> Detalles { get; set; }
}
public class DetalleRequest
{
    public List<string> Lecturas { get; set; }
    public int ProductoId { get; set; }
    public string Nombre { get; set; }
    public decimal Precio { get; set; }
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}
