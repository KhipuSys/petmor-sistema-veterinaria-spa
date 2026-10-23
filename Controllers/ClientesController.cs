using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using System.Linq;
using System.Threading.Tasks;

namespace MiniMarket.Web.Controllers
{
    [Authorize(Roles = "Administrador,Recepcionista,Veterinario")]
    public class ClientesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FactilizaService _factilizaService;

        public ClientesController(ApplicationDbContext context, FactilizaService factilizaService)
        {
            _context = context;
            _factilizaService = factilizaService;
        }

        public async Task<IActionResult> Index(string q, bool? estado, int pagina = 1)
        {
            ViewBag.Q = q;
            ViewBag.Estado = estado;
            var query = FiltrarClientes(q, estado);
            ViewBag.TotalClientes = await query.CountAsync();
            var totalPaginas = Math.Max(1, (int)Math.Ceiling((int)ViewBag.TotalClientes / 50m));
            pagina = Math.Clamp(pagina, 1, totalPaginas);
            ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas;
            return View(await query.OrderBy(c => c.Nombre).ThenBy(c => c.Id).Skip((pagina - 1) * 50).Take(50).ToListAsync());
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> DatosTabla(int draw = 0, int start = 0, int length = 10,
            string q = null, bool? estado = null, int columna = 0, bool descendente = false)
        {
            if (!ModelState.IsValid || draw < 0 || start < 0 || (length != 10 && length != 25 && length != 50 && length != 100 && length != -1) ||
                columna < 0 || columna > 3 || (q?.Length ?? 0) > 200)
                return BadRequest("Revisa los parámetros de la tabla.");

            var query = FiltrarClientes(q, estado);
            var recordsTotal = await _context.Clientes.CountAsync();
            var recordsFiltered = await query.CountAsync();
            var ordenados = columna switch
            {
                1 => descendente ? query.OrderByDescending(c => c.Documento) : query.OrderBy(c => c.Documento),
                2 => descendente ? query.OrderByDescending(c => c.Telefono) : query.OrderBy(c => c.Telefono),
                3 => descendente ? query.OrderByDescending(c => c.Estado) : query.OrderBy(c => c.Estado),
                _ => descendente ? query.OrderByDescending(c => c.Nombre) : query.OrderBy(c => c.Nombre)
            };
            var pagina = ordenados.ThenBy(c => c.Id).Skip(start);
            if (length > 0) pagina = pagina.Take(length);
            var data = await pagina
                .Select(c => new { c.Id, c.Nombre, c.Documento, c.Telefono, c.Correo, c.Estado }).ToListAsync();
            return Json(new { draw, recordsTotal, recordsFiltered, data });
        }

        private IQueryable<Cliente> FiltrarClientes(string q, bool? estado)
        {
            var query = _context.Clientes.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var termino = q.Trim();
                query = query.Where(c => c.Nombre.Contains(termino) || c.Documento.Contains(termino) || c.Telefono.Contains(termino) || c.Correo.Contains(termino) ||
                    _context.Mascotas.Any(m => m.ClienteId == c.Id && (m.Nombre.Contains(termino) || m.Especie.Contains(termino) || m.Raza.Contains(termino))));
            }
            if (estado.HasValue) query = query.Where(c => c.Estado == estado.Value);
            return query;
        }

