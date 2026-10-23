using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VentaRequest = MiniMarket.Web.Controllers.VentaRequest;

namespace MiniMarket.Web.Services;

public class OperacionVentaService(ApplicationDbContext db)
{
    private const decimal DescuentoMaximoOperadorPct = 30m;

    public async Task<(Venta Venta, bool Repetida)> Registrar(VentaRequest request, string usuario, bool permiteDescuentoMayor = false)
    {
        // Primera fase sin SQL: estructura y límites de todas las listas.
        if (request == null || request.OperacionId == Guid.Empty || request.Detalles == null ||
            (request.AtencionId.HasValue && request.ReservaId.HasValue) ||
            request.Detalles.Count is < 1 or > 200 || request.Detalles.Any(d => d == null || d.ProductoId <= 0 ||
                (d.Lecturas != null ? d.Lecturas.Count is < 1 or > CodigosLecturas.MaxLecturas : d.Cantidad is < 1 or > 100000)))
            throw new InvalidOperationException("Solicitud inválida: cantidades positivas e identificador de operación obligatorios.");
        if (request.Descuento < 0 || decimal.Round(request.Descuento, 2) != request.Descuento ||
            request.Recargo < 0 || decimal.Round(request.Recargo, 2) != request.Recargo ||
            !new[] { "Efectivo", "Tarjeta", "Yape", "Plin", "QR" }.Contains(request.MetodoPago))
            throw new InvalidOperationException("Descuento, recargo o método de pago inválido.");

        foreach (var d in request.Detalles)
        {
            if (d.Lecturas != null)
            {
                d.Lecturas = CodigosLecturas.Normalizar(d.Lecturas);
                d.Cantidad = d.Lecturas.Count;
            }
        }
        CodigosLecturas.ValidarUnicasEnOperacion(request.Detalles.Select(d => d.Lecturas));
        var lineas = request.Detalles.GroupBy(d => d.ProductoId).OrderBy(g => g.Key)
            .Select(g => new { ProductoId = g.Key, Cantidad = checked(g.Sum(d => d.Cantidad)) }).ToList();
        // Conserva la huella de solicitudes históricas sin reserva para sus reintentos.
        var datosHuella = request.ReservaId.HasValue
            ? JsonSerializer.Serialize(new { request.ClienteId, request.Descuento, request.Recargo, request.MetodoPago, request.Total, request.AtencionId, request.ReservaId, Lineas = lineas })
            : JsonSerializer.Serialize(new { request.ClienteId, request.Descuento, request.Recargo, request.MetodoPago, request.Total, request.AtencionId, Lineas = lineas });
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(datosHuella)));

        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var anterior = await db.Ventas.SingleOrDefaultAsync(v => v.OperacionId == request.OperacionId);
        if (anterior != null)
        {
            var propietario = await db.Users.AnyAsync(u => u.Id == anterior.UsuarioId && u.UserName == usuario);
            if (!propietario || anterior.HuellaSolicitud != huella)
                throw new InvalidOperationException("El identificador de operación ya corresponde a otra solicitud.");
            await tx.CommitAsync();
            return (anterior, true);
        }

        // Solo las operaciones nuevas consultan asociaciones; los reintentos recuperan por huella aunque sus códigos ya hayan salido.
        foreach (var d in request.Detalles)
            if (d.Lecturas != null) d.Cantidad = await CodigosLecturas.Cantidad(db, d.ProductoId, d.Lecturas);

        var caja = await db.AperturasCaja.SingleOrDefaultAsync(c => c.UsuarioId == usuario && c.Estado);
        if (caja == null) throw new InvalidOperationException("Debes abrir caja antes de vender.");
        var userId = await db.Users.Where(u => u.UserName == usuario).Select(u => u.Id).SingleAsync();
        if (request.ClienteId.HasValue && !await db.Clientes.AnyAsync(c => c.Id == request.ClienteId && c.Estado))
            throw new InvalidOperationException("El cliente no existe o está inactivo.");

        Atencion atencion = null;
        ReservaProducto reserva = null;
        if (request.ReservaId.HasValue)
        {
            reserva = await db.ReservasProductos.SingleOrDefaultAsync(x => x.Id == request.ReservaId);
            if (reserva == null || reserva.Estado != "Pendiente" || reserva.VentaId != null || reserva.OperacionCobro != request.OperacionId ||
                reserva.ClienteId != request.ClienteId || lineas.Count != 1 || lineas[0].ProductoId != reserva.ProductoId || lineas[0].Cantidad != reserva.Cantidad)
                throw new InvalidOperationException("La reserva no está disponible para cobrar. Actualice las cuentas pendientes.");
        }
        if (request.AtencionId.HasValue)
        {
            atencion = await db.Atenciones.Include(a => a.Mascota).Include(a => a.Cargos).SingleOrDefaultAsync(a => a.Id == request.AtencionId);
            if (atencion == null || atencion.VentaId != null || atencion.Estado is not (EstadoAtencion.Lista or EstadoAtencion.Entregada))
                throw new InvalidOperationException("La cuenta no está lista para cobrar o ya fue cobrada.");
            if (atencion.OperacionCobro != request.OperacionId || atencion.Mascota.ClienteId != request.ClienteId ||
                atencion.Cargos.Count != lineas.Count || lineas.Any(l => !atencion.Cargos.Any(c => c.ProductoId == l.ProductoId && c.Cantidad == l.Cantidad)))
                throw new InvalidOperationException("La cuenta cambió. Revise los conceptos antes de cobrar.");
        }

        var venta = new Venta { OperacionId = request.OperacionId, HuellaSolicitud = huella,
            AperturaCajaId = caja.Id, UsuarioId = userId, ClienteId = request.ClienteId,
            Descuento = request.Descuento, MetodoPago = request.MetodoPago, Fecha = HoraPeru.Ahora };
        foreach (var linea in lineas)
        {
            // Bloquea la fila hasta el commit: lectura de precio/costo y descuento de stock consistentes.
            var p = await db.Productos.FromSqlInterpolated($"SELECT * FROM Productos WITH (UPDLOCK, HOLDLOCK) WHERE Id={linea.ProductoId}").SingleOrDefaultAsync();
            if (p == null || !p.Estado || p.UsoInterno) throw new InvalidOperationException("Producto inexistente, interno o inactivo.");
            if (reserva == null && atencion == null && !p.EsServicio && request.Detalles.Any(d => d.ProductoId == p.Id && !(d.Lecturas?.Count > 0)))
                throw new InvalidOperationException("Las ventas nuevas de productos requieren lecturas; no se admite cantidad manual.");
            if (p.Vencido) throw new InvalidOperationException($"El producto {p.Nombre} está vencido y no se puede vender.");
            if (reserva == null && !p.EsServicio && p.Stock < linea.Cantidad) throw new InvalidOperationException($"Stock insuficiente para {p.Nombre}.");
            if (p.Precio < 0 || p.Costo < 0) throw new InvalidOperationException("El producto tiene un precio o costo inválido.");
            var precio = reserva?.Precio ?? atencion?.Cargos.Single(c => c.ProductoId == p.Id).Precio ?? p.Precio;
            var subtotal = decimal.Round(precio * linea.Cantidad, 2, MidpointRounding.AwayFromZero);
            var lecturasLinea = request.Detalles.Where(d => d.ProductoId == p.Id && d.Lecturas != null).SelectMany(d => d.Lecturas).ToList();
            var detalle = new DetalleVenta { ProductoId = p.Id, Cantidad = linea.Cantidad,
                DescripcionHistorica = reserva?.Descripcion ?? atencion?.Cargos.Single(c => c.ProductoId == p.Id).Descripcion ?? p.Nombre,
                PrecioUnitario = precio, CostoUnitarioHistorico = p.EsServicio ? 0 : p.Costo, SubTotal = subtotal,
                LecturasJson = lecturasLinea.Count > 0 ? JsonSerializer.Serialize(lecturasLinea) : null };
            venta.Detalles.Add(detalle);
            if (reserva == null && !p.EsServicio)
            {
                p.Stock -= linea.Cantidad;
                await RetirarCodigosVendidos(p, lecturasLinea);
            }
        }
        var bruto = venta.Detalles.Sum(d => d.SubTotal);
        if (request.Descuento > bruto) throw new InvalidOperationException("El descuento excede el subtotal.");
        var descuentoMaximo = decimal.Round(bruto * DescuentoMaximoOperadorPct / 100m, 2, MidpointRounding.AwayFromZero);
        if (!permiteDescuentoMayor && request.Descuento > descuentoMaximo)
            throw new InvalidOperationException($"El descuento máximo para recepción es {DescuentoMaximoOperadorPct:N0}%: S/. {descuentoMaximo:N2}.");
        var baseCobro = bruto - request.Descuento;
        var recargoEsperado = PagoReglas.CalcularRecargo(request.MetodoPago, baseCobro);
        if (request.Recargo != recargoEsperado) throw new InvalidOperationException("El recargo de tarjeta no coincide. Actualiza el carrito antes de confirmar.");
        venta.Recargo = recargoEsperado;
        venta.Total = baseCobro + recargoEsperado;
        if (venta.Total <= 0) throw new InvalidOperationException("El total de la venta debe ser mayor a S/. 0.00.");
        // La confirmación del cliente debe coincidir; nunca guardamos importes calculados por el navegador.
        if (request.Total != venta.Total) throw new InvalidOperationException("Los precios cambiaron o el total no coincide. Actualiza el carrito antes de confirmar.");
        if (venta.Total > 9999999999999999.99m) throw new InvalidOperationException("El total excede el límite permitido.");
        var empresa = await db.Configuraciones.AsNoTracking().FirstOrDefaultAsync() ?? new Configuracion
            { NombreEmpresa = "Petmor", Ruc = "00000000000", Direccion = "Configure el sistema", IgvPorcentaje = 18, MonedaSimbolo = "S/." };
        var cliente = request.ClienteId.HasValue ? await db.Clientes.FindAsync(request.ClienteId.Value) : null;
        var perfil = await db.UsuarioPerfiles.FindAsync(userId);
        var baseImponible = decimal.Round(venta.Total / (1 + empresa.IgvPorcentaje / 100m), 2, MidpointRounding.AwayFromZero);
        venta.TicketHistoricoJson = JsonSerializer.Serialize(new TicketHistorico {
            Empresa = empresa, Cliente = cliente?.Nombre ?? "Público General", Documento = cliente?.Documento,
            Cajero = perfil?.NombreCompleto ?? usuario,
            Sede = await db.Sucursales.Where(s => s.Id == db.SucursalId).Select(s => s.Nombre).SingleAsync(),
            BaseImponible = baseImponible, Igv = venta.Total - baseImponible
        });
        db.Ventas.Add(venta);
        await db.SaveChangesAsync();
        if (reserva != null)
        {
            reserva.VentaId = venta.Id;
            reserva.Estado = "Cobrada";
            reserva.FechaCierre = HoraPeru.Ahora;
            reserva.CerradaPor = usuario;
        }
        if (atencion != null)
        {
            atencion.VentaId = venta.Id;
            atencion.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Cuenta cobrada en venta #{venta.Id}" });
        }
        foreach (var d in venta.Detalles.Where(d => reserva == null && !db.Productos.Local.Single(p => p.Id == d.ProductoId).EsServicio))
            db.MovimientosInventario.Add(new MovimientoInventario { Fecha = venta.Fecha, ProductoId = d.ProductoId,
                TipoMovimiento = "SALIDA", Cantidad = d.Cantidad, Usuario = usuario, Referencia = $"Venta #{venta.Id}" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (venta, false);
    }

    public async Task Anular(int id, string usuario, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 300)
            throw new InvalidOperationException("Indica un motivo de anulación (máximo 300 caracteres).");
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        var venta = await db.Ventas.Include(v => v.Detalles).SingleOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("Venta no encontrada.");
        if (venta.Estado == "Anulada") { await tx.CommitAsync(); return; }
        venta.Estado = "Anulada";
        venta.FechaAnulacion = HoraPeru.Ahora;
        venta.AnuladaPor = usuario;
        venta.MotivoAnulacion = motivo.Trim();
        var reservaAnulada = await db.ReservasProductos.SingleOrDefaultAsync(r => r.VentaId == id);
        if (reservaAnulada != null)
        {
            reservaAnulada.Estado = "Anulada";
            reservaAnulada.FechaCierre = HoraPeru.Ahora;
            reservaAnulada.CerradaPor = usuario;
        }
        if (venta.EstadoEmision != "Pendiente") venta.EstadoEmision = "Revisar anulación externa";
        else venta.EstadoEmision = "Cancelada";
        var caja = venta.AperturaCajaId.HasValue ? await db.AperturasCaja.FindAsync(venta.AperturaCajaId.Value) : null;
        if (caja != null && !caja.Estado)
        {
            caja.AjustesAnulaciones += venta.Total;
            venta.AjustePosteriorCierre = true;
        }
        var atencion = await db.Atenciones.SingleOrDefaultAsync(a => a.VentaId == id);
        if (atencion != null)
        {
            atencion.VentaId = null;
            atencion.OperacionCobro = Guid.NewGuid();
            atencion.Eventos.Add(new EventoAtencion { Usuario = usuario, Descripcion = $"Anulación de cobro #{id}: {motivo.Trim()}" });
        }
        foreach (var d in venta.Detalles.OrderBy(d => d.ProductoId))
        {
            var producto = await db.Productos.Include(p => p.Codigos).SingleOrDefaultAsync(p => p.Id == d.ProductoId);
            if (producto == null || producto.EsServicio) continue;
            // Incremento atómico: tampoco se pierde stock ante otra actualización concurrente.
            var filas = await db.ExistenciasSucursales.Where(p => p.ProductoId == d.ProductoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Cantidad, p => p.Cantidad + d.Cantidad));
            if (filas != 1) throw new InvalidOperationException("No se puede restituir un producto eliminado.");
            var lecturas = string.IsNullOrWhiteSpace(d.LecturasJson) ? [] : JsonSerializer.Deserialize<List<string>>(d.LecturasJson) ?? [];
            if (lecturas.Count > 0)
            {
                await CodigosLecturas.Asociar(db, producto, lecturas);
                if (string.IsNullOrWhiteSpace(producto.CodigoBarras)) producto.CodigoBarras = lecturas[0];
            }
            db.MovimientosInventario.Add(new MovimientoInventario { Fecha = HoraPeru.Ahora,
                ProductoId = d.ProductoId, TipoMovimiento = "AJUSTE-ENTRADA", Cantidad = d.Cantidad,
                Usuario = usuario, Referencia = $"Anulación Venta #{id}", Motivo = motivo.Trim() });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task RetirarCodigosVendidos(Producto producto, IEnumerable<string> lecturas)
    {
        var vendidos = (lecturas ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (vendidos.Count == 0) return;
        var codigos = await db.CodigosProducto.Where(c => c.ProductoId == producto.Id).ToListAsync();
        foreach (var codigo in codigos.Where(c => vendidos.Contains(c.Codigo)).ToList())
            db.CodigosProducto.Remove(codigo);
        if (!string.IsNullOrWhiteSpace(producto.CodigoBarras) && vendidos.Contains(producto.CodigoBarras))
            producto.CodigoBarras = codigos.FirstOrDefault(c => !vendidos.Contains(c.Codigo))?.Codigo;
    }
}
