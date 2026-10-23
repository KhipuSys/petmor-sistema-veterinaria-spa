using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Models.ViewModels;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers;

[Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
public class PetmorController(ApplicationDbContext db, AtencionService operaciones, EmisionVentaService emision) : Controller
{
    private string Usuario => User.Identity.Name;
    private bool PuedeAtender(Atencion a) => User.IsInRole("Administrador") ||
        (User.IsInRole(a.Area == AreaAtencion.Spa ? "Groomer" : "Veterinario") &&
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == a.ResponsableId);

    public IActionResult Index() => RedirectToAction("Index", "Home");

    public async Task<IActionResult> Mascotas(string q, string especie, int? clienteId, bool? activa, int pagina = 1)
    {
        ViewBag.Q = q;
        ViewBag.Especie = especie;
        ViewBag.ClienteId = clienteId;
        ViewBag.Activa = activa;
        ViewBag.Clientes = new SelectList(await db.Clientes.AsNoTracking().Where(x => x.Estado).OrderBy(x => x.Nombre).ToListAsync(), "Id", "Nombre", clienteId);
        ViewBag.Especies = await db.Mascotas.AsNoTracking().Select(x => x.Especie).Where(x => x != null && x != "").Distinct().OrderBy(x => x).ToListAsync();

        var query = db.Mascotas.Include(x => x.Cliente).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Nombre.Contains(q) || x.Cliente.Nombre.Contains(q) || x.Cliente.Documento.Contains(q));
        if (!string.IsNullOrWhiteSpace(especie)) query = query.Where(x => x.Especie == especie);
        if (clienteId.HasValue) query = query.Where(x => x.ClienteId == clienteId.Value);
        if (activa.HasValue) query = query.Where(x => x.Activa == activa.Value);
        ViewBag.TotalMascotas = await query.CountAsync();
        var totalPaginas = Math.Max(1, (int)Math.Ceiling((int)ViewBag.TotalMascotas / 50m));
        pagina = Math.Clamp(pagina, 1, totalPaginas);
        ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas;
        var mascotas = await query.OrderBy(x => x.Nombre).ThenBy(x => x.Id).Skip((pagina - 1) * 50).Take(50).ToListAsync();
        var ids = mascotas.Select(x => x.Id).ToList();
        ViewBag.Fotos = await db.ArchivosClinicos.AsNoTracking()
            .Where(x => ids.Contains(x.MascotaId) && x.EsFoto)
            .GroupBy(x => x.MascotaId)
            .Select(x => new { MascotaId = x.Key, FotoId = x.OrderByDescending(a => a.Fecha).Select(a => a.Id).FirstOrDefault() })
            .ToDictionaryAsync(x => x.MascotaId, x => x.FotoId);
        return View(mascotas);
    }

    [Authorize(Roles = "Administrador,Recepcionista,Veterinario")]
    public async Task<IActionResult> Mascota(int? id, int? clienteId)
    {
        var m = id.HasValue ? await db.Mascotas.FindAsync(id.Value) : new Mascota { ClienteId = clienteId ?? 0 };
        if (m == null) return NotFound();
        ViewBag.FotoId = id.HasValue
            ? await db.ArchivosClinicos.AsNoTracking().Where(x => x.MascotaId == id.Value && x.EsFoto).OrderByDescending(x => x.Fecha).Select(x => (int?)x.Id).FirstOrDefaultAsync()
            : null;
        await Clientes(); return View(m);
    }

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista,Veterinario")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Mascota([Bind("Id,ClienteId,Nombre,Especie,Raza,Sexo,FechaNacimiento,Peso,Alergias,Observaciones,Activa")] Mascota form, IFormFile fotoMascota, string especieOtro)
    {
        form.Especie = NormalizarEspecie(form.Especie, especieOtro);
        if (!await db.Clientes.AnyAsync(x => x.Id == form.ClienteId && x.Estado)) ModelState.AddModelError("ClienteId", "Seleccione un propietario activo.");
        if (string.IsNullOrWhiteSpace(form.Especie)) ModelState.AddModelError("Especie", "Ingrese la especie de la mascota.");
        if (form.FechaNacimiento > HoraPeru.Hoy) ModelState.AddModelError("FechaNacimiento", "La fecha de nacimiento no puede ser futura.");
        if (fotoMascota != null && fotoMascota.Length > 5_000_000) ModelState.AddModelError("", "La foto debe pesar como máximo 5 MB.");
        if (!ModelState.IsValid) { await Clientes(); return View(form); }
        if (form.Id == 0) db.Mascotas.Add(form);
        else
        {
            var actual = await db.Mascotas.FindAsync(form.Id);
            if (actual == null) return NotFound();
            // El propietario histórico se mantiene: un traslado requiere revisión administrativa.
            if (actual.ClienteId != form.ClienteId && await db.Atenciones.IgnoreQueryFilters().AnyAsync(x => x.MascotaId == form.Id))
            { ModelState.AddModelError("ClienteId", "La mascota tiene historial; no se puede cambiar su propietario desde esta ficha."); await Clientes(); return View(form); }
            db.Entry(actual).CurrentValues.SetValues(form);
        }
        await db.SaveChangesAsync();

        if (fotoMascota != null && fotoMascota.Length >= 8)
        {
            using var contenido = new MemoryStream();
            await fotoMascota.CopyToAsync(contenido);
            var bytes = contenido.ToArray();
            var tipo = DetectarTipoImagen(bytes);
            if (tipo == null)
            {
                TempData["Error"] = "La ficha se guardó, pero la foto debe ser PNG, JPEG, WebP, GIF, AVIF o BMP.";
                return RedirectToAction(nameof(Mascota), new { id = form.Id });
            }

            var nombre = Path.GetFileName(fotoMascota.FileName);
            if (nombre.Length > 200) nombre = nombre[^200..];
            db.ArchivosClinicos.Add(new ArchivoClinico { MascotaId = form.Id, Nombre = nombre, TipoContenido = tipo, Contenido = bytes, EsFoto = true, Usuario = Usuario });
            await db.SaveChangesAsync();
        }

        TempData["Exito"] = "Ficha de mascota guardada.";
        return RedirectToAction(nameof(FichaMascota), new { id = form.Id });
    }

    private static string NormalizarEspecie(string especie, string especieOtro)
    {
        var valor = especie?.Trim();
        if (string.Equals(valor, "Otro", StringComparison.OrdinalIgnoreCase)) valor = especieOtro?.Trim();
        if (string.IsNullOrWhiteSpace(valor)) return valor;
        return valor.Length <= 40 ? valor : valor[..40];
    }

    public async Task<IActionResult> FichaMascota(int id)
    {
        var m = await db.Mascotas.Include(x => x.Cliente).SingleOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        // Historial clínico compartido de solo lectura; las operaciones siguen filtradas por sede.
        ViewBag.Historial = await db.Atenciones.IgnoreQueryFilters().AsNoTracking().Include(x => x.Eventos).Where(x => x.MascotaId == id).AsSplitQuery().OrderByDescending(x => x.Inicio).ToListAsync();
        ViewBag.SedesHistorial = await db.Atenciones.IgnoreQueryFilters().AsNoTracking().Where(x => x.MascotaId == id)
            .Join(db.Sucursales, x => EF.Property<int>(x, "SucursalId"), s => s.Id, (x, s) => new { x.Id, s.Nombre })
            .ToDictionaryAsync(x => x.Id, x => x.Nombre);
        ViewBag.Preventivos = await db.PreventivosMascota.Where(x => x.MascotaId == id).OrderByDescending(x => x.Fecha).ToListAsync();
        ViewBag.Archivos = await db.ArchivosClinicos.Where(x => x.MascotaId == id).OrderByDescending(x => x.Fecha)
            .Select(x => new ArchivoClinico { Id = x.Id, Nombre = x.Nombre, Fecha = x.Fecha, EsFoto = x.EsFoto }).ToListAsync();
        return View(m);
    }

    public async Task<IActionResult> Agenda(DateTime? fecha, AreaAtencion? areaAtencion, EstadoAtencion? estado, bool activas = false)
    {
        var dia = (fecha ?? HoraPeru.Hoy).Date;
        ViewBag.Fecha = dia; ViewBag.Area = areaAtencion; ViewBag.Estado = estado; ViewBag.Activas = activas;
        ViewBag.MostrarAccionesAgenda = true;
        var query = db.Atenciones
            .Include(x => x.Mascota).ThenInclude(x => x.Cliente)
            .Include(x => x.Cargos).ThenInclude(x => x.Producto)
            .Include(x => x.Eventos)
            .AsSplitQuery()
            .AsQueryable();
        if (activas) query = query.Where(x => x.Estado != EstadoAtencion.Entregada && x.Estado != EstadoAtencion.Cancelada);
        else query = query.Where(x => x.Inicio >= dia && x.Inicio < dia.AddDays(1));
        if (areaAtencion.HasValue) query = query.Where(x => x.Area == areaAtencion);
        if (estado.HasValue) query = query.Where(x => x.Estado == estado);
        var atenciones = await query.OrderBy(x => x.Inicio).ToListAsync();
        var mascotaIds = atenciones.Select(x => x.MascotaId).Distinct().ToList();
        ViewBag.Fotos = await db.ArchivosClinicos.AsNoTracking()
            .Where(x => x.EsFoto && mascotaIds.Contains(x.MascotaId))
            .GroupBy(x => x.MascotaId)
            .Select(x => new { MascotaId = x.Key, FotoId = x.OrderByDescending(f => f.Fecha).Select(f => f.Id).First() })
            .ToDictionaryAsync(x => x.MascotaId, x => x.FotoId);
        ViewBag.Responsables = await NombresUsuarios();
        return View(atenciones);
    }

    [Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
    public async Task<IActionResult> Agendar(int? mascotaId, AreaAtencion areaAtencion = AreaAtencion.Veterinaria)
    {
        await AgendaListas(); return View(new AgendaForm { MascotaId = mascotaId ?? 0, Area = areaAtencion });
    }

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
    public async Task<IActionResult> Agendar(AgendaForm f)
    {
        if (ModelState.IsValid)
        {
            try { await operaciones.Agendar(f, Usuario); return RedirectToAction(nameof(Agenda), new { activas = true }); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await AgendaListas(); return View(f);
    }

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
    public async Task<IActionResult> Estado(int id, EstadoAtencion estado, string motivo, string returnUrl = null)
    {
        if (motivo?.Length > 300) { TempData["Error"] = "El motivo admite hasta 300 caracteres."; return RedirectToAction(nameof(Agenda), new { activas = true }); }
        var a = await db.Atenciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        return await Ejecutar(id, () => operaciones.CambiarEstado(id, estado, Usuario, PuedeAtender(a), motivo), returnUrl);
    }

    [HttpPost, Authorize(Roles = "Administrador,Veterinario,Groomer")]
    public async Task<IActionResult> Ficha(int id, FichaForm form)
    {
        var a = await db.Atenciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (!PuedeAtender(a)) return Forbid();
        if (!ModelState.IsValid) { TempData["Error"] = "Verifique el peso y la longitud de los textos."; return RedirectToAction(nameof(Agenda), new { activas = true }); }
        return await Ejecutar(id, () => operaciones.GuardarFicha(id, form, Usuario));
    }

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista,Veterinario,Groomer")]
    public Task<IActionResult> Cargo(int id, int productoId, int cantidad = 1, bool quitar = false, List<string> lecturas = null) =>
        Ejecutar(id, () => operaciones.Cargo(id, productoId, cantidad, Usuario, quitar, lecturas));

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista")]
    public Task<IActionResult> Reprogramar(int id, DateTime inicio, int duracion) =>
        Ejecutar(id, () => operaciones.Reprogramar(id, inicio, duracion, Usuario));

    public async Task<IActionResult> Indicaciones(int id)
    {
        var a = await db.Atenciones.Include(x => x.Mascota).ThenInclude(x => x.Cliente).SingleOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (a.Estado is not (EstadoAtencion.Lista or EstadoAtencion.Entregada))
        { TempData["Error"] = "Finalice la atención antes de emitir las indicaciones."; return RedirectToAction(nameof(Agenda), new { activas = true }); }
        ViewBag.Responsable = await NombreUsuario(a.ResponsableId);
        return View(a);
    }

    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Reporte(DateTime? desde, DateTime? hasta)
    {
        var inicio = (desde ?? new DateTime(HoraPeru.Hoy.Year, HoraPeru.Hoy.Month, 1)).Date;
        var fin = (hasta ?? HoraPeru.Hoy).Date;
        if (fin < inicio || (fin - inicio).TotalDays > 366)
        { ViewData["ErrorFiltro"] = "Seleccione un rango de hasta 366 días."; return View("~/Views/Shared/FiltroInvalido.cshtml"); }
        ViewBag.Desde = inicio; ViewBag.Hasta = fin;
        return View(await db.Atenciones.Include(x => x.Mascota).ThenInclude(x => x.Cliente).Include(x => x.Consumos).Include(x => x.Eventos).Include(x => x.Venta).ThenInclude(x => x.Detalles)
            .Where(x => x.Inicio >= inicio && x.Inicio < fin.AddDays(1)).AsSplitQuery().OrderByDescending(x => x.Inicio).ToListAsync());
    }

    [HttpPost, Authorize(Roles = "Administrador,Veterinario,Groomer")]
    [RequestFormLimits(ValueCountLimit = 2100)]
    public async Task<IActionResult> Consumo(int id, int productoId, int cantidad, Guid operacion, List<string> lecturas = null)
    {
        var a = await db.Atenciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (!PuedeAtender(a)) return Forbid();
        return await Ejecutar(id, () => operaciones.Consumir(id, productoId, cantidad, operacion, Usuario, lecturas));
    }

    public IActionResult Spa() => RedirectToAction(nameof(Agenda), new { areaAtencion = AreaAtencion.Spa, activas = true });

    public async Task<IActionResult> Cuentas()
    {
        ViewBag.Reservas = await db.ReservasProductos.AsNoTracking().Include(x => x.Cliente)
            .Where(x => x.Estado == "Pendiente").OrderBy(x => x.Fecha).ToListAsync();
        return View(await db.Atenciones.Include(x => x.Mascota).ThenInclude(x => x.Cliente).Include(x => x.Cargos)
        .Where(x => x.VentaId == null && x.Cargos.Any() && (x.Estado == EstadoAtencion.Lista || x.Estado == EstadoAtencion.Entregada))
            .OrderBy(x => x.Inicio).ToListAsync());
    }

    [HttpPost, Authorize(Roles = "Administrador,Recepcionista")]
    public async Task<IActionResult> Cobrar(int id, Guid operacion, string metodoPago, decimal total, decimal descuento = 0)
    {
        var a = await db.Atenciones.AsNoTracking().Include(x => x.Mascota).Include(x => x.Cargos).SingleOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        var baseCobro = total - descuento;
        var recargo = PagoReglas.CalcularRecargo(metodoPago, baseCobro);
        var request = new VentaRequest { AtencionId = id, OperacionId = operacion, ClienteId = a.Mascota.ClienteId, Total = baseCobro + recargo,
            Descuento = descuento, Recargo = recargo, MetodoPago = metodoPago, Detalles = a.Cargos.Select(x => new DetalleRequest { ProductoId = x.ProductoId, Cantidad = x.Cantidad }).ToList() };
        try
        {
            var (venta, repetida) = await new OperacionVentaService(db).Registrar(request, Usuario, User.IsInRole("Administrador"));
            if (!repetida) await emision.Intentar(venta.Id);
            return RedirectToAction("Ticket", "Ventas", new { id = venta.Id });
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Cuentas)); }
    }

    public async Task<IActionResult> Servicios() => View(await db.Productos.Where(x => x.EsServicio && x.Estado).OrderBy(x => x.AreaServicio).ThenBy(x => x.Nombre).ToListAsync());

    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Servicio(int? id)
    {
        if (!id.HasValue) return View(new ServicioForm());
        var p = await db.Productos.SingleOrDefaultAsync(x => x.Id == id && x.EsServicio);
        if (p == null) return NotFound();
        return View(new ServicioForm { Id = p.Id, Nombre = p.Nombre, Descripcion = p.Descripcion, Precio = p.Precio,
            Area = p.AreaServicio ?? AreaAtencion.Veterinaria, Duracion = p.DuracionMinutos, Estado = p.Estado });
    }

    [HttpPost, Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Servicio(ServicioForm f)
    {
        if (!ModelState.IsValid) return View(f);
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var p = f.Id == 0 ? new Producto { EsServicio = true, CodigoBarras = $"SRV-{Guid.NewGuid():N}" } : await db.Productos.SingleOrDefaultAsync(x => x.Id == f.Id && x.EsServicio);
        if (p == null) return NotFound();
        var nombreCategoria = f.Area == AreaAtencion.Spa ? "Servicios de spa" : "Servicios veterinarios";
        if (!f.Estado && p.Estado && await DependenciasOperacion.ProductoPendiente(db, p.Id))
        { ModelState.AddModelError("Estado", "El servicio tiene cuentas pendientes de cobro."); return View(f); }
        var cat = await db.Categorias.FirstOrDefaultAsync(x => x.Nombre == nombreCategoria);
        if (cat == null) { cat = new Categoria { Nombre = nombreCategoria }; db.Categorias.Add(cat); }
        p.Categoria = cat; p.Nombre = f.Nombre.Trim(); p.Descripcion = f.Descripcion; p.Precio = f.Precio;
        p.AreaServicio = f.Area; p.DuracionMinutos = f.Duracion; p.Estado = f.Estado;
        if (f.Id == 0) db.Productos.Add(p);
        await db.SaveChangesAsync(); await tx.CommitAsync(); TempData["Exito"] = "Servicio guardado.";
        return RedirectToAction(nameof(Servicios));
    }

    [HttpPost, Authorize(Roles = "Administrador,Veterinario")]
    public async Task<IActionResult> Preventivo([Bind("MascotaId,Tipo,Nombre,Fecha,Proxima,Observaciones")] PreventivoMascota f)
    {
        if (f.Tipo != "Vacuna" || f.Fecha > HoraPeru.Ahora || f.Fecha < new DateTime(2000, 1, 1) || f.Proxima <= f.Fecha ||
            !await db.Mascotas.AnyAsync(x => x.Id == f.MascotaId && x.Activa)) ModelState.AddModelError("", "Revise tipo, mascota y fechas.");
        if (ModelState.IsValid)
        {
            f.Usuario = Usuario;
            f.Lote = null;
            db.PreventivosMascota.Add(f);
            TempData["Exito"] = "Preventivo registrado. El registro clínico no descuenta inventario.";
            await db.SaveChangesAsync();
        }
        else TempData["Error"] = "No se guardó el preventivo: revise campos obligatorios y fechas.";
        return RedirectToAction(nameof(FichaMascota), new { id = f.MascotaId });
    }

    [HttpPost, Authorize(Roles = "Administrador,Veterinario")]
    public async Task<IActionResult> EliminarPreventivo(int id)
    {
        var preventivo = await db.PreventivosMascota.SingleOrDefaultAsync(x => x.Id == id);
        if (preventivo == null) return NotFound();
        var mascotaId = preventivo.MascotaId;
        db.PreventivosMascota.Remove(preventivo);
        await db.SaveChangesAsync();
        TempData["Exito"] = "Preventivo eliminado.";
        return RedirectToAction(nameof(FichaMascota), new { id = mascotaId });
    }

    [HttpPost, RequestSizeLimit(6_000_000), Authorize(Roles = "Administrador,Veterinario,Recepcionista")]
    public async Task<IActionResult> Adjuntar(int mascotaId, IFormFile archivo, bool esFoto)
    {
        if (!await db.Mascotas.AnyAsync(x => x.Id == mascotaId)) return NotFound();
        if (!esFoto && !(User.IsInRole("Administrador") || User.IsInRole("Veterinario"))) return Forbid();
        if (archivo == null || archivo.Length < 8 || archivo.Length > 5_000_000)
        { TempData["Error"] = "Seleccione un PDF, PNG, JPEG, WebP, GIF, AVIF o BMP de hasta 5 MB."; return RedirectToAction(nameof(FichaMascota), new { id = mascotaId }); }
        using var contenido = new MemoryStream(); await archivo.CopyToAsync(contenido);
        var bytes = contenido.ToArray();
        var tipo = bytes.Take(4).SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }) ? "application/pdf" : DetectarTipoImagen(bytes);
        if (tipo == null || (esFoto && tipo == "application/pdf")) TempData["Error"] = "El archivo no es una imagen o PDF admitido.";
        else
        {
            var nombre = Path.GetFileName(archivo.FileName);
            if (nombre.Length > 200) nombre = nombre[^200..];
            db.ArchivosClinicos.Add(new ArchivoClinico { MascotaId = mascotaId, Nombre = nombre, TipoContenido = tipo, Contenido = bytes, EsFoto = esFoto, Usuario = Usuario });
            await db.SaveChangesAsync(); TempData["Exito"] = "Archivo incorporado al expediente.";
        }
        return RedirectToAction(nameof(FichaMascota), new { id = mascotaId });
    }

    public async Task<IActionResult> Archivo(int id)
    {
        var a = await db.ArchivosClinicos.FindAsync(id);
        if (a == null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return a.EsFoto ? File(a.Contenido, a.TipoContenido) : File(a.Contenido, a.TipoContenido, a.Nombre);
    }

    private static string DetectarTipoImagen(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 12 && bytes.Take(4).SequenceEqual(new byte[] { 82, 73, 70, 70 }) && bytes.Skip(8).Take(4).SequenceEqual(new byte[] { 87, 69, 66, 80 })) return "image/webp";
        if (bytes.Length >= 6 && (bytes.Take(6).SequenceEqual(new byte[] { 71, 73, 70, 56, 55, 97 }) || bytes.Take(6).SequenceEqual(new byte[] { 71, 73, 70, 56, 57, 97 }))) return "image/gif";
        if (bytes.Length >= 12 && bytes.Skip(4).Take(4).SequenceEqual(new byte[] { 102, 116, 121, 112 }) && (bytes.Skip(8).Take(4).SequenceEqual(new byte[] { 97, 118, 105, 102 }) || bytes.Skip(8).Take(4).SequenceEqual(new byte[] { 97, 118, 105, 115 }))) return "image/avif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4d) return "image/bmp";
        return null;
    }

    private async Task<IActionResult> Ejecutar(int id, Func<Task> operacion, string returnUrl = null)
    {
        try { await operacion(); TempData["Exito"] = "Operación registrada."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Agenda), new { activas = true });
    }

    private async Task<Dictionary<string, string>> NombresUsuarios()
    {
        var perfiles = await db.UsuarioPerfiles.AsNoTracking().ToDictionaryAsync(x => x.UsuarioId, x => x.NombreCompleto);
        var usuarios = await db.Users.AsNoTracking().Select(x => new { x.Id, x.UserName }).ToListAsync();
        return usuarios.ToDictionary(x => x.Id, x => perfiles.TryGetValue(x.Id, out var nombre) && !string.IsNullOrWhiteSpace(nombre) ? nombre : NombreSinCorreo(x.UserName));
    }

    private async Task<string> NombreUsuario(string id)
    {
        var perfil = await db.UsuarioPerfiles.AsNoTracking().Where(x => x.UsuarioId == id).Select(x => x.NombreCompleto).SingleOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(perfil)) return perfil;
        var usuario = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.UserName).SingleOrDefaultAsync();
        return NombreSinCorreo(usuario);
    }

    private static string NombreSinCorreo(string usuario) => string.IsNullOrWhiteSpace(usuario) ? "-" : usuario.Split('@')[0];
    private async Task Clientes() => ViewBag.Clientes = new SelectList(await db.Clientes.Where(x => x.Estado).OrderBy(x => x.Nombre).ToListAsync(), "Id", "Nombre");
    private async Task AgendaListas()
    {
        var mascotas = await db.Mascotas.Include(x => x.Cliente).Where(x => x.Activa && x.Cliente.Estado).OrderBy(x => x.Nombre).ToListAsync();
        ViewBag.Mascotas = new SelectList(mascotas.Select(x => new { x.Id, Nombre = $"{x.Nombre} · {x.Cliente.Nombre}" }), "Id", "Nombre");
        ViewBag.Servicios = await db.Productos.Where(x => x.EsServicio && x.Estado && !x.UsoInterno).OrderBy(x => x.AreaServicio).ThenBy(x => x.Nombre)
            .Select(x => new { x.Id, Nombre = x.Nombre + " · S/. " + x.Precio.ToString("N2"), Area = (int)(x.AreaServicio ?? AreaAtencion.Veterinaria), Duracion = x.DuracionMinutos }).ToListAsync();
        var perfiles = await db.UsuarioPerfiles.AsNoTracking().ToDictionaryAsync(x => x.UsuarioId, x => x.NombreCompleto);
        var usuarios = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
            where (r.Name == "Veterinario" || r.Name == "Groomer") && db.UsuariosSedes.Any(s => s.UsuarioId == u.Id && s.Sucursal.Activa && (s.SucursalId == db.SucursalId || s.SucursalId == 0))
            select new { u.Id, u.UserName, Rol = r.Name }).Distinct().ToListAsync();
        ViewBag.Responsables = usuarios.Select(u => new { u.Id, u.Rol, Nombre = (perfiles.TryGetValue(u.Id, out var nombre) && !string.IsNullOrWhiteSpace(nombre) ? nombre : u.UserName) + " · " + u.Rol }).ToList();
    }
}
