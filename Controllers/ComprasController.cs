using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ComprasController : Controller
    {
        public ComprasController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
        }

        public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta)
        {
            await Task.CompletedTask;
            TempData["Error"] = "Compras fue retirado. Gestiona el stock desde Productos > Editar.";
            return RedirectToAction("Index", "Productos");
        }

        public async Task<IActionResult> Create(int? productoId, int? cantidad)
        {
            await Task.CompletedTask;
            TempData["Error"] = "Ingreso de mercadería fue retirado. Gestiona el stock desde Productos > Editar.";
            if (productoId.HasValue && productoId.Value > 0) return RedirectToAction("Edit", "Productos", new { id = productoId.Value });
            return RedirectToAction("Index", "Productos");
        }

        public async Task<IActionResult> Details(int? id)
        {
            await Task.CompletedTask;
            TempData["Error"] = "Historial de compras fue retirado del proyecto.";
            return RedirectToAction("Index", "Productos");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registrar([FromBody] CompraRequestDTO modelo)
        {
            await Task.CompletedTask;
            return Json(new { exito = false, mensaje = "Compras fue retirado. Gestiona el stock desde Productos > Editar." });
        }

        [HttpGet]
        public async Task<IActionResult> BuscarProductos(string term, int page = 1)
        {
            await Task.CompletedTask;
            return Json(new { results = Array.Empty<object>(), pagination = new { more = false } });
        }
    }

    public class CompraRequestDTO
    {
        public Guid OperacionId { get; set; }
        public int ProveedorId { get; set; }
        [Required, StringLength(50)]
        public string NumeroDocumento { get; set; }
        public decimal Total { get; set; }
        public List<DetalleCompraDTO> Detalles { get; set; }
    }

    public class DetalleCompraDTO
    {
        public List<string> Lecturas { get; set; }
        public int ProductoId { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Total { get; set; }
    }
}
