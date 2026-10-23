using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Controllers;

[Authorize(Roles = "Administrador")]
public class UsuariosController(UserManager<IdentityUser> users, RoleManager<IdentityRole> roles,
    ApplicationDbContext db, FactilizaService documentos, ILogger<UsuariosController> logger,
    SignInManager<IdentityUser> signIn) : Controller
{
    public async Task<IActionResult> Index()
    {
        var perfiles = await db.UsuarioPerfiles.AsNoTracking().ToDictionaryAsync(p => p.UsuarioId);
        var sedes = await db.UsuariosSedes.Include(x => x.Sucursal).AsNoTracking().ToDictionaryAsync(x => x.UsuarioId, x => x.SucursalId == 0 ? "Ambas sedes" : x.Sucursal.Nombre);
        var lista = new List<UsuarioViewModel>();
        foreach (var user in await users.Users.ToListAsync())
        {
            perfiles.TryGetValue(user.Id, out var perfil);
            lista.Add(new UsuarioViewModel { Id = user.Id, Email = user.Email,
                Rol = string.Join(", ", await users.GetRolesAsync(user)),
                Dni = perfil?.Dni, NombreCompleto = perfil?.NombreCompleto,
                SedeNombre = sedes.GetValueOrDefault(user.Id, "Sin sede asignada") });
        }
        return View(lista);
    }

    [HttpGet]
    public async Task<IActionResult> ConsultarDni(string dni)
    {
        dni = dni?.Trim();
        if (dni == null || !System.Text.RegularExpressions.Regex.IsMatch(dni, "^[0-9]{8}$"))
            return Json(new { exito = false, mensaje = "Ingrese un DNI de 8 dígitos." });
        var resultado = await documentos.ConsultarDocumentoAsync(dni);
        return Json(new { exito = resultado.Exito, dni, nombres = resultado.Nombres,
            apellidos = resultado.Apellidos, nombreCompleto = resultado.Nombre, mensaje = resultado.Mensaje });
    }

    public IActionResult Create()
    {
        CargarRoles();
        return View(new UsuarioRegistroModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UsuarioRegistroModel model)
    {
        PrepararCorreo(model);
        await ValidarDatos(model, model.Rol);
        if (ModelState.IsValid)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var correo = model.Email.Trim().ToLowerInvariant();
                var user = new IdentityUser { UserName = correo, Email = correo, EmailConfirmed = true };
                if (Comprobar(await users.CreateAsync(user, model.Password)) &&
                    Comprobar(await users.AddToRolesAsync(user, model.RolesAdicionales.Append(model.Rol).Distinct())))
                {
                    db.UsuarioPerfiles.Add(new UsuarioPerfil { UsuarioId = user.Id, Dni = model.Dni,
                        Nombres = model.Nombres.Trim(), Apellidos = model.Apellidos.Trim() });
                    db.UsuariosSedes.Add(new UsuarioSede { UsuarioId = user.Id, SucursalId = model.SucursalId.Value });
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    TempData["Exito"] = "Usuario y datos personales registrados.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "No se pudo guardar. Verifica que el DNI y el correo no estén registrados."); }
            await tx.RollbackAsync();
        }
        CargarRoles(model.Rol);
        return View(model);
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = id == null ? null : await users.FindByIdAsync(id);
        if (user == null) return NotFound();
        var perfil = await db.UsuarioPerfiles.FindAsync(id);
        var sedeUsuario = await db.UsuariosSedes.FindAsync(id);
        var asignados = await users.GetRolesAsync(user);
        var rol = asignados.Contains("Administrador") ? "Administrador" : asignados.FirstOrDefault() ?? "";
        CargarRoles(rol);
        return View(new UsuarioEditModel { Id = id, Email = user.Email, UsuarioAcceso = user.Email?.Split('@')[0], RolActual = rol, NuevoRol = rol,
            Dni = perfil?.Dni, Nombres = perfil?.Nombres, Apellidos = perfil?.Apellidos, SucursalId = sedeUsuario?.SucursalId,
            RolesAdicionales = asignados.Where(x => x != rol).ToList() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UsuarioEditModel model)
    {
        PrepararCorreo(model);
        var user = model.Id == null ? null : await users.FindByIdAsync(model.Id);
        if (user == null) return NotFound();
        var actuales = await users.GetRolesAsync(user);
        model.RolActual = actuales.FirstOrDefault() ?? "";
        await ValidarDatos(model, model.NuevoRol, user.Id);
        if (users.GetUserId(User) == user.Id && model.NuevoRol != "Administrador")
            ModelState.AddModelError("NuevoRol", "No puedes quitar el rol de administrador a tu propia cuenta.");
        if (ModelState.IsValid)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await ActualizacionComercial.BloquearOperaciones(db);
                var asignacion = await db.UsuariosSedes.FindAsync(user.Id);
                if (asignacion != null && asignacion.SucursalId != model.SucursalId &&
                    (await db.AperturasCaja.IgnoreQueryFilters().AnyAsync(c => c.UsuarioId == user.UserName && c.Estado) ||
                     await db.Atenciones.IgnoreQueryFilters().AnyAsync(a => a.ResponsableId == user.Id && a.Estado != EstadoAtencion.Entregada && a.Estado != EstadoAtencion.Cancelada)))
                {
                    ModelState.AddModelError("SucursalId", "Cierre la caja y complete o reasigne las atenciones pendientes antes de cambiar la sede del usuario.");
                    CargarRoles(model.NuevoRol);
                    return View(model);
                }
                bool valido = true;
                var nombreAnterior = user.UserName;
                var correoNuevo = model.Email.Trim().ToLowerInvariant();
                var cambiaCorreo = !string.Equals(user.UserName, correoNuevo, StringComparison.Ordinal);
                if (cambiaCorreo)
                    valido = Comprobar(await users.SetEmailAsync(user, correoNuevo)) &&
                        Comprobar(await users.SetUserNameAsync(user, correoNuevo));
                if (valido && !string.IsNullOrEmpty(model.NuevaPassword))
                    valido = Comprobar(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), model.NuevaPassword));
                var solicitados = model.RolesAdicionales.Append(model.NuevoRol).Distinct().ToList();
                if (!solicitados.Contains("Administrador") && await db.Atenciones.IgnoreQueryFilters().AnyAsync(a => a.ResponsableId == user.Id &&
                    a.Estado != EstadoAtencion.Cancelada && a.Estado != EstadoAtencion.Entregada &&
                    ((a.Area == AreaAtencion.Veterinaria && !solicitados.Contains("Veterinario")) || (a.Area == AreaAtencion.Spa && !solicitados.Contains("Groomer")))))
                {
                    await tx.RollbackAsync();
                    ModelState.AddModelError("NuevoRol", "Complete las atenciones pendientes antes de retirar el rol profesional.");
                    CargarRoles(model.NuevoRol); return View(model);
                }
                var cambiaPermisos = !actuales.OrderBy(x => x).SequenceEqual(solicitados.OrderBy(x => x)) || asignacion?.SucursalId != model.SucursalId;
                if (valido && !actuales.OrderBy(x => x).SequenceEqual(solicitados.OrderBy(x => x)))
                    valido = Comprobar(await users.RemoveFromRolesAsync(user, actuales)) && Comprobar(await users.AddToRolesAsync(user, solicitados));
                if (valido && cambiaPermisos) valido = Comprobar(await users.UpdateSecurityStampAsync(user));
                if (valido)
                {
                    if (cambiaCorreo)
                    {
                        await db.AperturasCaja.IgnoreQueryFilters().Where(c => c.UsuarioId == nombreAnterior)
                            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsuarioId, correoNuevo));
                        await db.MovimientosInventario.IgnoreQueryFilters().Where(m => m.Usuario == nombreAnterior)
                            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Usuario, correoNuevo));
                        await db.Ventas.IgnoreQueryFilters().Where(v => v.AnuladaPor == nombreAnterior)
                            .ExecuteUpdateAsync(s => s.SetProperty(v => v.AnuladaPor, correoNuevo));
                        valido = Comprobar(await users.UpdateSecurityStampAsync(user));
                    }
                }
                if (valido)
                {
                    if (asignacion == null) db.UsuariosSedes.Add(new UsuarioSede { UsuarioId = user.Id, SucursalId = model.SucursalId.Value });
                    else asignacion.SucursalId = model.SucursalId.Value;
                    var perfil = await db.UsuarioPerfiles.FindAsync(user.Id);
                    if (perfil == null) { perfil = new UsuarioPerfil { UsuarioId = user.Id }; db.UsuarioPerfiles.Add(perfil); }
                    perfil.Dni = model.Dni;
                    perfil.Nombres = model.Nombres.Trim();
                    perfil.Apellidos = model.Apellidos.Trim();
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    if ((cambiaCorreo || cambiaPermisos) && users.GetUserId(User) == user.Id)
                        await signIn.RefreshSignInAsync(user);
                    TempData["Exito"] = "Datos personales y permisos actualizados.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (DbUpdateException) { ModelState.AddModelError("", "No se pudo guardar. Verifica que el DNI no pertenezca a otro usuario."); }
            await tx.RollbackAsync();
        }
        CargarRoles(model.NuevoRol);
        return View(model);
    }

    private void PrepararCorreo(UsuarioDatosModel model)
    {
        if (model.UsuarioAcceso == null) return; // Compatibilidad con solicitudes anteriores que envían Email completo.
        model.UsuarioAcceso = model.UsuarioAcceso.Trim().ToLowerInvariant();
        if (model.UsuarioAcceso.Contains('@')) model.UsuarioAcceso = model.UsuarioAcceso.Split('@')[0];
        model.Email = model.UsuarioAcceso + "@petmor.pe";
        ModelState.Remove(nameof(model.Email));
        TryValidateModel(model);
    }

    private async Task ValidarDatos(UsuarioDatosModel model, string rol, string usuarioId = null)
    {
        if (!model.SucursalId.HasValue || !await db.Sucursales.AnyAsync(s => s.Id == model.SucursalId.Value && s.Activa))
            ModelState.AddModelError(nameof(model.SucursalId), "Seleccione una sede activa.");
        if (!string.IsNullOrEmpty(model.Dni) && await db.UsuarioPerfiles.AnyAsync(p => p.Dni == model.Dni && p.UsuarioId != usuarioId))
            ModelState.AddModelError(nameof(model.Dni), "Este DNI ya pertenece a otro usuario.");
        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var correo = model.Email.Trim().ToUpperInvariant();
            if (await users.Users.AnyAsync(u => u.Id != usuarioId && (u.NormalizedEmail == correo || u.NormalizedUserName == correo)))
                ModelState.AddModelError(nameof(model.Email), "Este correo ya pertenece a otro usuario.");
        }
        var permitidos = new[] { "Administrador", "Recepcionista", "Veterinario", "Groomer" };
        model.RolesAdicionales ??= new();
        if (!permitidos.Contains(rol) || model.RolesAdicionales.Any(x => !permitidos.Contains(x) || x == "Administrador") || !await roles.RoleExistsAsync(rol ?? ""))
            ModelState.AddModelError("", "Seleccione un rol válido.");
    }
    private bool Comprobar(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return result.Succeeded;
    }
    private void CargarRoles(string elegido = null)
    {
        ViewBag.Roles = new SelectList(roles.Roles, "Name", "Name", elegido);
        var sedes = db.Sucursales.AsNoTracking().Where(x => x.Activa && x.Id > 0).OrderBy(x => x.Id)
            .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre }).ToList();
        if (db.Sucursales.AsNoTracking().Any(x => x.Id == 0 && x.Activa))
            sedes.Insert(0, new SelectListItem { Value = "0", Text = "Ambas sedes" });
        ViewBag.Sedes = sedes;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { exito = false, mensaje = "Seleccione un usuario válido." });
        if ((await users.GetUserAsync(User))?.Id == id)
            return Json(new { exito = false, mensaje = "No puedes eliminar tu propia cuenta." });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await ActualizacionComercial.BloquearOperaciones(db);
            var user = await users.FindByIdAsync(id);
            if (user == null) return Json(new { exito = false, mensaje = "Usuario no encontrado. Actualiza el listado." });

            // No se eliminan movimientos ni se deja el historial sin su responsable.
            var vinculados = new List<string>();
            if (await db.Atenciones.IgnoreQueryFilters().AnyAsync(a => a.ResponsableId == id || a.CreadoPor == user.UserName) ||
                await db.EventosAtencion.IgnoreQueryFilters().AnyAsync(e => e.Usuario == user.UserName)) vinculados.Add("atenciones veterinarias o spa");
            if (await db.Ventas.IgnoreQueryFilters().AnyAsync(v => v.UsuarioId == id)) vinculados.Add("ventas");
            if (await db.Compras.IgnoreQueryFilters().AnyAsync(c => c.UsuarioId == id)) vinculados.Add("compras");
            if (await db.Gastos.IgnoreQueryFilters().AnyAsync(g => g.UsuarioId == id)) vinculados.Add("gastos");
            // Caja y auditoría guardan el nombre de acceso, no el Id de Identity.
            if (await db.AperturasCaja.IgnoreQueryFilters().AnyAsync(c => c.UsuarioId == user.UserName)) vinculados.Add("registros de caja");
            if (await db.MovimientosInventario.IgnoreQueryFilters().AnyAsync(m => m.Usuario == user.UserName) ||
                await db.Ventas.IgnoreQueryFilters().AnyAsync(v => v.AnuladaPor == user.UserName)) vinculados.Add("movimientos o anulaciones");
            if (vinculados.Count > 0)
                return Json(new { exito = false, codigo = "USUARIO_CON_HISTORIAL",
                    mensaje = $"No se puede eliminar este usuario porque tiene {string.Join(", ", vinculados)} asociados. Se conserva la cuenta para mantener el historial del negocio." });

            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
                return Json(new { exito = false, mensaje = "No se pudo eliminar la cuenta. Actualiza el listado e inténtalo nuevamente." });
            await tx.CommitAsync();
            return Json(new { exito = true, mensaje = "Usuario eliminado correctamente." });
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "No se pudo eliminar el usuario {UsuarioId} por una restricción de datos", id);
            return Conflict(new { exito = false, mensaje = "No se pudo eliminar porque la cuenta tiene registros asociados o fue modificada mientras se procesaba la solicitud. Actualiza el listado." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error eliminando el usuario {UsuarioId}", id);
            return StatusCode(503, new { exito = false, mensaje = "El servidor no pudo completar la eliminación. Actualiza el listado para comprobar su estado antes de reintentar." });
        }
    }
}
