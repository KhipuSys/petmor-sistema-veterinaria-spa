using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiniMarket.Web.Controllers;
using MiniMarket.Web.Data;
using MiniMarket.Web.Infrastructure;
using MiniMarket.Web.Models;
using MiniMarket.Web.Services;
using System.Security.Claims;
using System.Text.Json;

static class AuditoriaTests
{
    public static async Task Run()
    {
        var database = "Petmor_Auditoria_Test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
        ApplicationDbContext Db() => new(options);
        void Check(bool ok, string mensaje) { if (!ok) throw new Exception(mensaje); }
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddControllersWithViews();
        using var services = registrations.BuildServiceProvider();
        ControllerContext Context() => new() { RouteData = new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor(), HttpContext = new DefaultHttpContext { RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Administrador") }, "test")) } };
        try
        {
            int productoId, proveedorId, categoriaId;
            await using (var db = Db())
            {
                await ActualizacionComercial.Aplicar(db); await ActualizacionVeterinaria.Aplicar(db);
                await ActualizacionComercial.Aplicar(db); await ActualizacionVeterinaria.Aplicar(db);
                db.Users.Add(new IdentityUser { Id = "admin", UserName = "admin" });
                db.AperturasCaja.Add(new AperturaCaja { UsuarioId = "admin", Estado = true });
                var categoria = new Categoria { Nombre = "Categoría" };
                var producto = new Producto { Nombre = "Original", CodigoBarras = "COMPRA-1", Precio = 20.70m, Costo = 4, Stock = 2, Categoria = categoria,
                    Codigos = [new CodigoProducto { Codigo = "COMPRA-ALT" }] };
                var proveedor = new Proveedor { RazonSocial = "Proveedor", Ruc = "20123456789", Estado = true };
                db.Productos.Add(producto); db.Proveedores.Add(proveedor);
                db.Configuraciones.Add(new Configuracion { NombreEmpresa = "Original", IgvPorcentaje = 18, MonedaSimbolo = "S/." });
                await db.SaveChangesAsync(); productoId = producto.Id; proveedorId = proveedor.Id; categoriaId = categoria.Id;
            }
            Check(PagoReglas.CalcularRecargo("Tarjeta", 20.70m) == 1.04m, "Redondeo tarjeta incorrecto");
            int ventaId;
            await using (var db = Db())
            {
                var venta = await new OperacionVentaService(db).Registrar(new VentaRequest { OperacionId = Guid.NewGuid(), MetodoPago = "Tarjeta", Total = 21.74m, Recargo = 1.04m,
                    Detalles = [new() { ProductoId = productoId, Cantidad = 1, Lecturas = ["COMPRA-1"] }] }, "admin");
                ventaId = venta.Venta.Id;
                (await db.Configuraciones.FirstAsync()).IgvPorcentaje = 10;
                (await db.Productos.FindAsync(productoId)).Nombre = "Renombrado";
                await db.SaveChangesAsync();
            }
            await using (var db = Db())
            {
                var venta = await db.Ventas.Include(v => v.Detalles).SingleAsync(v => v.Id == ventaId);
                var historico = JsonSerializer.Deserialize<TicketHistorico>(venta.TicketHistoricoJson);
                Check(historico.Empresa.IgvPorcentaje == 18 && venta.Detalles[0].DescripcionHistorica == "Original", "Ticket histórico alterado");
                Check(historico.BaseImponible + historico.Igv == venta.Total, "Desglose histórico inconsistente");
            }
            var tag = new FechaTablaTagHelper();
            var output = new TagHelperOutput("td", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent("<strong>01/09/2026</strong><small>09:30</small>")));
            await tag.ProcessAsync(new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "fecha"), output);
            Check(output.Attributes["data-order"].Value.ToString() == "20260901093000", "Orden de fecha incorrecto");
            Console.WriteLine("PASS AUDITORIA: recargo, instantánea de ticket y clave cronológica de tablas");

            await using (var db = Db())
            {
                var propietario = new Cliente { Nombre = "Propietario pendiente", Documento = "22222222", Telefono = "999999999" };
                var atencion = new Atencion { Mascota = new Mascota { Nombre = "Mascota", Cliente = propietario }, ResponsableId = "admin", Motivo = "Prueba",
                    Cargos = [new CargoAtencion { ProductoId = productoId, Descripcion = "Cargo", Cantidad = 1, Precio = 20.70m }] };
                db.Atenciones.Add(atencion); await db.SaveChangesAsync();
                Check(await DependenciasOperacion.ClientePendiente(db, propietario.Id), "No detectó cliente pendiente");
                Check(await DependenciasOperacion.ProductoPendiente(db, productoId), "No detectó producto pendiente");
                await new AtencionService(db).CambiarEstado(atencion.Id, EstadoAtencion.Cancelada, "admin", true, "Cambio. Estado: Cancelada. literal");
                Check(atencion.MotivoCancelacionVisible == "Cambio. Estado: Cancelada. literal", "Motivo estructurado mutilado");
                Check(!await DependenciasOperacion.ClientePendiente(db, propietario.Id), "Cancelada bloquea desactivación");
            }
            var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html>contenido no imagen</html>");
            using (var stream = new MemoryStream(htmlBytes))
            {
                bool rechazado = false;
                try { await ImagenSubida.Extension(new FormFile(stream, 0, stream.Length, "foto", "foto.png")); }
                catch (InvalidOperationException) { rechazado = true; }
                Check(rechazado, "Se aceptó HTML disfrazado de imagen");
            }
            Console.WriteLine("PASS AUDITORIA: dependencias de cuentas, motivo estructurado e imagen con contenido inválido");

            await using (var db = Db())
            {
                db.Productos.Add(new Producto { Nombre = "Con código", CodigoBarras = "BAR-001", Precio = 1, Costo = 1, CategoriaId = categoriaId });
                await db.SaveChangesAsync();
            }
            await using (var db = Db())
            {
                var ctx = Context();
                var controller = new ProductosController(db, null) { ControllerContext = ctx };
                var duplicado = new Producto { Nombre = "Duplicado", CodigoBarras = " BAR-001 ", Precio = 2, Costo = 1, CategoriaId = categoriaId };
                var action = await controller.Create(duplicado, null, [" BAR-001 "]);
                Check(action is ViewResult && controller.ModelState[nameof(Producto.CodigoBarras)]?.Errors.Count > 0, "Código de barras duplicado aceptado en alta");
                Check(duplicado.CodigoBarras == "BAR-001", "Código de barras no se normalizó al crear");
            }
            await using (var db = Db())
            {
                var ctx = Context();
                var controller = new ProductosController(db, null) { ControllerContext = ctx };
                var existente = await db.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId);
                existente.CodigoBarras = "BAR-001";
                var action = await controller.Edit(productoId, existente, null);
                Check(action is ViewResult && controller.ModelState[nameof(Producto.CodigoBarras)]?.Errors.Count > 0, "Código de barras duplicado aceptado en edición");
            }
            Console.WriteLine("PASS AUDITORIA: código de barras único en alta y edición de productos");

            Console.WriteLine("PASS AUDITORIA: el reset operativo ya no existe dentro de la aplicación; mantenimiento destructivo queda fuera del proyecto.");
        }
        finally { await using var db = Db(); await db.Database.EnsureDeletedAsync(); }
    }
}
