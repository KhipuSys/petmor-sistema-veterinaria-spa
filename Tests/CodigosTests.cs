using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using MiniMarket.Web.Controllers;
using Microsoft.AspNetCore.Identity;

public static class CodigosTests
{
    public static async Task Run()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(
            $"Server=localhost\\SQLEXPRESS;Database=MiniMarket_Codigos_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new ApplicationDbContext(options);
        void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Se aceptó un código inválido"); }
        try
        {
            await ActualizacionComercial.Aplicar(db);
            var p = new Producto { Nombre = "Lecturas", CodigoBarras = "00001", Categoria = new Categoria { Nombre = "Test" }, Stock = 7 };
            db.Productos.Add(p); await db.SaveChangesAsync();
            // Instalación real anterior: solo la columna principal, sin tabla relacionada.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE CodigosProducto;");
            db.ChangeTracker.Clear();
            await ActualizacionCodigos.Aplicar(db); await ActualizacionCodigos.Aplicar(db);
            Assert(await db.CodigosProducto.CountAsync(c => c.ProductoId == p.Id) == 1, "Migración duplicó el código");
            var nuevo = new Producto { Nombre = "Nuevo", CategoriaId = p.CategoriaId, Stock = 999, Precio = 10, Costo = 4 };
            await Reject(() => CodigosLecturas.PrepararAlta(db, new Producto(), ["002", "002"]));
            await CodigosLecturas.PrepararAlta(db, nuevo, ["002", "003", "004"]);
            Assert(nuevo.Stock == 3 && nuevo.Codigos.Count == 3, "No cuenta códigos únicos o no agrupa códigos");
            db.Productos.Add(nuevo); await db.SaveChangesAsync();
            Assert(await CodigosLecturas.Cantidad(db, nuevo.Id, ["002", "003", "004"]) == 3, "Código alternativo no resuelto");
            await Reject(() => CodigosLecturas.Cantidad(db, nuevo.Id, ["002", "002"]));
            await Reject(() => CodigosLecturas.PrepararAlta(db, new Producto(), ["00001"]));
            await Reject(() => CodigosLecturas.Cantidad(db, nuevo.Id, ["00001"]));
            await Reject(() => CodigosLecturas.Cantidad(db, nuevo.Id, ["desconocido"]));
            await db.Database.ExecuteSqlRawAsync("DELETE FROM CodigosProducto WHERE Codigo='00001'; INSERT INTO CodigosProducto(Codigo, ProductoId) VALUES ('00001', {0});", nuevo.Id);
            await Reject(() => CodigosLecturas.Cantidad(db, nuevo.Id, ["00001"]));
            await Reject(() => CodigosLecturas.ResolverProducto(db, "00001"));
            await db.Database.ExecuteSqlRawAsync("DELETE FROM CodigosProducto WHERE Codigo='00001'; INSERT INTO CodigosProducto(Codigo, ProductoId) VALUES ('00001', {0});", p.Id);
            var borrador = new Producto { Stock = 999 };
            await CodigosLecturas.PrepararAlta(db, borrador, []);
            Assert(borrador.Stock == 0, "Stock manipulado aceptado");
            Assert((await db.Productos.AsNoTracking().SingleAsync(x => x.Id == p.Id)).Stock == 7, "Leer/cancelar alteró inventario");
            db.Users.Add(new IdentityUser { Id = "scanner", UserName = "scanner" });
            db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "scanner", Estado = true });
            await db.SaveChangesAsync();
            var venta = new VentaRequest { OperacionId = Guid.NewGuid(), Total = 20, MetodoPago = "Efectivo",
                Detalles = [new() { ProductoId = nuevo.Id, Cantidad = 999, Lecturas = ["002", "003"] }] };
            await new OperacionVentaService(db).Registrar(venta, "scanner");
            venta.Detalles[0].Lecturas = null; // Reintento de contrato histórico: misma huella, ninguna nueva salida.
            await new OperacionVentaService(db).Registrar(venta, "scanner");
            Assert(nuevo.Stock == 1 && await db.Ventas.CountAsync() == 1 && await db.MovimientosInventario.CountAsync() == 1,
                "Venta por lecturas no fue autoritativa o descontó dos veces");
            Assert(!await db.CodigosProducto.AnyAsync(c => c.ProductoId == nuevo.Id && (c.Codigo == "002" || c.Codigo == "003")) &&
                await db.CodigosProducto.AnyAsync(c => c.ProductoId == nuevo.Id && c.Codigo == "004") &&
                await db.DetalleVentas.AnyAsync(d => d.ProductoId == nuevo.Id && d.LecturasJson.Contains("002") && d.LecturasJson.Contains("003")),
                "La venta no retiró o no registró los códigos vendidos");
            venta.OperacionId = Guid.NewGuid();
            await Reject(() => new OperacionVentaService(db).Registrar(venta, "scanner"));
            var ventaId = await db.Ventas.Select(v => v.Id).SingleAsync();
            await new OperacionVentaService(db).Anular(ventaId, "scanner", "Prueba devolución códigos");
            Assert((await db.Productos.AsNoTracking().SingleAsync(p => p.Id == nuevo.Id)).Stock == 3 &&
                await db.CodigosProducto.CountAsync(c => c.ProductoId == nuevo.Id && new[] { "002", "003", "004" }.Contains(c.Codigo)) == 3,
                "La anulación no restauró stock y códigos vendidos");
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Productos_CodigoBarras ON Productos; UPDATE Productos SET CodigoBarras='00001';");
            await Reject(() => ActualizacionCodigos.Aplicar(db));
            Assert(await db.Productos.CountAsync() == 2 && await db.CodigosProducto.CountAsync() == 4, "Migración conflictiva perdió datos");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Productos ALTER COLUMN CodigoBarras nvarchar(max) NULL;");
            var largo = new string('X', 101);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Productos SET CodigoBarras={largo} WHERE Id={p.Id}");
            await Reject(() => ActualizacionCodigos.Aplicar(db));
            Assert((await db.Productos.AsNoTracking().SingleAsync(x => x.Id == p.Id)).CodigoBarras.Length == 101, "Código histórico truncado");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Productos SET CodigoBarras='ABC' WHERE Id={nuevo.Id}");
            foreach (var invalido in new[] { "\tABC", "ABC\t", "\u00a0ABC", "\ufeffABC", "\u200bABC", "ＡＢＣ", "ÁBC", " ABC ", "abc" })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Productos SET CodigoBarras={invalido} WHERE Id={p.Id}");
                // La ruta completa no debe limpiar el legado antes de detectar el problema.
                await Reject(() => ActualizacionComercial.Aplicar(db));
                Assert((await db.Productos.AsNoTracking().SingleAsync(x => x.Id == p.Id)).CodigoBarras == invalido, "El upgrade normalizó silenciosamente un código histórico");
                Assert(await db.CodigosProducto.CountAsync() == 4, "El upgrade modificó asociaciones ante un código inválido");
            }
            foreach (var invalido in new[] { "\tABC", "ABC\t", "\u00a0ABC", "\ufeffABC", "\u200bABC", "ＡＢＣ", "ÁBC" })
                await Reject(() => Task.FromResult(CodigosLecturas.Normalizar([invalido])));
            Assert(CodigosLecturas.Normalizar([" ABC "])[0] == "ABC", "Canonicalización de espacios ASCII inconsistente");
            await Reject(() => Task.FromResult(CodigosLecturas.Normalizar(Enumerable.Repeat("ABC", CodigosLecturas.MaxLecturas + 1))));
            Console.WriteLine("PASS: migración de códigos repetible, ceros iniciales, lecturas únicas, alternativos, conflictos, stock manipulado y borrador sin movimientos");
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
