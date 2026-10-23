using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Controllers;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Models.ViewModels;
using Petmor.Web.Services;
using Petmor.Web.Infrastructure;

static class VeterinariaTests
{
    public static async Task Run()
    {
        var nombre = "Petmor_Test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={nombre};Trusted_Connection=True;TrustServerCertificate=True").Options;
        ApplicationDbContext Db() => new(options);
        void Check(bool ok, string mensaje) { if (!ok) throw new Exception(mensaje); }
        async Task Rechaza(Func<Task> f) { try { await f(); } catch (InvalidOperationException) { return; } throw new Exception("Se aceptó una operación inválida."); }
        async Task Operar(Func<AtencionService, Task> f) { await using var db = Db(); await f(new AtencionService(db)); }
        int mascotaId, servicioId, productoId, clienteId, atencionId;
        try
        {
            await using (var db = Db())
            {
                await ActualizacionComercial.Aplicar(db);
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Productos ALTER COLUMN Stock int NOT NULL; ALTER TABLE MovimientosInventario ALTER COLUMN Cantidad int NOT NULL;");
                await ActualizacionVeterinaria.Aplicar(db);
                await ActualizacionVeterinaria.Aplicar(db);
                db.Users.Add(new IdentityUser { Id = "vet", UserName = "veterinario", NormalizedUserName = "VETERINARIO" });
                db.Roles.Add(new IdentityRole { Id = "rol-vet", Name = "Veterinario", NormalizedName = "VETERINARIO" });
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = "vet", RoleId = "rol-vet" });
                db.UsuariosSedes.Add(new UsuarioSede { UsuarioId = "vet", SucursalId = 1 });
                var mascota = new Mascota { Nombre = "Mascota prueba", Cliente = new Cliente { Nombre = "Propietario prueba", Documento = "12345678", Telefono = "999999999" } };
                var categoria = new Categoria { Nombre = "Prueba", Estado = true };
                var servicio = new Producto { Nombre = "Consulta", Precio = 50, EsServicio = true, AreaServicio = AreaAtencion.Veterinaria, Categoria = categoria };
                var producto = new Producto { Nombre = "Insumo", CodigoBarras = "INSUMO-1", Precio = 10, Costo = 4, Stock = 10, Categoria = categoria,
                    Codigos = [new CodigoProducto { Codigo = "INSUMO-ALT" }] };
                db.Mascotas.Add(mascota); db.Productos.AddRange(servicio, producto);
                await db.SaveChangesAsync(); mascotaId = mascota.Id; clienteId = mascota.ClienteId; servicioId = servicio.Id; productoId = producto.Id;
            }
            var form = new AgendaForm { MascotaId = mascotaId, ServicioId = servicioId, ResponsableId = "vet", Area = AreaAtencion.Veterinaria, Motivo = "Consulta", Inicio = HoraPeru.Ahora, Duracion = 30 };
            await using (var db = Db()) atencionId = await new AtencionService(db).Agendar(form, "recepcion");
            await Rechaza(() => Operar(async s => { await s.Agendar(form, "recepcion"); }));
            await Rechaza(() => Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.Lista, "vet", true, null)));
            await Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.EnEspera, "recepcion", false, null));
            // La recepción puede iniciar el flujo operativo; editar ficha sigue restringido por controlador.
            await Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.EnAtencion, "recepcion", false, null));
            await Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.EnAtencion, "veterinario", true, null));
            await Operar(s => s.Cargo(atencionId, servicioId, 1, "veterinario", quitar: true));
            await Rechaza(() => Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.Lista, "veterinario", true, null)));
            await Operar(s => s.GuardarFicha(atencionId, new FichaForm { Evaluacion = "Evaluación registrada", Diagnostico = "Control", Tratamiento = "Receta", Peso = 12.5m }, "veterinario"));
            var op = Guid.NewGuid();
            await Task.WhenAll(Operar(s => s.Consumir(atencionId, productoId, 999, op, "veterinario", ["INSUMO-1", "INSUMO-ALT"])), Operar(s => s.Consumir(atencionId, productoId, 999, op, "veterinario", ["INSUMO-1", "INSUMO-ALT"])));
            await Rechaza(() => Operar(s => s.Consumir(atencionId, productoId, 3, op, "veterinario")));
            await Operar(s => s.Consumir(atencionId, productoId, 2, op, "veterinario")); // Reintento histórico.
            await Rechaza(() => Operar(s => s.Consumir(atencionId, productoId, 1, Guid.NewGuid(), "veterinario")));
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 8, "Consumo duplicado");
                Check(await db.ConsumosAtencion.CountAsync() == 1, "Registro de consumo duplicado");
            }
            await Operar(s => s.Cargo(atencionId, servicioId, 1, "veterinario"));
            await Operar(s => s.Cargo(atencionId, productoId, 1, "veterinario", lecturas: ["INSUMO-1"]));
            await Rechaza(() => Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.Lista, "recepcion", false, null)));
            await Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.Lista, "veterinario", true, null));
            await Rechaza(() => Operar(s => s.GuardarFicha(atencionId, new FichaForm { Evaluacion = "alteración" }, "veterinario")));
            await Rechaza(() => Operar(s => s.Consumir(atencionId, productoId, 1, Guid.NewGuid(), "veterinario")));
            Guid cobro;
            await using (var db = Db()) cobro = (await db.Atenciones.FindAsync(atencionId)).OperacionCobro;
            VentaRequest request = new() { AtencionId = atencionId, OperacionId = cobro, ClienteId = clienteId, MetodoPago = "Efectivo", Total = 60,
                Detalles = [new() { ProductoId = servicioId, Cantidad = 1 }, new() { ProductoId = productoId, Cantidad = 1 }] };
            async Task<(Venta Venta, bool Repetida)> Cobrar() { await using var db = Db(); return await new OperacionVentaService(db).Registrar(request, "veterinario"); }
            await Rechaza(async () => { await Cobrar(); });
            await using (var db = Db()) { db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "veterinario", Estado = true }); await db.SaveChangesAsync(); }
            var resultados = await Task.WhenAll(Cobrar(), Cobrar());
            Check(resultados[0].Venta.Id == resultados[1].Venta.Id, "Cobro duplicado");
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 7, "Venta descontó otra vez el consumo");
                Check((await db.Productos.FindAsync(servicioId)).Stock == 0, "Servicio movió stock");
                Check((await db.Atenciones.FindAsync(atencionId)).VentaId == resultados[0].Venta.Id, "Cuenta no vinculada a venta");
                Check(!await db.MovimientosInventario.AnyAsync(x => x.ProductoId == servicioId), "Servicio produjo Kardex");
            }
            await Rechaza(() => Operar(s => s.Cargo(atencionId, servicioId, 1, "recepcion")));
            await Operar(s => s.CambiarEstado(atencionId, EstadoAtencion.Entregada, "recepcion", false, null));
            await using (var db = Db()) await new OperacionVentaService(db).Anular(resultados[0].Venta.Id, "admin", "Prueba anulación");
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(productoId)).Stock == 8, "Anulación restituyó consumos ya aplicados");
                Check((await db.Atenciones.FindAsync(atencionId)).VentaId == null, "Cuenta no reabierta tras anulación");
            }
            Console.WriteLine("PASS PETMOR: agenda sin cruces; transiciones y ficha; consumo concurrente idempotente; caja obligatoria; cobro único; servicio sin stock; anulación conserva consumos.");
            int insumoFraccionado;
            await using (var db = Db())
            {
                db.Roles.Add(new IdentityRole { Id = "rol-groomer", Name = "Groomer", NormalizedName = "GROOMER" });
                db.UserRoles.Add(new IdentityUserRole<string> { RoleId = "rol-groomer", UserId = "vet" });
                var p = new Producto { Nombre = "Insumo inventariable", CodigoBarras = "INSUMO-SPA", Unidad = "unidad", Precio = 10, Costo = 2, Stock = 3,
                    CategoriaId = (await db.Categorias.FirstAsync()).Id };
                db.Productos.Add(p); await db.SaveChangesAsync(); insumoFraccionado = p.Id;
                var spa = new Producto { Nombre = "Baño", EsServicio = true, AreaServicio = AreaAtencion.Spa, Precio = 50, CategoriaId = p.CategoriaId };
                db.Productos.Add(spa); await db.SaveChangesAsync(); form.ServicioId = spa.Id;
            }
            form.Area = AreaAtencion.Spa; form.Inicio = HoraPeru.Ahora.AddHours(3);
            int spaId;
            await using (var db = Db()) spaId = await new AtencionService(db).Agendar(form, "recepcion");
            await Operar(s => s.CambiarEstado(spaId, EstadoAtencion.EnEspera, "recepcion", false, null));
            await Operar(s => s.CambiarEstado(spaId, EstadoAtencion.EnAtencion, "veterinario", true, null));
            await Operar(s => s.Consumir(spaId, insumoFraccionado, 1, Guid.NewGuid(), "veterinario", ["INSUMO-SPA"]));
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(insumoFraccionado)).Stock == 2, "Stock global fraccionado incorrecto");
            }
            await Rechaza(() => Operar(s => s.Consumir(spaId, insumoFraccionado, 10001, Guid.NewGuid(), "veterinario")));
            await Operar(s => s.Cargo(spaId, form.ServicioId.Value, 1, "veterinario", quitar: true));
            await Rechaza(() => Operar(s => s.CambiarEstado(spaId, EstadoAtencion.Lista, "veterinario", true, null)));
            await Operar(s => s.GuardarFicha(spaId, new FichaForm { Bano = true, Indicaciones = "Servicio concluido" }, "veterinario"));
            await Operar(s => s.Cargo(spaId, servicioId, 1, "veterinario"));
            await Operar(s => s.CambiarEstado(spaId, EstadoAtencion.Lista, "veterinario", true, null));
            await using (var db = Db())
            {
                Check((await db.Productos.FindAsync(insumoFraccionado)).Stock == 2, "El rechazo dejó un descuento parcial");
            }
            Console.WriteLine("PASS PETMOR: spa exige cargos; ficha de tareas; consumos enteros; rechazo revierte stock global.");
        }
        finally { await using var db = Db(); await db.Database.EnsureDeletedAsync(); }
    }
}
