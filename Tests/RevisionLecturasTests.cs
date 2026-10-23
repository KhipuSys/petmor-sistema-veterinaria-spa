using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Petmor.Web.Controllers;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Services;

public static class RevisionLecturasTests
{
    private sealed class PausaBloqueo : DbCommandInterceptor
    {
        public readonly TaskCompletionSource Adquirido = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continuar = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("Petmor.OperacionesCaja") && Adquirido.TrySetResult())
                await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return result;
        }
    }
    private sealed class ContadorConsultas : DbCommandInterceptor
    {
        public int Lecturas;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Lecturas++; return ValueTask.FromResult(result); }
    }
    public static async Task Run()
    {
        var connection = $"Server=localhost\\SQLEXPRESS;Database=RevisionLecturas_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";
        ApplicationDbContext Db(IInterceptor interceptor = null) {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection);
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        void Check(bool ok, string error) { if (!ok) throw new Exception(error); }
        async Task Rechazar(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Se aceptó operación inválida"); }
        ControllerContext Context() => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Administrador")], "test")) } };
        int categoriaId, clienteId;
        await using var inicial = Db();
        try
        {
            await ActualizacionComercial.Aplicar(inicial);
            var categoria = new Categoria { Nombre = "Revisión", Estado = true };
            var cliente = new Cliente { Nombre = "Revisión", Documento = "12345678", Telefono = "999999999", Estado = true };
            inicial.Categorias.Add(categoria); inicial.Clientes.Add(cliente); await inicial.SaveChangesAsync();
            categoriaId = categoria.Id; clienteId = cliente.Id;
            foreach (var interno in new[] { false, true })
            foreach (var primeroReserva in new[] { true, false })
            {
                var codigo = Guid.NewGuid().ToString("N");
                var p = new Producto { Nombre = "Carrera", CodigoBarras = codigo, CategoriaId = categoriaId, Precio = 10, Stock = 5 };
                inicial.Productos.Add(p); await inicial.SaveChangesAsync();
                var pausa = new PausaBloqueo();
                async Task Reservar(bool pausar) { await using var db = Db(pausar ? pausa : null); await new ReservaService(db).Crear(new ReservaForm {
                    ClienteId = clienteId, ProductoId = p.Id, Cantidad = 1, Lecturas = [codigo] }, "admin"); }
                async Task<IActionResult> Editar(bool pausar) { await using var db = Db(pausar ? pausa : null); return await new ProductosController(db, null) { ControllerContext = Context() }
                    .Edit(p.Id, new Producto { Id = p.Id, Nombre = "Editado", CategoriaId = categoriaId, Precio = 10,
                        CodigoBarras = codigo, Estado = interno, UsoInterno = interno, Stock = 999 }, null); }
                Task primera = primeroReserva ? Reservar(true) : Editar(true);
                await pausa.Adquirido.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Task segunda = primeroReserva ? Editar(false) : Reservar(false);
                try { await Task.Delay(200); Check(!segunda.IsCompleted, "Edición/reserva avanzó sin respetar el bloqueo transaccional"); }
                finally { pausa.Continuar.TrySetResult(); }
                await primera;
                if (primeroReserva)
                {
                    var resultado = await (Task<IActionResult>)segunda;
                    Check(resultado is ViewResult, "La edición ignoró una reserva confirmada");
                    await using var ver = Db(); var actual = await ver.Productos.FindAsync(p.Id);
                    Check(actual.Estado && !actual.UsoInterno && actual.Stock == 4, "La edición alteró estado/stock de un producto reservado");
                    Check(!await ver.CodigosProducto.AnyAsync(c => c.Codigo == "NUEVO-" + codigo), "Se guardaron códigos de una edición rechazada");
                }
                else
                {
                    await Rechazar(() => segunda);
                    await using var ver = Db();
                    Check(!await ver.ReservasProductos.AnyAsync(r => r.ProductoId == p.Id), "Se reservó después de desactivar/marcar interno");
                    Check((await ver.Productos.FindAsync(p.Id)).Stock == 5, "La edición aceptó stock publicado");
                }
            }
            Console.WriteLine("PASS REVISIÓN: carreras reserva/edición en ambos órdenes, desactivación y uso interno; stock y asociaciones atómicos.");

            var contador = new ContadorConsultas();
            await using var medido = Db(contador);
            var lote = new Producto { Nombre = "Lote de prueba de consultas", CategoriaId = categoriaId };
            var codigos = Enumerable.Range(0, CodigosLecturas.MaxCodigos).Select(n => $"BATCH-{n:000}").ToArray();
            contador.Lecturas = 0;
            await CodigosLecturas.PrepararAlta(medido, lote, codigos);
            Check(contador.Lecturas <= 4 && lote.Codigos.Count == codigos.Length, "Asociar presenta consultas N+1");
            medido.Productos.Add(lote); await medido.SaveChangesAsync();
            contador.Lecturas = 0;
            Check(await CodigosLecturas.Cantidad(medido, lote.Id, codigos) == 200 && contador.Lecturas <= 5, "Conteo no utiliza consultas por lote");
            async Task RechazarSinConsultas(int cantidadDetalles, Action<List<DetalleRequest>> alterar = null)
            {
                var detalles = Enumerable.Range(0, cantidadDetalles).Select(_ => new DetalleRequest {
                    ProductoId = lote.Id, Cantidad = 1, Precio = 1, Total = 1, Lecturas = [codigos[0]] }).ToList();
                alterar?.Invoke(detalles);
                var venta = new VentaRequest { OperacionId = Guid.NewGuid(), MetodoPago = "Efectivo", Total = cantidadDetalles, Detalles = detalles };
                contador.Lecturas = 0;
                await Rechazar(() => new OperacionVentaService(medido).Registrar(venta, "admin"));
                Check(contador.Lecturas == 0, "Venta consultó SQL antes de validar toda la estructura/listas");
            }
            await RechazarSinConsultas(201);
            await RechazarSinConsultas(500);
            await RechazarSinConsultas(0);
            await RechazarSinConsultas(2, d => d[1] = null);
            await RechazarSinConsultas(2, d => d[1].ProductoId = 0);
            await RechazarSinConsultas(2, d => d[1].Lecturas = []);
            await RechazarSinConsultas(2, d => d[1].Lecturas = Enumerable.Repeat(codigos[0], CodigosLecturas.MaxLecturas + 1).ToList());
            await RechazarSinConsultas(2, d => d[1].Lecturas = ["\tINVALIDO"]);
            var limiteVenta = new VentaRequest { OperacionId = Guid.NewGuid(), MetodoPago = "Efectivo", Total = 200,
                Detalles = Enumerable.Range(0, 200).Select(i => new DetalleRequest { ProductoId = lote.Id, Cantidad = -999, Lecturas = [codigos[i]] }).ToList() };
            contador.Lecturas = 0;
            await Rechazar(() => new OperacionVentaService(medido).Registrar(limiteVenta, "admin")); // No hay caja en este fixture.
            Check(contador.Lecturas > 0 && limiteVenta.Detalles.All(d => d.Cantidad == 1), "El límite válido de 200 no resolvió las lecturas de venta");
            Console.WriteLine("PASS SEGURIDAD: ventas rechazan 201 y 500 detalles, estructura y última lista inválida con cero consultas SQL.");
            await Rechazar(() => CodigosLecturas.Asociar(medido, lote, ["EXCEDE-LIMITE"]));
            var controlador = new ProductosController(medido, null) { ControllerContext = Context() };
            controlador.ModelState.AddModelError("Nombre", "Error simulado de formulario");
            var intento = new List<string> { "BORRADOR-CONSERVADO" };
            var vista = await controlador.Edit(lote.Id, new Producto { Id = lote.Id, Nombre = "", CategoriaId = categoriaId, Estado = true }, null, lecturas: intento);
            Check(vista is ViewResult && ((IEnumerable<string>)controlador.ViewData["Lecturas"]).SequenceEqual(intento), "Edit perdió las lecturas en un error");
            Check(!await medido.CodigosProducto.AnyAsync(c => c.Codigo == intento[0]), "Un error de formulario persistió códigos");
            Console.WriteLine("PASS REVISIÓN: 200 códigos únicos en 4 consultas, 200 lecturas en 5 consultas; límite y borrador de edición preservados.");
        }
        finally { await inicial.Database.EnsureDeletedAsync(); }
    }
}
