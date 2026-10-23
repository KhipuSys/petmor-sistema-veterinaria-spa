using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Petmor.Web.Models.ViewModels;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;

namespace Petmor.Web.Controllers
{
    [Authorize(Roles = "Administrador,Recepcionista")]
    public class EtiquetasController(ApplicationDbContext db) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> BuscarProductos(string term, int page = 1)
        {
            term = (term ?? "").Trim(); page = Math.Clamp(page, 1, 10000);
            var rows = await db.Productos.AsNoTracking().Where(p => p.Estado && !p.EsServicio && p.CodigoBarras != null && p.CodigoBarras != "" &&
                (term == "" || p.Nombre.Contains(term) || p.CodigoBarras.Contains(term)))
                .OrderBy(p => p.Nombre).ThenBy(p => p.Id).Skip((page - 1) * 20).Take(21)
                .Select(p => new { id = p.Id, text = p.Nombre, nombre = p.Nombre, codigo = p.CodigoBarras, precio = p.Precio }).ToListAsync();
            return Json(new { results = rows.Take(20), pagination = new { more = rows.Count > 20 } });
        }
        // GET: Etiquetas
        public IActionResult Index()
        {
            return View();
        }

        // POST: Etiquetas/Imprimir
        [HttpPost]
        public async Task<IActionResult> Imprimir(string jsonEtiquetas)
        {
            if (string.IsNullOrEmpty(jsonEtiquetas))
            {
                // Si mandan un array vacío, regresamos al formulario.
                TempData["MensajeError"] = "Para imprimir debes añadir al menos un producto a la lista.";
                return RedirectToAction(nameof(Index));
            }

            List<EtiquetaItemViewModel> items;
            try { items = JsonSerializer.Deserialize<List<EtiquetaItemViewModel>>(jsonEtiquetas); }
            catch (JsonException) { TempData["MensajeError"] = "La lista de etiquetas no es válida."; return RedirectToAction(nameof(Index)); }
            
            if (items == null || items.Count is < 1 or > 100 || items.Any(i => i == null || i.Cantidad is < 1 or > 500) || items.Sum(i => (long)i.Cantidad) > 500)
            {
                TempData["MensajeError"] = "Error procesando las etiquetas. Intente otra vez.";
                return RedirectToAction(nameof(Index));
            }

            var ids = items.Select(i => i.ProductoId).Distinct().ToList();
            var productos = await db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Estado && !p.EsServicio && p.CodigoBarras != null && p.CodigoBarras != "").ToDictionaryAsync(p => p.Id);
            if (productos.Count != ids.Count) { TempData["MensajeError"] = "Un producto no está disponible para imprimir."; return RedirectToAction(nameof(Index)); }
            foreach (var item in items) { var p = productos[item.ProductoId]; item.Nombre = p.Nombre; item.Precio = p.Precio; item.Codigo = p.CodigoBarras; }
            // Los datos de impresión se obtienen del catálogo, no del precio enviado por el navegador.
            return View(items);
        }
    }
}
