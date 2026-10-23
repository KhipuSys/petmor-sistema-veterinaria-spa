using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers;

[Authorize(Roles = "Administrador,Recepcionista")]
[AutoValidateAntiforgeryToken]
public class ReservasController(ApplicationDbContext db, EmisionVentaService emision) : Controller
{
    public IActionResult Create() => View(new ReservaForm());

    [HttpPost]
    [RequestFormLimits(ValueCountLimit = 2100)]
    public async Task<IActionResult> Create(ReservaForm form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await new ReservaService(db).Crear(form, User.Identity.Name);
                TempData["Exito"] = "Pendiente de pago registrado. Producto separado del stock disponible de esta sede.";
                return RedirectToAction("Cuentas", "Petmor");
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ViewBag.Cliente = await db.Clientes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ClienteId);
        ViewBag.Producto = await db.Productos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ProductoId);
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Cancelar(int id)
    {
        try
        {
            await new ReservaService(db).Cancelar(id, User.Identity.Name);
            TempData["Exito"] = "Reserva cancelada. Las unidades volvieron al stock disponible.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Cuentas", "Petmor");
    }

    [HttpPost]
    public async Task<IActionResult> Cobrar(int id, Guid operacion, string metodoPago, decimal total)
    {
        if (!ModelState.IsValid) return BadRequest();
        var reserva = await db.ReservasProductos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (reserva == null) return NotFound();
        var request = new VentaRequest { ReservaId = id, OperacionId = operacion, ClienteId = reserva.ClienteId,
            MetodoPago = metodoPago, Total = total + PagoReglas.CalcularRecargo(metodoPago, total),
            Recargo = PagoReglas.CalcularRecargo(metodoPago, total),
            Detalles = new() { new DetalleRequest { ProductoId = reserva.ProductoId, Cantidad = reserva.Cantidad } } };
        try
        {
            var (venta, repetida) = await new OperacionVentaService(db).Registrar(request, User.Identity.Name);
            if (!repetida) await emision.Intentar(venta.Id);
            return RedirectToAction("Ticket", "Ventas", new { id = venta.Id });
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; return RedirectToAction("Cuentas", "Petmor"); }
    }
}
