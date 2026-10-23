using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting; // Necesario para manejar archivos
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ProductosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment; // Para guardar imágenes

        public ProductosController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Productos
        public async Task<IActionResult> Index()
        {
            var productos = await _context.Productos.Include(p => p.Categoria).Where(p => !p.EsServicio).ToListAsync();
            return View(productos);
        }

        // GET: Productos/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos
                .Include(p => p.Categoria)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (producto == null) return NotFound();

            return View(producto);
        }

        // GET: Productos/Create
        public IActionResult Create()
        {
            ViewData["CategoriaId"] = new SelectList(_context.Categorias.Where(c => c.Estado), "Id", "Nombre");
            return View(new Producto());
        }

        // POST: Productos/Create
        // Aceptamos "imagenArchivo" desde el formulario
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)]
        [RequestFormLimits(ValueCountLimit = 2100)]
        public async Task<IActionResult> Create(Producto producto, IFormFile imagenArchivo, List<string> lecturas = null)
        {
            ViewBag.Lecturas = lecturas ?? new List<string>();
            ModelState.Remove("Stock");
            try { await MiniMarket.Web.Services.CodigosLecturas.PrepararAlta(_context, producto, lecturas); }
            catch (InvalidOperationException ex) { producto.Stock = 0; ModelState.AddModelError("", ex.Message); }
            string extension = null;
            try { extension = await MiniMarket.Web.Services.ImagenSubida.Extension(imagenArchivo); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            producto.EsServicio = false;
            NormalizarCodigoBarras(producto);
            if (!producto.ConCaducidad) producto.FechaVencimiento = null;
            if (!await _context.Categorias.AnyAsync(c => c.Id == producto.CategoriaId && c.Estado))
                ModelState.AddModelError("CategoriaId", "Seleccione una categoría activa.");
            await ValidarCodigoBarrasUnico(producto.CodigoBarras, null);
            producto.AreaServicio = null;
            if (producto.Stock < 0) ModelState.AddModelError("Stock", "Stock inválido.");
            if (ModelState.IsValid)
            {
                // LÓGICA PARA GUARDAR IMAGEN
                if (imagenArchivo != null)
                {
                    // 1. Preparar carpeta
                    string carpetaImagenes = Path.Combine(_webHostEnvironment.WebRootPath, "imagenes", "productos");
                    if (!Directory.Exists(carpetaImagenes)) Directory.CreateDirectory(carpetaImagenes);

                    // 2. Generar nombre único (para que no se reemplacen fotos con el mismo nombre)
                    string nombreArchivo = Guid.NewGuid().ToString("N") + extension;
                    string rutaCompleta = Path.Combine(carpetaImagenes, nombreArchivo);

                    // 3. Guardar archivo en el servidor
                    using (var fileStream = new FileStream(rutaCompleta, FileMode.Create))
                    {
                        await imagenArchivo.CopyToAsync(fileStream);
                    }

                    // 4. Guardar la ruta en la base de datos
                    producto.ImagenUrl = "/imagenes/productos/" + nombreArchivo;
                }

                try
                {
                    _context.Add(producto);
                    if (producto.Stock > 0)
                        _context.MovimientosInventario.Add(new MovimientoInventario { Producto = producto, Cantidad = producto.Stock,
                            TipoMovimiento = "ENTRADA", Fecha = HoraPeru.Ahora, Usuario = User.Identity.Name,
                            Referencia = "Registro inicial de producto", Motivo = "Existencia inicial" });
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateException ex) when (EsDuplicadoIndice(ex))
                {
                    ModelState.AddModelError("CodigoBarras", "Ya existe un producto con este código de barras.");
                    _context.ChangeTracker.Clear();
                }
                catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            }
            ViewData["CategoriaId"] = new SelectList(_context.Categorias.Where(c => c.Estado || c.Id == producto.CategoriaId), "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // GET: Productos/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos.Include(p => p.Codigos).SingleOrDefaultAsync(p => p.Id == id);
            if (producto == null) return NotFound();
            
            ViewData["CategoriaId"] = new SelectList(_context.Categorias.Where(c => c.Estado || c.Id == producto.CategoriaId), "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // POST: Productos/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)]
        [RequestFormLimits(ValueCountLimit = 2100)]
        public async Task<IActionResult> Edit(int id, Producto producto, IFormFile imagenArchivo, bool eliminarImagen = false, List<string> lecturas = null,
            string tipoAjuste = null, int cantidad = 0, string motivo = null, Guid? operacionId = null)
        {
            ViewBag.Lecturas = lecturas ?? new List<string>();
            ViewBag.OperacionId = operacionId ?? Guid.NewGuid();
            ViewBag.TipoAjuste = tipoAjuste;
            ViewBag.MotivoAjuste = motivo;
            string extension = null;
            try { extension = await MiniMarket.Web.Services.ImagenSubida.Extension(imagenArchivo); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            if (id != producto.Id) return NotFound();
            NormalizarCodigoBarras(producto);
            if (!producto.ConCaducidad) producto.FechaVencimiento = null;
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            // La reserva y esta edición se serializan ANTES de leer estado y dependencias.
            var configuracionActual = await _context.Productos.Include(p => p.Codigos).SingleOrDefaultAsync(p => p.Id == id);
            if (configuracionActual == null) return NotFound();
            if (configuracionActual.EsServicio) return RedirectToAction("Servicio", "Petmor", new { id });
            var imagenAnterior = configuracionActual.ImagenUrl;
            if (((!producto.Estado && configuracionActual.Estado) || (producto.UsoInterno && !configuracionActual.UsoInterno)) && await MiniMarket.Web.Services.DependenciasOperacion.ProductoPendiente(_context, id))
                ModelState.AddModelError("Estado", "El producto tiene cuentas pendientes de cobro. Cóbrelas o cancele sus reservas antes de desactivarlo o marcarlo para uso interno.");
            if (producto.CategoriaId != configuracionActual.CategoriaId && !await _context.Categorias.AnyAsync(c => c.Id == producto.CategoriaId && c.Estado))
                ModelState.AddModelError("CategoriaId", "Seleccione una categoría activa.");
            await ValidarCodigoBarrasUnico(producto.CodigoBarras, id);
            var hayAjusteStock = !string.IsNullOrWhiteSpace(tipoAjuste) || !string.IsNullOrWhiteSpace(motivo) || cantidad > 0 || (lecturas?.Any() == true);
            var motivoNormalizado = motivo?.Trim();
            List<string> lecturasNormalizadas = [];
            try
            {
                await MiniMarket.Web.Services.CodigosLecturas.Asociar(_context, configuracionActual,
                    string.IsNullOrEmpty(producto.CodigoBarras) ? [] : new[] { producto.CodigoBarras });
                if (hayAjusteStock)
                {
                    if (!configuracionActual.Estado || configuracionActual.UsoInterno || !producto.Estado || producto.UsoInterno)
                        throw new InvalidOperationException("Solo productos activos y vendibles pueden ajustar stock.");
                    if (!operacionId.HasValue || operacionId == Guid.Empty || string.IsNullOrWhiteSpace(motivoNormalizado) || motivoNormalizado.Length > 300 || (tipoAjuste != "ENTRADA" && tipoAjuste != "SALIDA"))
                        throw new InvalidOperationException("Para modificar stock desde editar, seleccione Entrada o Salida, motivo obligatorio e identificador de operación.");
                    if (!MotivoValidoParaMovimiento(tipoAjuste, motivoNormalizado))
                        throw new InvalidOperationException("Seleccione un motivo válido para el movimiento elegido.");
                    if (lecturas?.Count > 0) lecturasNormalizadas = MiniMarket.Web.Services.CodigosLecturas.Normalizar(lecturas);
                    if (lecturasNormalizadas.Count == 0)
                        throw new InvalidOperationException("Escanee las unidades para modificar stock.");
                    cantidad = lecturasNormalizadas.Count;

                    var referencia = $"Editar producto ({tipoAjuste}) #{HuellaAjusteStock(tipoAjuste, cantidad, motivoNormalizado, lecturasNormalizadas)}";
                    var anterior = await _context.MovimientosInventario.SingleOrDefaultAsync(m => m.OperacionId == operacionId);
                    if (anterior != null)
                    {
                        if (anterior.ProductoId != id || anterior.Cantidad != cantidad || anterior.Motivo != motivoNormalizado || anterior.Usuario != User.Identity.Name || anterior.Referencia != referencia)
                            throw new InvalidOperationException("El identificador corresponde a otro ajuste.");
                        hayAjusteStock = false;
                    }
                    else
                    {
                        if (lecturasNormalizadas.Count > 0)
                        {
                            var resueltos = await MiniMarket.Web.Services.CodigosLecturas.ResolverIds(_context, lecturasNormalizadas);
                            if (tipoAjuste == "SALIDA")
                            {
                                if (lecturasNormalizadas.Any(c => !resueltos.TryGetValue(c, out var productoId) || productoId != id))
                                    throw new InvalidOperationException("Para salida, todos los códigos deben pertenecer a este producto.");
                            }
                            else
                            {
                                foreach (var par in resueltos.Where(x => x.Value != id))
                                    throw new InvalidOperationException($"El código {par.Key} ya pertenece a otro producto.");
                                var propios = configuracionActual.Codigos.Select(c => c.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
                                if (!string.IsNullOrWhiteSpace(configuracionActual.CodigoBarras)) propios.Add(configuracionActual.CodigoBarras);
                                foreach (var codigo in lecturasNormalizadas.Where(c => !resueltos.ContainsKey(c) && propios.Add(c)))
                                    configuracionActual.Codigos.Add(new CodigoProducto { Codigo = codigo });
                                if (string.IsNullOrWhiteSpace(configuracionActual.CodigoBarras)) configuracionActual.CodigoBarras = lecturasNormalizadas.First();
                                var codigosDisponibles = configuracionActual.Codigos.Select(c => c.Codigo).Concat(string.IsNullOrWhiteSpace(configuracionActual.CodigoBarras) ? [] : new[] { configuracionActual.CodigoBarras })
                                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                                if (codigosDisponibles > MiniMarket.Web.Services.CodigosLecturas.MaxCodigos)
                                    throw new InvalidOperationException($"Máximo {MiniMarket.Web.Services.CodigosLecturas.MaxCodigos} códigos por producto.");
                                if (configuracionActual.Stock + cantidad > codigosDisponibles)
                                    throw new InvalidOperationException("No hay códigos libres suficientes para este ingreso. Escanee códigos nuevos o corrija las asociaciones.");
                            }
                        }
                        if (tipoAjuste == "SALIDA" && configuracionActual.Stock < cantidad) throw new InvalidOperationException($"Stock insuficiente. Disponible: {configuracionActual.Stock} unidades.");
                        if (tipoAjuste == "ENTRADA" && configuracionActual.Stock > int.MaxValue - cantidad) throw new InvalidOperationException("La cantidad supera el límite de inventario.");
                        if (tipoAjuste == "ENTRADA") configuracionActual.Stock += cantidad; else configuracionActual.Stock -= cantidad;
                        _context.MovimientosInventario.Add(new MovimientoInventario { OperacionId = operacionId, Fecha = HoraPeru.Ahora, ProductoId = id,
                            TipoMovimiento = "AJUSTE", Cantidad = cantidad, Usuario = User.Identity.Name, Referencia = referencia, Motivo = motivoNormalizado });
                    }
                }
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            if (ModelState.IsValid)
            {
                try
                {
                    // LÓGICA PARA ACTUALIZAR IMAGEN
                    if (eliminarImagen)
                    {
                        configuracionActual.ImagenUrl = null;
                    }
                    else if (imagenArchivo != null)
                    {
                        string carpetaImagenes = Path.Combine(_webHostEnvironment.WebRootPath, "imagenes", "productos");
                        if (!Directory.Exists(carpetaImagenes)) Directory.CreateDirectory(carpetaImagenes);

                        string nombreArchivo = Guid.NewGuid().ToString("N") + extension;
                        string rutaCompleta = Path.Combine(carpetaImagenes, nombreArchivo);

                        using (var fileStream = new FileStream(rutaCompleta, FileMode.Create))
                        {
                            await imagenArchivo.CopyToAsync(fileStream);
                        }

                        configuracionActual.ImagenUrl = "/imagenes/productos/" + nombreArchivo;
                    }
                    // Lista explícita: nunca adjuntar el grafo recibido ni aceptar stock/servicio/imágenes del cliente.
                    configuracionActual.Nombre = producto.Nombre;
                    configuracionActual.Descripcion = producto.Descripcion;
                    configuracionActual.CodigoBarras = string.IsNullOrWhiteSpace(producto.CodigoBarras) ? configuracionActual.CodigoBarras : producto.CodigoBarras;
                    configuracionActual.Precio = producto.Precio;
                    configuracionActual.Costo = producto.Costo;
                    configuracionActual.StockMinimo = producto.StockMinimo;
                    configuracionActual.Estado = producto.Estado;
                    configuracionActual.UsoInterno = producto.UsoInterno;
                    configuracionActual.Unidad = producto.Unidad;
                    configuracionActual.CategoriaId = producto.CategoriaId;
                    configuracionActual.ConCaducidad = producto.ConCaducidad;
                    configuracionActual.FechaVencimiento = producto.FechaVencimiento;
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                    if (eliminarImagen || imagenArchivo != null)
                        MiniMarket.Web.Services.ImagenSubida.BorrarAnterior(_webHostEnvironment.WebRootPath, imagenAnterior, "imagenes/productos", HttpContext.RequestServices.GetRequiredService<ILogger<ProductosController>>());
                }
                catch (DbUpdateException ex) when (EsDuplicadoIndice(ex))
                {
                    ModelState.AddModelError("CodigoBarras", "Ya existe un producto con este código de barras.");
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductoExists(producto.Id)) return NotFound();
                    else throw;
                }
                catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
                if (ModelState.IsValid) return RedirectToAction(nameof(Index));
            }
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            producto.Codigos = await _context.CodigosProducto.AsNoTracking().Where(c => c.ProductoId == id).ToListAsync();
            producto.ImagenUrl = imagenAnterior;
            producto.Stock = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == id)).Stock;
            ModelState.Remove("Stock");
            ViewData["CategoriaId"] = new SelectList(_context.Categorias.Where(c => c.Estado || c.Id == producto.CategoriaId), "Id", "Nombre", producto.CategoriaId);
            return View(producto);
        }

        // GET: Productos/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int? id)
        {
            if (id == null) return NotFound();
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            if (producto.Estado && await MiniMarket.Web.Services.DependenciasOperacion.ProductoPendiente(_context, producto.Id))
            { TempData["Error"] = "El producto tiene cuentas pendientes de cobro."; return RedirectToAction(nameof(Index)); }
            producto.Estado = !producto.Estado;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Exito"] = producto.Estado ? "Producto activado correctamente." : "Producto desactivado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null) return NotFound();

            var tieneOperacionesReales = await _context.DetalleVentas.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id)
                || await _context.DetalleCompras.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id)
                || await _context.CargosAtencion.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id)
                || await _context.ConsumosAtencion.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id)
                || await _context.ReservasProductos.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id);
            var movimientosNoIniciales = await _context.MovimientosInventario.AnyAsync(x => x.ProductoId == id && x.Referencia != "Registro inicial de producto");
            if (await _context.ExistenciasSucursales.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id && x.SucursalId != _context.SucursalId && x.Cantidad != 0) ||
                await _context.MovimientosInventario.IgnoreQueryFilters().AnyAsync(x => x.ProductoId == id && EF.Property<int>(x, "SucursalId") != _context.SucursalId))
                movimientosNoIniciales = true;

            if (tieneOperacionesReales || movimientosNoIniciales)
            {
                TempData["Error"] = "No se puede eliminar porque el producto tiene historial. Desactívalo para conservar compras, ventas, Kardex y atenciones.";
                return RedirectToAction(nameof(Index));
            }

            var movimientosIniciales = await _context.MovimientosInventario.Where(x => x.ProductoId == id).ToListAsync();
            _context.MovimientosInventario.RemoveRange(movimientosIniciales);
            _context.Productos.Remove(producto);
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Producto eliminado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private bool ProductoExists(int id)
        {
            return _context.Productos.Any(e => e.Id == id);
        }

        private void NormalizarCodigoBarras(Producto producto)
        {
            if (string.IsNullOrEmpty(producto.CodigoBarras)) { producto.CodigoBarras = null; return; }
            try { producto.CodigoBarras = MiniMarket.Web.Services.CodigosLecturas.Canonicalizar(producto.CodigoBarras); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("CodigoBarras", ex.Message); }
        }

        private async Task ValidarCodigoBarrasUnico(string codigoBarras, int? productoId)
        {
            if (string.IsNullOrWhiteSpace(codigoBarras)) return;
            var existe = await _context.Productos.IgnoreQueryFilters().AnyAsync(p => (p.CodigoBarras == codigoBarras || p.Codigos.Any(c => c.Codigo == codigoBarras)) && (!productoId.HasValue || p.Id != productoId.Value));
            if (existe) ModelState.AddModelError("CodigoBarras", "Ya existe un producto con este código de barras.");
        }

        private static string HuellaAjusteStock(string tipoAjuste, int cantidad, string motivo, IEnumerable<string> lecturas)
        {
            var texto = string.Join("\n", new[] { tipoAjuste, cantidad.ToString(), motivo ?? string.Empty }
                .Concat((lecturas ?? []).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(texto));
            return Convert.ToHexString(bytes)[..12];
        }

        private static bool MotivoValidoParaMovimiento(string tipoAjuste, string motivo)
        {
            var motivosEntrada = new[] { "Ingreso físico por escaneo", "Reposición registrada fuera del sistema", "Ajuste positivo por inventario físico", "Corrección de códigos asociados" };
            var motivosSalida = new[] { "Producto dañado", "Merma / vencimiento", "Pérdida o robo", "Ajuste negativo por inventario físico", "Corrección administrativa" };
            return tipoAjuste == "ENTRADA"
                ? motivosEntrada.Contains(motivo, StringComparer.Ordinal)
                : motivosSalida.Contains(motivo, StringComparer.Ordinal);
        }

        private static bool EsDuplicadoIndice(DbUpdateException ex)
        {
            return ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627;
        }
    }
}
