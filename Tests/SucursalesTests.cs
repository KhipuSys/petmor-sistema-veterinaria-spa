using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Petmor.Web.Controllers;
using Petmor.Web.Data;
using Petmor.Web.Infrastructure;
using Petmor.Web.Models;
using Petmor.Web.Models.ViewModels;
using Petmor.Web.Services;
using System.Security.Claims;

static class SucursalesTests
{
    public static async Task Run()
    {
        var database = "Petmor_Sedes_Test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
        ApplicationDbContext Db(int id = 1) => new(options, new SedeActual { Id = id });
        void Check(bool ok, string error) { if (!ok) throw new Exception(error); }
        async Task Rechaza(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Se aceptó operación de otra sede"); }
        int productoId, servicioId, mascotaId, atencionId, ventaId;
        try
        {
            await using (var db = Db())
            {
                await ActualizacionComercial.Aplicar(db);
                await ActualizacionVeterinaria.Aplicar(db);
                var producto = new Producto { Nombre = "Alimento", CodigoBarras = "SEDE-ALIMENTO", Precio = 10, Costo = 4, StockLegado = 12, Categoria = new Categoria { Nombre = "Prueba" } };
                var servicio = new Producto { Nombre = "Consulta", Precio = 30, EsServicio = true, Categoria = producto.Categoria };
                var mascota = new Mascota { Nombre = "Kira", Cliente = new Cliente { Nombre = "Propietario", Documento = "12345678", Telefono = "999999999" } };
                db.Productos.AddRange(producto, servicio); db.Mascotas.Add(mascota);
                db.Users.AddRange(new IdentityUser { Id = "admin", UserName = "admin" }, new IdentityUser { Id = "vet", UserName = "vet" });
                db.Roles.Add(new IdentityRole { Id = "r-vet", Name = "Veterinario" });
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = "vet", RoleId = "r-vet" });
                db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "admin", Estado = true });
                await db.SaveChangesAsync(); productoId = producto.Id; servicioId = servicio.Id; mascotaId = mascota.Id;
                // Simula tabla de existencias ausente de una instalación anterior.
                await db.Database.ExecuteSqlRawAsync("DROP TABLE ExistenciasSucursales;");
                foreach (var tabla in new[] { "Ventas", "Compras", "Gastos", "AperturasCaja", "MovimientosInventario", "Atenciones" })
                {
                    var quitarColumna = $"""
                        ALTER TABLE [{tabla}] DROP CONSTRAINT [FK_{tabla}_Sucursales_SucursalId];
                        DROP INDEX [IX_{tabla}_SucursalId] ON [{tabla}];
                        DECLARE @df nvarchar(128), @sql nvarchar(max);
                        SELECT @df=d.name FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
                            WHERE d.parent_object_id=OBJECT_ID('{tabla}') AND c.name='SucursalId';
                        IF @df IS NOT NULL BEGIN SET @sql=N'ALTER TABLE [{tabla}] DROP CONSTRAINT '+QUOTENAME(@df); EXEC(@sql); END;
                        ALTER TABLE [{tabla}] DROP COLUMN SucursalId;
                        """;
                    await db.Database.ExecuteSqlRawAsync(quitarColumna);
                }
                await ActualizacionSucursales.Aplicar(db);
                await ActualizacionSucursales.Aplicar(db);
            }
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 12, "Migración perdió stock San Miguel");
                Check(await db.UsuariosSedes.CountAsync(x => x.SucursalId == 1) == 2, "Asignación de usuarios antiguos");
            }
            await using (var db = Db(2))
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 0, "Centro heredó stock incorrecto");
                Check(!await db.AperturasCaja.AnyAsync(), "Caja visible en otra sede");
                var producto = await db.Productos.FindAsync(productoId); producto.Stock = 7;
                db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "admin", Estado = true });
                await db.SaveChangesAsync();
            }
            Console.WriteLine("PASS: migración repetible, usuarios antiguos en San Miguel, stock y cajas independientes");
            VentaRequest Request() => new() { OperacionId = Guid.NewGuid(), MetodoPago = "Efectivo", Total = 10, Detalles = [new() { ProductoId = productoId, Cantidad = 1, Lecturas = ["SEDE-ALIMENTO"] }] };
            var solicitud = Request();
            await using (var db = Db()) ventaId = (await new OperacionVentaService(db).Registrar(solicitud, "admin", true)).Venta.Id;
            await using (var db = Db(2))
            {
                Check(await db.Ventas.FindAsync(ventaId) == null, "Acceso por ID a venta de otra sede");
                Check(!await db.DetalleVentas.AnyAsync() && !await db.MovimientosInventario.AnyAsync(), "Detalle/Kardex filtrado incorrecto");
                await Rechaza(() => new OperacionVentaService(db).Anular(ventaId, "admin", "Prueba"));
                Check((await db.Productos.FindAsync(productoId)).Stock == 7, "Venta San Miguel cambió stock Centro");
                await new OperacionVentaService(db).Registrar(Request(), "admin", true);
            }
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 11, "Venta Centro cambió San Miguel");
                await new OperacionVentaService(db).Anular(ventaId, "admin", "Prueba");
            }
            await using (var db = Db()) Check((await db.Productos.FindAsync(productoId)).Stock == 12, "Anulación no repuso San Miguel");
            await using (var db = Db(2)) Check((await db.Productos.FindAsync(productoId)).Stock == 6, "Anulación cruzó stock");
            Console.WriteLine("PASS: venta, anulación, detalles, acceso por ID y Kardex aislados por sede");
            await using (var db = Db())
            {
                atencionId = await new AtencionService(db).Agendar(new AgendaForm { MascotaId = mascotaId, ServicioId = servicioId, ResponsableId = "vet", Duracion = 30 }, "admin");
            }
            await using (var db = Db(2))
            {
                Check(await db.Atenciones.FindAsync(atencionId) == null && !await db.CargosAtencion.AnyAsync(), "Agenda/cargos cruzados");
                await Rechaza(() => new AtencionService(db).Agendar(new AgendaForm { MascotaId = mascotaId, ServicioId = servicioId, ResponsableId = "vet", Duracion = 30 }, "admin"));
                Check(await db.Mascotas.AnyAsync(x => x.Id == mascotaId), "Mascotas no compartidas");
            }
            await using (var db = Db(2))
            {
                var ajena = await db.Ventas.IgnoreQueryFilters().SingleAsync(v => v.Id == ventaId);
                ajena.Estado = "Activa";
                await Rechaza(async () => await db.SaveChangesAsync());
            }
            foreach (int sede in new[] { 1, 2 })
            {
                await using var db = Db(sede);
                // SQL de proyecciones usadas en POS, reportes y alertas.
                await db.Productos.Select(p => new { Stock = p.Existencias.Sum(x => x.Cantidad), p.Nombre }).ToListAsync();
                await db.Productos.SumAsync(p => p.Precio * p.Existencias.Sum(x => x.Cantidad));
            }
            var actual = new SedeActual { Id = 1 };
            var services = new ServiceCollection().AddSingleton(actual).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services };
            var provider = new SedeAntiforgeryData(); var tokenData = provider.GetAdditionalData(http);
            actual.Id = 2;
            Check(!provider.ValidateAdditionalData(http, tokenData), "Formulario obsoleto aceptado al cambiar sede");
            await using (var db = Db())
            {
                var context = new DefaultHttpContext();
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "vet"), new Claim(ClaimTypes.Role, "Veterinario")], "test"));
                context.Request.Headers.Cookie = "petmor-sede=2";
                var state = new SedeActual();
                await new SedeMiddleware(_ => Task.CompletedTask).InvokeAsync(context, db, state);
                Check(state.Id == 1 && !state.PuedeCambiar, "Empleado pudo elegir sede manipulando cookie");
            }
            Console.WriteLine("PASS: agenda por sede, responsables autorizados, guardia de escritura, consultas SQL, formularios obsoletos y cookie manipulada");
            async Task<bool> ResponsableVisible(int sede)
            {
                await using var db = Db(sede);
                var controller = new PetmorController(db, new AtencionService(db), null);
                await controller.Agendar((int?)mascotaId);
                var lista = (System.Collections.IEnumerable)controller.ViewBag.Responsables;
                return lista.Cast<object>().Any(x => (string)x.GetType().GetProperty("Id").GetValue(x) == "vet");
            }
            Check(await ResponsableVisible(1) && !await ResponsableVisible(2), "Selector permite profesional de otra sede");
            await using (var db = Db())
            {
                (await db.UsuariosSedes.SingleAsync(x => x.UsuarioId == "vet")).SucursalId = 0;
                (await db.Atenciones.SingleAsync(x => x.Id == atencionId)).Estado = EstadoAtencion.Cancelada;
                await db.SaveChangesAsync();
            }
            foreach (var sede in new[] { 1, 2 })
            {
                Check(await ResponsableVisible(sede), "Profesional de Ambas sedes ausente del selector en sede " + sede);
                await using var db = Db(sede);
                var id = await new AtencionService(db).Agendar(new AgendaForm { MascotaId = mascotaId, ServicioId = servicioId, ResponsableId = "vet", Duracion = 30 }, "admin");
                var atencion = await db.Atenciones.SingleAsync(x => x.Id == id);
                Check(db.Entry(atencion).Property<int>("SucursalId").CurrentValue == sede, "Atención asignada a sede incorrecta");
                atencion.Estado = EstadoAtencion.Cancelada;
                await db.SaveChangesAsync();
            }
            await using (var db = Db())
            {
                (await db.Sucursales.SingleAsync(x => x.Id == 0)).Activa = false;
                await db.SaveChangesAsync();
            }
            Check(!await ResponsableVisible(1), "Selector permite una asignación de sede inactiva");
            await using (var db = Db())
                await Rechaza(() => new AtencionService(db).Agendar(new AgendaForm { MascotaId = mascotaId, ServicioId = servicioId, ResponsableId = "vet", Duracion = 30 }, "admin"));
            Console.WriteLine("PASS: profesional de Ambas sedes visible y asignable en ambas; otras sedes e inactivas excluidas.");
        }
        finally
        {
            await using var db = Db();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
