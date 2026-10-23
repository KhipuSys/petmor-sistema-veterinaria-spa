using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Infrastructure;
using MiniMarket.Web.Models;
using MiniMarket.Web.Models.ViewModels;

namespace MiniMarket.Web.Services;

public class AtencionService(ApplicationDbContext db)
{
    public static bool Cerrada(Atencion a) => a.Estado is EstadoAtencion.Lista or EstadoAtencion.Entregada or EstadoAtencion.Cancelada;

    public async Task<int> Agendar(AgendaForm f, string usuario)
    {
        if (f.Duracion is < 10 or > 480)
            throw new InvalidOperationException("Complete una duración válida.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var servicio = await db.Productos.SingleOrDefaultAsync(x => x.Id == f.ServicioId && x.EsServicio && x.Estado && !x.UsoInterno);
        if (servicio == null) throw new InvalidOperationException("Seleccione un servicio disponible.");
        f.Area = servicio.AreaServicio ?? AreaAtencion.Veterinaria;
        if (servicio.DuracionMinutos > 0) f.Duracion = servicio.DuracionMinutos;
        var motivo = string.IsNullOrWhiteSpace(f.Motivo) ? servicio.Nombre : f.Motivo.Trim();
        if (!await db.Mascotas.AnyAsync(x => x.Id == f.MascotaId && x.Activa && x.Cliente.Estado))
            throw new InvalidOperationException("Seleccione una mascota y propietario activos.");
        var rol = f.Area == AreaAtencion.Spa ? "Groomer" : "Veterinario";
        var elegible = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                              where ur.UserId == f.ResponsableId && (r.Name == rol || r.Name == "Administrador") select ur).AnyAsync();
        if (!elegible) throw new InvalidOperationException("El responsable no tiene el perfil requerido para esta área.");
        if (!await db.UsuariosSedes.AnyAsync(s => s.UsuarioId == f.ResponsableId && s.Sucursal.Activa && (s.SucursalId == db.SucursalId || s.SucursalId == 0)))
            throw new InvalidOperationException("El responsable debe pertenecer a la sede activa.");
        var inicio = HoraPeru.Ahora;
        var fin = inicio.AddMinutes(f.Duracion);
        if (await db.Atenciones.IgnoreQueryFilters().AnyAsync(x => x.Estado != EstadoAtencion.Cancelada && x.Estado != EstadoAtencion.Entregada &&
            (x.ResponsableId == f.ResponsableId || x.MascotaId == f.MascotaId) && (x.Estado == EstadoAtencion.EnAtencion || (x.Inicio < fin && x.FinPrevisto > inicio))))
            throw new InvalidOperationException("La mascota o responsable ya tiene una atención en ese horario.");
        var a = new Atencion { MascotaId = f.MascotaId, Area = f.Area, Inicio = inicio, FinPrevisto = fin,
            ResponsableId = f.ResponsableId, Motivo = motivo, Recepcion = f.Recepcion, CreadoPor = usuario,
            Estado = EstadoAtencion.EnEspera };
        a.Cargos.Add(new CargoAtencion { ProductoId = servicio.Id, Descripcion = servicio.Nombre, Cantidad = 1, Precio = servicio.Precio });
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = "Recepción registrada" });
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Servicio inicial: {servicio.Nombre}" });
        db.Atenciones.Add(a);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return a.Id;
    }

    public async Task CambiarEstado(int id, EstadoAtencion destino, string usuario, bool puedeAtender, string motivo)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var a = await db.Atenciones.Include(x => x.Consumos).SingleAsync(x => x.Id == id);
        if (a.Estado == destino) { await tx.CommitAsync(); return; }
        bool valido = (a.Estado, destino) switch
        {
            (EstadoAtencion.Programada, EstadoAtencion.Confirmada or EstadoAtencion.EnEspera) => true,
            (EstadoAtencion.Confirmada, EstadoAtencion.EnEspera) => true,
            (EstadoAtencion.EnEspera, EstadoAtencion.EnAtencion) => true,
            (EstadoAtencion.EnAtencion, EstadoAtencion.Lista) => true,
            (EstadoAtencion.Lista, EstadoAtencion.Entregada) => true,
            (_, EstadoAtencion.Cancelada) => a.Estado is EstadoAtencion.Programada or EstadoAtencion.Confirmada or EstadoAtencion.EnEspera,
            _ => false
        };
        if (!valido) throw new InvalidOperationException("La transición no corresponde al estado actual o a sus permisos.");
        if (destino == EstadoAtencion.Cancelada && (string.IsNullOrWhiteSpace(motivo) || a.Consumos.Count > 0 || a.VentaId != null))
            throw new InvalidOperationException("Indique el motivo. Una atención con consumos o cobros no se puede cancelar.");
        if (destino == EstadoAtencion.Lista && !puedeAtender)
            throw new InvalidOperationException("Solo el profesional asignado puede finalizar la atención.");
        if (destino == EstadoAtencion.Lista && !await db.CargosAtencion.AnyAsync(x => x.AtencionId == id))
            throw new InvalidOperationException("Agregue a la cuenta los servicios realizados antes de finalizar la atención.");
        a.Estado = destino;
        if (destino == EstadoAtencion.Cancelada)
        {
            a.MotivoCancelacion = motivo.Trim();
            a.CanceladaPor = usuario;
            a.FechaCancelacion = HoraPeru.Ahora;
        }
        if (destino == EstadoAtencion.EnAtencion) a.InicioReal = HoraPeru.Ahora;
        if (destino == EstadoAtencion.Lista) a.FinReal = HoraPeru.Ahora;
        if (destino == EstadoAtencion.Entregada) a.Entrega = HoraPeru.Ahora;
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Estado: {destino.Texto()}. {motivo}".Trim() });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task GuardarFicha(int id, FichaForm f, string usuario)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var a = await db.Atenciones.Include(x => x.Mascota).SingleAsync(x => x.Id == id);
        if (a.Estado != EstadoAtencion.EnAtencion) throw new InvalidOperationException("La ficha solo se modifica durante la atención.");
        a.Evaluacion = f.Evaluacion; a.Diagnostico = f.Diagnostico; a.Tratamiento = f.Tratamiento;
        a.Indicaciones = f.Indicaciones; a.Peso = f.Peso; a.ProximoControl = f.ProximoControl;
        if (f.Peso.HasValue) a.Mascota.Peso = f.Peso;
        a.Bano = f.Bano; a.Corte = f.Corte; a.Unas = f.Unas; a.Oidos = f.Oidos; a.Perfume = f.Perfume;
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = "Ficha de atención actualizada" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task Reprogramar(int id, DateTime inicio, int duracion, string usuario)
    {
        if (inicio < HoraPeru.Ahora || inicio > HoraPeru.Ahora.AddYears(2) || duracion is < 10 or > 480)
            throw new InvalidOperationException("Seleccione fecha futura y duración entre 10 y 480 minutos.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var a = await db.Atenciones.SingleAsync(x => x.Id == id);
        if (a.Estado is not (EstadoAtencion.Programada or EstadoAtencion.Confirmada))
            throw new InvalidOperationException("Solo se reprograman citas que todavía no ingresaron a atención.");
        var fin = inicio.AddMinutes(duracion);
        if (await db.Atenciones.IgnoreQueryFilters().AnyAsync(x => x.Id != id && x.Estado != EstadoAtencion.Cancelada && x.Estado != EstadoAtencion.Entregada &&
            (x.MascotaId == a.MascotaId || x.ResponsableId == a.ResponsableId) && x.Inicio < fin && x.FinPrevisto > inicio))
            throw new InvalidOperationException("El nuevo horario coincide con otra atención de la mascota o profesional.");
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Reprogramada: {a.Inicio:dd/MM/yyyy HH:mm} → {inicio:dd/MM/yyyy HH:mm}" });
        a.Inicio = inicio; a.FinPrevisto = fin; a.Estado = EstadoAtencion.Programada;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task Cargo(int id, int productoId, int cantidad, string usuario, bool quitar = false, List<string> lecturas = null)
    {
        if (!quitar && lecturas?.Count > 0) cantidad = await CodigosLecturas.Cantidad(db, productoId, lecturas);
        if (cantidad is < 1 or > 10000) throw new InvalidOperationException("Cantidad inválida.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var a = await db.Atenciones.Include(x => x.Cargos).SingleAsync(x => x.Id == id);
        if (a.VentaId != null || a.Estado is EstadoAtencion.Cancelada or EstadoAtencion.Entregada)
            throw new InvalidOperationException("La cuenta está bloqueada.");
        var p = await db.Productos.SingleAsync(x => x.Id == productoId);
        if (!p.Estado || p.UsoInterno) throw new InvalidOperationException("Este artículo no está disponible para cobro.");
        if (!quitar && !p.EsServicio && !(lecturas?.Count > 0)) throw new InvalidOperationException("Agregar productos físicos a la cuenta requiere lecturas.");
        var cargo = a.Cargos.SingleOrDefault(x => x.ProductoId == productoId);
        if (quitar) { if (cargo != null) db.CargosAtencion.Remove(cargo); }
        else if (cargo != null)
        {
            if (cargo.Cantidad + cantidad > 10000) throw new InvalidOperationException("La cantidad acumulada excede el límite permitido.");
            cargo.Cantidad += cantidad;
        }
        else a.Cargos.Add(new CargoAtencion { ProductoId = productoId, Descripcion = p.Nombre, Cantidad = cantidad, Precio = p.Precio });
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Cuenta: {(quitar ? "retiro" : "cargo")} {p.Nombre}" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task Consumir(int id, int productoId, int cantidad, Guid operacion, string usuario, List<string> lecturas = null)
    {
        if (lecturas?.Count > 0) cantidad = await CodigosLecturas.Cantidad(db, productoId, lecturas);
        if (cantidad <= 0 || cantidad > 10000 || operacion == Guid.Empty) throw new InvalidOperationException("Cantidad inválida.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var previo = await db.ConsumosAtencion.SingleOrDefaultAsync(x => x.OperacionId == operacion);
        if (previo != null)
        {
            if (previo.AtencionId != id || previo.ProductoId != productoId || previo.Cantidad != cantidad)
                throw new InvalidOperationException("El identificador ya corresponde a otro consumo.");
            await tx.CommitAsync(); return;
        }
        if (!(lecturas?.Count > 0)) throw new InvalidOperationException("Los consumos nuevos requieren lecturas.");
        var a = await db.Atenciones.SingleAsync(x => x.Id == id);
        if (a.Estado != EstadoAtencion.EnAtencion) throw new InvalidOperationException("Inicie la atención antes de registrar consumos.");
        var p = await db.Productos.FromSqlInterpolated($"SELECT * FROM Productos WITH (UPDLOCK,HOLDLOCK) WHERE Id={productoId}").SingleAsync();
        if (!p.Estado || p.EsServicio || p.Stock < cantidad) throw new InvalidOperationException("Insumo no disponible o stock insuficiente.");
        p.Stock -= cantidad;
        var consumo = new ConsumoAtencion { ProductoId = p.Id, Cantidad = cantidad, Costo = p.Costo, OperacionId = operacion, Usuario = usuario };
        a.Consumos.Add(consumo);
        db.MovimientosInventario.Add(new MovimientoInventario { ProductoId = p.Id, Cantidad = cantidad, TipoMovimiento = "SALIDA",
            Usuario = usuario, Fecha = HoraPeru.Ahora, Referencia = $"Consumo atención #{id}", Motivo = "Uso interno; no se descuenta otra vez al cobrar" });
        a.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Consumo: {cantidad} de {p.Nombre}" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
