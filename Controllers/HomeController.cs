using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;
using Petmor.Web.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Petmor.Web.Controllers
{
    [Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? fecha)
        {
            var hoyInicio = (fecha ?? HoraPeru.Hoy).Date;
            var hoyFin = hoyInicio.AddDays(1);
            ViewBag.FechaAgenda = hoyInicio;
            ViewBag.Mascotas = await _context.Mascotas.CountAsync(x => x.Activa);
            ViewBag.Pendientes = await _context.Atenciones.CountAsync(x => x.VentaId == null && x.Cargos.Any() && (x.Estado == EstadoAtencion.Lista || x.Estado == EstadoAtencion.Entregada));
            ViewBag.StockBajo = await _context.Productos.CountAsync(x => x.Estado && !x.EsServicio && x.Existencias.Sum(s => s.Cantidad) <= x.StockMinimo);
            ViewBag.SpaListos = await _context.Atenciones.CountAsync(x => x.Area == AreaAtencion.Spa && x.Estado == EstadoAtencion.Lista);
            ViewBag.Preventivos = await _context.PreventivosMascota.Include(x => x.Mascota).Where(x => x.Proxima != null && x.Proxima <= hoyInicio.AddDays(7)).OrderBy(x => x.Proxima).Take(15).ToListAsync();
            ViewBag.AgendaHoy = await _context.Atenciones
                .Include(x => x.Mascota).ThenInclude(x => x.Cliente)
                .Include(x => x.Cargos).ThenInclude(x => x.Producto)
                .Include(x => x.Eventos)
                .Where(x => x.Inicio >= hoyInicio && x.Inicio < hoyFin)
                .AsSplitQuery().OrderBy(x => x.Inicio).ToListAsync();
            var agendaMascotaIds = ((List<Atencion>)ViewBag.AgendaHoy).Select(x => x.MascotaId).Distinct().ToList();
            ViewBag.Fotos = await _context.ArchivosClinicos.AsNoTracking()
                .Where(x => x.EsFoto && agendaMascotaIds.Contains(x.MascotaId))
                .GroupBy(x => x.MascotaId)
                .Select(x => new { MascotaId = x.Key, FotoId = x.OrderByDescending(f => f.Fecha).Select(f => f.Id).First() })
                .ToDictionaryAsync(x => x.MascotaId, x => x.FotoId);
            var perfiles = await _context.UsuarioPerfiles.AsNoTracking().ToDictionaryAsync(x => x.UsuarioId, x => x.NombreCompleto);
            var usuarios = await _context.Users.AsNoTracking().Select(x => new { x.Id, x.UserName }).ToListAsync();
            ViewBag.Responsables = usuarios.ToDictionary(x => x.Id, x => perfiles.TryGetValue(x.Id, out var nombre) && !string.IsNullOrWhiteSpace(nombre) ? nombre : (x.UserName ?? "-").Split('@')[0]);
            ViewBag.MostrarAccionesAgenda = false;
            ViewBag.VerComercial = User.IsInRole("Administrador") || User.IsInRole("Recepcionista");
            if (!(bool)ViewBag.VerComercial) return View(new DashboardViewModel());

            // 1. TARJETAS SUPERIORES
            var ventasHoy = await _context.Ventas
                .Where(v => v.Fecha >= hoyInicio && v.Fecha < hoyFin && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            var totalProductos = await _context.Productos.CountAsync(p => p.Estado == true);
            var totalCategorias = await _context.Categorias.CountAsync();

            var capitalEstimado = await _context.Productos
                .Where(p => p.Estado == true && !p.EsServicio)
                .SumAsync(p => p.Precio * p.Existencias.Sum(s => s.Cantidad));

            // 2. GRÁFICO DE LÍNEAS (Últimos 7 días) — solo ventas activas
            var fechaInicioGrafico = hoyInicio.AddDays(-6);
            var ventasUltimos7Dias = await _context.Ventas
                .Where(v => v.Fecha >= fechaInicioGrafico && v.Fecha < hoyFin && v.Estado == "Activa")
                .GroupBy(v => v.Fecha.Date)
                .Select(g => new { Fecha = g.Key, Total = g.Sum(v => v.Total) })
                .ToListAsync();

            var etiquetasFechas = new List<string>();
            var valoresVentas = new List<decimal>();

            for (int i = 0; i < 7; i++)
            {
                var fechaActual = fechaInicioGrafico.AddDays(i);
                var ventaDia = ventasUltimos7Dias.FirstOrDefault(v => v.Fecha == fechaActual);
                etiquetasFechas.Add(fechaActual.ToString("ddd dd", new CultureInfo("es-ES")));
                valoresVentas.Add(ventaDia?.Total ?? 0);
            }

            // 3. GRÁFICO CIRCULAR (TOP 5 PRODUCTOS) — solo ventas activas
            var topProductos = await _context.DetalleVentas
                .Include(d => d.Producto)
                .Include(d => d.Venta)
                .Where(d => d.Venta.Estado == "Activa" && d.Venta.Fecha >= hoyInicio && d.Venta.Fecha < hoyFin)
                .GroupBy(d => new { d.ProductoId, d.Producto.Nombre })
                .Select(g => new { Producto = g.Key.Nombre, Cantidad = g.Sum(d => d.Cantidad) })
                .OrderByDescending(x => x.Cantidad)
                .Take(5)
                .ToListAsync();

            // 4. ALERTAS STOCK BAJO
            var productosStockBajo = await _context.Productos
                .Where(p => p.Estado == true && !p.EsServicio && p.Existencias.Sum(s => s.Cantidad) <= p.StockMinimo)
                .OrderBy(p => p.Existencias.Sum(s => s.Cantidad))
                .ToListAsync();

            // 5. VENTAS POR CAJERO Y COMPARATIVO MENSUAL
            var inicioMesActual = new DateTime(hoyInicio.Year, hoyInicio.Month, 1);
            var finMesActual = inicioMesActual.AddMonths(1).AddTicks(-1);
            
            var inicioMesAnterior = inicioMesActual.AddMonths(-1);
            var finMesAnterior = inicioMesActual.AddTicks(-1);

            var ventasActual = await _context.Ventas
                .Where(v => v.Fecha >= inicioMesActual && v.Fecha <= finMesActual && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            var ventasAnterior = await _context.Ventas
                .Where(v => v.Fecha >= inicioMesAnterior && v.Fecha <= finMesAnterior && v.Estado == "Activa")
                .SumAsync(v => v.Total);

            decimal crecimiento = 0;
            if (ventasAnterior > 0)
                crecimiento = ((ventasActual - ventasAnterior) / ventasAnterior) * 100;
            else if (ventasActual > 0)
                crecimiento = 100; // Si antes era 0 y ahora hay ventas, 100% de crecimiento

            var ventasCajero = await _context.Ventas
                .Include(v => v.Usuario)
                .Where(v => v.Fecha >= inicioMesActual && v.Fecha <= finMesActual && v.Estado == "Activa")
                .GroupBy(v => v.Usuario.UserName)
                .Select(g => new { Cajero = g.Key ?? "Sistema", Total = g.Sum(x => x.Total) })
                .OrderByDescending(x => x.Total)
                .ToListAsync();

            var modelo = new DashboardViewModel
            {
                VentasHoy = ventasHoy,
                TotalProductos = totalProductos,
                TotalCategorias = totalCategorias,
                CapitalEstimado = capitalEstimado,
                FechasGrafico = etiquetasFechas,
                VentasGrafico = valoresVentas,
                TopProductosNombres = topProductos.Select(x => x.Producto).ToList(),
                TopProductosCantidades = topProductos.Select(x => x.Cantidad).ToList(),
                ProductosStockBajo = productosStockBajo,
                VentasMesActual = ventasActual,
                VentasMesAnterior = ventasAnterior,
                PorcentajeCrecimiento = crecimiento,
                VentasPorCajero = ventasCajero.ToDictionary(k => k.Cajero, v => v.Total)
            };

            return View(modelo);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
