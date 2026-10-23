using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;

namespace MiniMarket.Web.Services;

public class ReservaService(ApplicationDbContext db)
{
    public async Task<ReservaProducto> Crear(ReservaForm form, string usuario)
    {
        if (form.Lecturas?.Count > 0) form.Cantidad = await CodigosLecturas.Cantidad(db, form.ProductoId, form.Lecturas);
        if (form.OperacionId == Guid.Empty || form.Cantidad is < 1 or > 100000 || form.Observaciones?.Length > 300)
            throw new InvalidOperationException("Indique una cantidad válida y observaciones de hasta 300 caracteres.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var anterior = await db.ReservasProductos.SingleOrDefaultAsync(x => x.OperacionId == form.OperacionId);
        if (anterior != null)
        {
            if (anterior.Usuario != usuario || anterior.ClienteId != form.ClienteId || anterior.ProductoId != form.ProductoId || anterior.Cantidad != form.Cantidad || anterior.Observaciones != form.Observaciones?.Trim())
                throw new InvalidOperationException("La operación corresponde a otra reserva.");
            await tx.CommitAsync();
            return anterior;
        }
        if (!(form.Lecturas?.Count > 0)) throw new InvalidOperationException("Las reservas nuevas requieren lecturas.");
        if (!await db.Clientes.AnyAsync(x => x.Id == form.ClienteId && x.Estado))
            throw new InvalidOperationException("Seleccione un cliente activo.");
        var producto = await db.Productos.SingleOrDefaultAsync(x => x.Id == form.ProductoId);
        if (producto == null || !producto.Estado || producto.EsServicio || producto.UsoInterno || producto.Vencido)
            throw new InvalidOperationException("Seleccione un producto activo, vendible y no vencido.");
        if (producto.Precio <= 0 || producto.Stock < form.Cantidad)
            throw new InvalidOperationException("El producto debe tener precio mayor a cero y stock disponible suficiente.");
        var reserva = new ReservaProducto { ClienteId = form.ClienteId, ProductoId = producto.Id, Cantidad = form.Cantidad,
            Precio = producto.Precio, Descripcion = producto.Nombre, Observaciones = form.Observaciones?.Trim(), Usuario = usuario, OperacionId = form.OperacionId };
        producto.Stock -= form.Cantidad;
        db.ReservasProductos.Add(reserva);
        await db.SaveChangesAsync();
        db.MovimientosInventario.Add(new MovimientoInventario { ProductoId = producto.Id, Cantidad = form.Cantidad,
            Fecha = HoraPeru.Ahora, TipoMovimiento = "SALIDA", Usuario = usuario, Referencia = $"Reserva #{reserva.Id}", Motivo = "Separación de stock disponible; pendiente de pago" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reserva;
    }

    public async Task Cancelar(int id, string usuario)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var reserva = await db.ReservasProductos.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Reserva no encontrada en esta sede.");
        if (reserva.Estado == "Cancelada") { await tx.CommitAsync(); return; }
        if (reserva.Estado != "Pendiente") throw new InvalidOperationException("Una reserva cobrada no se puede cancelar; utilice la anulación de venta.");
        var producto = await db.Productos.SingleAsync(x => x.Id == reserva.ProductoId);
        if (producto.Stock > int.MaxValue - reserva.Cantidad) throw new InvalidOperationException("El stock supera el máximo permitido.");
        producto.Stock += reserva.Cantidad;
        reserva.Estado = "Cancelada";
        reserva.FechaCierre = HoraPeru.Ahora;
        reserva.CerradaPor = usuario;
        db.MovimientosInventario.Add(new MovimientoInventario { ProductoId = producto.Id, Cantidad = reserva.Cantidad,
            Fecha = HoraPeru.Ahora, TipoMovimiento = "AJUSTE-ENTRADA", Usuario = usuario, Referencia = $"Cancelación Reserva #{id}", Motivo = "Liberación de producto separado" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
