using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers;

[Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
public class LecturasController(ApplicationDbContext db) : Controller
{
    // Solo consulta; escanear/deshacer/cancelar un borrador no modifica existencias.
    [HttpGet]
    public async Task<IActionResult> Resolver(string codigo)
    {
        try { codigo = CodigosLecturas.Normalizar([codigo]).Single(); }
        catch (InvalidOperationException ex) { return BadRequest(new { mensaje = ex.Message }); }
        Producto p;
        try { p = await CodigosLecturas.ResolverProducto(db, codigo); }
        catch (InvalidOperationException ex) { return Conflict(new { mensaje = ex.Message }); }
        if (p == null) return Json(new { encontrado = false, codigo });
        return Json(new { encontrado = true, codigo, id = p.Id, nombre = p.Nombre, precio = p.Precio, costo = User.IsInRole("Administrador") ? p.Costo : 0,
            stock = p.Stock, activo = p.Estado, servicio = p.EsServicio, interno = p.UsoInterno, vencido = p.Vencido });
    }
}
