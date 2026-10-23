using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Petmor.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class PrediccionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PrediccionController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            DateTime fechaLimite = HoraPeru.Hoy.AddDays(-30);
            int diasEvaluados = 30;
            int diasProyeccion = 7;

            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .Where(p => p.Estado && !p.EsServicio)
                .ToListAsync();

            var detallesVentas = await _context.DetalleVentas
                .Include(d => d.Venta)
                .Where(d => d.Venta.Fecha >= fechaLimite && d.Venta.Estado == "Activa")
                .ToListAsync();

            var reporte = new List<PrediccionViewModel>();

            foreach (var prod in productos)
            {
                int totalVendido = detallesVentas
                    .Where(d => d.ProductoId == prod.Id)
                    .Sum(d => d.Cantidad);

                decimal promedioDiario = (decimal)totalVendido / diasEvaluados;
                int demandaProyectada = (int)Math.Ceiling(promedioDiario * diasProyeccion);

                int sugerido = (demandaProyectada + prod.StockMinimo) - prod.Stock;
                if (sugerido < 0) sugerido = 0;

                string estado;
                if (prod.Stock <= prod.StockMinimo)
                {
                    estado = "Crítico";
                }
                else if (prod.Stock < (demandaProyectada + prod.StockMinimo))
                {
                    estado = "Bajo";
                }
                else
                {
                    estado = "Óptimo";
                }

                reporte.Add(new PrediccionViewModel
                {
                    ProductoId = prod.Id,
                    CodigoBarras = prod.CodigoBarras,
                    ProductoNombre = prod.Nombre,
                    ImagenUrl = prod.ImagenUrl,
                    CategoriaNombre = prod.Categoria?.Nombre ?? "General",
                    StockActual = prod.Stock,
                    StockMinimo = prod.StockMinimo,
                    CostoUnitario = prod.Costo,
                    PromedioVentaDiaria = Math.Round(promedioDiario, 2),
                    DemandaEstimada7Dias = demandaProyectada,
                    CantidadSugeridaComprar = sugerido,
                    EstadoStock = estado
                });
            }

            reporte = reporte.OrderByDescending(r => r.CantidadSugeridaComprar).ToList();

            return View(reporte);
        }
    }
}
