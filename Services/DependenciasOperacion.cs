using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;

namespace MiniMarket.Web.Services;

public static class DependenciasOperacion
{
    public static async Task<bool> ClientePendiente(ApplicationDbContext db, int id) => await db.Atenciones.IgnoreQueryFilters().AnyAsync(a =>
        a.Mascota.ClienteId == id && a.Estado != EstadoAtencion.Cancelada && (a.VentaId == null || a.Estado != EstadoAtencion.Entregada)) ||
        await db.ReservasProductos.IgnoreQueryFilters().AnyAsync(x => x.ClienteId == id && x.Estado == "Pendiente");
    public static async Task<bool> ProductoPendiente(ApplicationDbContext db, int id) => await db.CargosAtencion.IgnoreQueryFilters().AnyAsync(c =>
        c.ProductoId == id && c.Atencion.Estado != EstadoAtencion.Cancelada && c.Atencion.VentaId == null) ||
        await db.ReservasProductos.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id && x.Estado == "Pendiente");
}
