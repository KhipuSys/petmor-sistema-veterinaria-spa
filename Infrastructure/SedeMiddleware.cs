using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Infrastructure;

public class SedeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ApplicationDbContext db, SedeActual sede)
    {
        if (http.Request.Path.Equals("/Home/Error", StringComparison.OrdinalIgnoreCase)) { await next(http); return; }
        if (http.User.Identity?.IsAuthenticated == true)
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var asignada = await db.UsuariosSedes.AsNoTracking().Include(x => x.Sucursal).SingleOrDefaultAsync(x => x.UsuarioId == id);
            if (asignada == null || !asignada.Sucursal.Activa)
            {
                http.Response.StatusCode = 403;
                await http.Response.WriteAsync("Tu usuario no tiene una sede activa asignada. Contacta al administrador.");
                return;
            }
            var operaEnAmbas = asignada.SucursalId == 0;
            sede.PuedeCambiar = http.User.IsInRole("Administrador") || operaEnAmbas;
            sede.Disponibles = sede.PuedeCambiar
                ? await db.Sucursales.AsNoTracking().Where(x => x.Activa && x.Id > 0).OrderBy(x => x.Id).ToListAsync()
                : new[] { asignada.Sucursal };
            var elegida = operaEnAmbas ? sede.Disponibles.FirstOrDefault() : asignada.Sucursal;
            if (sede.PuedeCambiar && int.TryParse(http.Request.Cookies["petmor-sede"], out var solicitada))
                elegida = sede.Disponibles.SingleOrDefault(x => x.Id == solicitada) ?? elegida;
            if (elegida == null)
            {
                http.Response.StatusCode = 403;
                await http.Response.WriteAsync("No hay sedes operativas activas. Contacta al administrador.");
                return;
            }
            sede.Id = elegida.Id;
            sede.Nombre = elegida.Nombre;
        }
        await next(http);
    }
}
