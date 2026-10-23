using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Controllers;
using MiniMarket.Web.Data;
using MiniMarket.Web.Infrastructure;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;

static class ReservasTests
{
    public static async Task Run()
    {
        var nombre = "Petmor_ReservasTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={nombre};Trusted_Connection=True;TrustServerCertificate=True").Options;
        ApplicationDbContext Db(int sede = 1) => new(options, new SedeActual { Id = sede });
        void Check(bool ok, string mensaje) { if (!ok) throw new Exception(mensaje); }
        async Task Rechaza(Func<Task> accion) { try { await accion(); } catch (InvalidOperationException) { return; } throw new Exception("Operación inválida aceptada"); }
        int productoId, clienteId;
        try
        {
            await using (var db = Db())
            {
                await ActualizacionComercial.Aplicar(db);
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Productos DROP COLUMN ConCaducidad, FechaVencimiento;");
                await ActualizacionComercial.Aplicar(db);
                await ActualizacionComercial.Aplicar(db);
                await ActualizacionVeterinaria.Aplicar(db);
                // Simula una instalación anterior que todavía no tiene la tabla.
                await db.Database.ExecuteSqlRawAsync("DROP TABLE ReservasProductos;");
                await ActualizacionSucursales.Aplicar(db);
                await ActualizacionSucursales.Aplicar(db);
                db.Users.Add(new IdentityUser { Id = "tester", UserName = "tester" });
                var cliente = new Cliente { Nombre = "Cliente reserva", Documento = "12345678", Telefono = "999999999" };
                var producto = new Producto { Nombre = "Producto reserva", CodigoBarras = "001234567890", Precio = 10, Costo = 4, Stock = 5,
                    Categoria = new Categoria { Nombre = "Prueba", Estado = true }, ConCaducidad = true, FechaVencimiento = HoraPeru.Hoy.AddDays(5) };
                producto.Codigos.AddRange([new CodigoProducto { Codigo = "ALT-RESERVA" }, new CodigoProducto { Codigo = "ALT-RESERVA-2" }, new CodigoProducto { Codigo = "ALT-RESERVA-3" }]);
                db.Clientes.Add(cliente); db.Productos.Add(producto); await db.SaveChangesAsync();
                productoId = producto.Id; clienteId = cliente.Id;
            }
            var form = new ReservaForm { ClienteId = clienteId, ProductoId = productoId, Cantidad = 999,
                Lecturas = ["001234567890", "ALT-RESERVA", "ALT-RESERVA-2"] };
            async Task<ReservaProducto> Crear(ReservaForm f) { await using var db = Db(); return await new ReservaService(db).Crear(f, "tester"); }
            var reservas = await Task.WhenAll(Crear(form), Crear(form));
            Check(reservas[0].Id == reservas[1].Id, "Reserva duplicada al reintentar");
            var reserva = reservas[0];
            var historica = await Crear(new ReservaForm { OperacionId = form.OperacionId, ClienteId = clienteId, ProductoId = productoId, Cantidad = 3 });
            Check(historica.Id == reserva.Id, "Se rompió el reintento histórico sin lecturas");
            await Rechaza(() => Crear(new ReservaForm { ClienteId = clienteId, ProductoId = productoId, Cantidad = 1 }));
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 2, "Stock no separado exactamente una vez");
                Check(!await db.Ventas.AnyAsync(), "Reservar registró una venta");
                Check(await DependenciasOperacion.ProductoPendiente(db, productoId), "No protege producto con reserva");
                Check(await DependenciasOperacion.ClientePendiente(db, clienteId), "No protege cliente con reserva");
                (await db.Productos.FindAsync(productoId)).Precio = 20;
                await db.SaveChangesAsync();
            }
            await Rechaza(() => Crear(new ReservaForm { ClienteId = clienteId, ProductoId = productoId, Cantidad = 3, Lecturas = ["ALT-RESERVA", "ALT-RESERVA", "ALT-RESERVA-3"] }));
            await using (var db = Db(2))
            {
                Check(!await db.ReservasProductos.AnyAsync(), "Reserva visible en otra sede");
                await Rechaza(() => new ReservaService(db).Cancelar(reserva.Id, "tester"));
            }
            var request = new VentaRequest { ReservaId = reserva.Id, ClienteId = clienteId, OperacionId = reserva.OperacionCobro,
                Total = 30, MetodoPago = "Efectivo", Detalles = new() { new() { ProductoId = productoId, Cantidad = 3 } } };
            async Task<(Venta Venta, bool Repetida)> Cobrar() { await using var db = Db(); return await new OperacionVentaService(db).Registrar(request, "tester"); }
            await Rechaza(async () => { await Cobrar(); });
            await using (var db = Db()) { db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "tester", Estado = true }); await db.SaveChangesAsync(); }
            var ventas = await Task.WhenAll(Cobrar(), Cobrar());
            Check(ventas[0].Venta.Id == ventas[1].Venta.Id, "Cobro duplicado");
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 2, "Cobro descontó stock otra vez");
                Check(await db.MovimientosInventario.CountAsync() == 1, "Cobro duplicó salida de Kardex");
                Check((await db.ReservasProductos.FindAsync(reserva.Id)).Estado == "Cobrada", "Reserva sigue pendiente");
                await Rechaza(() => new ReservaService(db).Cancelar(reserva.Id, "tester"));
            }
            await using (var db = Db()) await new OperacionVentaService(db).Anular(ventas[0].Venta.Id, "tester", "Prueba");
            await using (var db = Db()) Check((await db.Productos.FindAsync(productoId)).Stock == 5, "Anulación no devolvió unidades");
            var cancelar = await Crear(new ReservaForm { ClienteId = clienteId, ProductoId = productoId, Cantidad = 2, Lecturas = ["ALT-RESERVA", "ALT-RESERVA-3"] });
            async Task Cancelar() { await using var db = Db(); await new ReservaService(db).Cancelar(cancelar.Id, "tester"); }
            await Task.WhenAll(Cancelar(), Cancelar());
            await using (var db = Db())
            {
                var p = await db.Productos.FindAsync(productoId);
                Check(p.Stock == 5, "Cancelación devolvió unidades más de una vez");
                p.FechaVencimiento = HoraPeru.Hoy.AddDays(-1); await db.SaveChangesAsync();
            }
            await Rechaza(() => Crear(new ReservaForm { ClienteId = clienteId, ProductoId = productoId, Cantidad = 1, Lecturas = ["ALT-RESERVA"] }));
            await using (var db = Db()) await Rechaza(async () => { await new OperacionVentaService(db).Registrar(new VentaRequest {
                OperacionId = Guid.NewGuid(), ClienteId = clienteId, MetodoPago = "Efectivo", Total = 20,
                Detalles = new() { new() { ProductoId = productoId, Cantidad = 1, Lecturas = ["ALT-RESERVA"] } } }, "tester"); });
            var invalido = new Producto { Nombre = "Sin fecha", ConCaducidad = true };
            Check(!Validator.TryValidateObject(invalido, new ValidationContext(invalido), new List<ValidationResult>(), true), "Caducidad sin fecha aceptada");
            Check(!new Producto { ConCaducidad = true, FechaVencimiento = HoraPeru.Hoy }.Vencido, "Vence hoy marcado antes de tiempo");
            Console.WriteLine("PASS RESERVAS: migración repetible; stock separado; reintento concurrente; sedes; caja obligatoria; precio reservado; cobro y cancelación únicos; anulación restituye stock.");
            Console.WriteLine("PASS CADUCIDAD: fecha obligatoria; vencidos rechazados en venta y reserva; fecha de hoy válida.");
        }
        finally { await using var db = Db(); await db.Database.EnsureDeletedAsync(); }
    }
}
