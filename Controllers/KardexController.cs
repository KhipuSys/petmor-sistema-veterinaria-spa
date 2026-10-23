using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;
using Petmor.Web.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Petmor.Web.Controllers
{
    [Authorize]
    public class KardexController : Controller
    {
        private readonly ApplicationDbContext _context;

        public KardexController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Kardex/Index
        public async Task<IActionResult> Index(int? productoId, DateTime? desde, DateTime? hasta)
        {
            ViewBag.Productos = new SelectList(
                _context.Productos.Where(p => !p.EsServicio).OrderBy(p => p.Nombre).Select(p => new { p.Id, Nombre = p.Nombre + " | " + (p.CodigoBarras ?? "Sin código") + (p.Estado ? "" : " (inactivo)") }),
                "Id", "Nombre");
            ViewBag.ProductoSeleccionado = productoId;
            ViewBag.Desde = desde?.ToString("yyyy-MM-dd");
            ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd");

            var query = _context.MovimientosInventario
                .Include(m => m.Producto)
                .AsQueryable();

            if (productoId.HasValue)
            {
                var producto = await _context.Productos.FirstOrDefaultAsync(p => p.Id == productoId && !p.EsServicio);
                if (producto == null) return RedirectToAction(nameof(Index));
                ViewBag.ProductoObjeto = producto;
                query = query.Where(m => m.ProductoId == productoId.Value);
            }
            else
            {
                ViewBag.StockGeneral = await _context.ExistenciasSucursales.Where(s => s.Producto.Estado && !s.Producto.EsServicio).SumAsync(s => s.Cantidad);
            }

            if (desde.HasValue)
                query = query.Where(m => m.Fecha >= desde.Value.Date);

            if (hasta.HasValue)
                query = query.Where(m => m.Fecha < hasta.Value.Date.AddDays(1));

            var movimientos = await query.OrderByDescending(m => m.Fecha).ToListAsync();
            return View(movimientos);
        }

        // GET: Kardex/Ajuste
        [Authorize(Roles = "Administrador")]
        public IActionResult Ajuste()
        {
            TempData["Error"] = "Los ajustes de stock se gestionan desde Editar producto.";
            return RedirectToAction("Index", "Productos");
        }

        // POST: Kardex/Ajuste
        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(ValueCountLimit = 2100)]
        public IActionResult Ajuste(int productoId, string tipoAjuste, int cantidad, string motivo, List<string> lecturas = null, Guid? operacionId = null, bool excepcionManual = false)
        {
            TempData["Error"] = "Los ajustes de stock se gestionan desde Editar producto.";
            if (productoId > 0) return RedirectToAction("Edit", "Productos", new { id = productoId });
            return RedirectToAction("Index", "Productos");
        }

        // API: Obtener stock de un producto (para el formulario de ajuste)
        [HttpGet]
        public IActionResult GetStock(int id)
        {
            var producto = _context.Productos.Find(id);
            if (producto == null || producto.EsServicio) return Json(new { stock = 0, nombre = "" });
            return Json(new { stock = producto.Stock, nombre = producto.Nombre });
        }
    }
}