        public async Task<IActionResult> Mascotas(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _context.Clientes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
            if (cliente == null) return NotFound();
            return RedirectToAction("Mascotas", "Petmor", new { clienteId = id });
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarMascotaSistema()
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            var cliente = await ObtenerClienteSistema();
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return RedirectToAction("Mascota", "Petmor", new { clienteId = cliente.Id });
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarDocumento(string documento)
        {
            var resultado = await _factilizaService.ConsultarDocumentoAsync(documento);
            return Json(resultado);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Cliente cliente, bool registrarMascota = false, bool usuarioSistema = false)
        {
            if (usuarioSistema)
            {
                ModelState.Remove(nameof(cliente.Documento));
                ModelState.Remove(nameof(cliente.Nombre));
                ModelState.Remove(nameof(cliente.Telefono));
                ModelState.Remove(nameof(cliente.Correo));
                ModelState.Remove(nameof(cliente.Direccion));
                await using var txSistema = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                var clienteSistema = await ObtenerClienteSistema();
                await _context.SaveChangesAsync();
                await txSistema.CommitAsync();

                TempData["Titulo"] = "Publico General";
                TempData["Mensaje"] = "Se usará el propietario interno para registrar la mascota.";
                TempData["Tipo"] = "success";

                if (registrarMascota)
                    return RedirectToAction("Mascota", "Petmor", new { clienteId = clienteSistema.Id });

                return RedirectToAction(nameof(Index));
            }
            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                // Evitar duplicados por Documento
                if (await _context.Clientes.AnyAsync(c => c.Documento == cliente.Documento))
                {
                    ModelState.AddModelError("Documento", "Ya existe un cliente con este documento.");
                    return View(cliente);
                }

                cliente.Estado = true;
                _context.Add(cliente);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                TempData["Titulo"] = "¡Registrado!";
                TempData["Mensaje"] = "Cliente guardado exitosamente.";
                TempData["Tipo"] = "success";
                
                if (registrarMascota)
                    return RedirectToAction("Mascota", "Petmor", new { clienteId = cliente.Id });

                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        private async Task<Cliente> ObtenerClienteSistema()
        {
            const string documentoSistema = "00000000";
            var cliente = await _context.Clientes.SingleOrDefaultAsync(c => c.Documento == documentoSistema);
            if (cliente == null)
            {
                cliente = new Cliente
                {
                    Nombre = "Publico General",
                    Documento = documentoSistema,
                    Telefono = "000000000",
                    Estado = true
                };
                _context.Clientes.Add(cliente);
                return cliente;
            }

            cliente.Nombre = "Publico General";
            cliente.Telefono = string.IsNullOrWhiteSpace(cliente.Telefono) ? "000000000" : cliente.Telefono;
            cliente.Estado = true;
            return cliente;
        }

        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int id, Cliente cliente)
        {
            if (id != cliente.Id) return NotFound();

            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await ActualizacionComercial.BloquearOperaciones(_context);
                var existente = await _context.Clientes.FindAsync(id);
                if (existente == null) return NotFound();
                if (await _context.Clientes.AnyAsync(c => c.Id != id && c.Documento == cliente.Documento))
                {
                    ModelState.AddModelError("Documento", "Ya existe otro cliente con este documento.");
                    return View(cliente);
                }
                try
                {
                    // Editar datos no activa/desactiva al cliente ni cambia su identidad interna.
                    existente.Documento = cliente.Documento;
                    existente.Nombre = cliente.Nombre;
                    existente.Telefono = cliente.Telefono;
                    existente.Correo = cliente.Correo;
                    existente.Direccion = cliente.Direccion;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    TempData["Titulo"] = "¡Actualizado!";
                    TempData["Mensaje"] = "Datos del cliente actualizados.";
                    TempData["Tipo"] = "success";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Clientes.Any(e => e.Id == cliente.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> CambiarEstado(int? id)
        {
            if (id == null) return NotFound();
            await using var tx = await _context.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(_context);
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();

            if (cliente.Estado && await DependenciasOperacion.ClientePendiente(_context, cliente.Id))
            { TempData["Error"] = "Complete las atenciones y cobre las cuentas pendientes antes de desactivar al cliente."; return RedirectToAction(nameof(Index)); }
            cliente.Estado = !cliente.Estado;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Titulo"] = cliente.Estado ? "¡Habilitado!" : "¡Inhabilitado!";
            TempData["Mensaje"] = "El estado del cliente ha cambiado.";
            TempData["Tipo"] = cliente.Estado ? "success" : "warning";

            return RedirectToAction(nameof(Index));
        }

        // GET: Historial de compras de un cliente
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Compras(int? id)
        {
            if (id == null) return NotFound();

            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();

            var ventas = await _context.Ventas
                .Include(v => v.Usuario)
                .Include(v => v.Detalles)
                .Where(v => v.ClienteId == id)
                .OrderByDescending(v => v.Fecha)
                .ToListAsync();

            ViewBag.Cliente = cliente;
            return View(ventas);
        }
    }
}
