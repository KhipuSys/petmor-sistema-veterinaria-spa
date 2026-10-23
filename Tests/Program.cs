using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Petmor.Web.Controllers;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Services;
using Petmor.Web.Infrastructure;
using System.Net;
using System.Security.Claims;
using VentaRequest = Petmor.Web.Controllers.VentaRequest;

if (args.Contains("--codigos")) { await CodigosTests.Run(); return; }
if (args.Contains("--revision-lecturas")) { await RevisionLecturasTests.Run(); return; }
if (args.Length == 0) await CodigosTests.Run();
if (args.Length == 0) await RevisionLecturasTests.Run();
if (args.Contains("--sedes")) { await SucursalesTests.Run(); return; }
if (args.Contains("--reservas")) { await ReservasTests.Run(); return; }
if (args.Contains("--veterinaria")) { await VeterinariaTests.Run(); return; }
if (args.Contains("--web")) { await PetmorWebTests.Run(); return; }
if (args.Contains("--auditoria")) { await AuditoriaTests.Run(); return; }

var database = "Petmor_Test_" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
ApplicationDbContext Db() => new(options);
int productoId = 0, cajaId = 0, codigoSecuencia = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
string SiguienteCodigo() => $"TEST-VENTA-{codigoSecuencia++:000}";
VentaRequest Request(int quantity = 1, decimal total = 10) => new() {
    OperacionId = Guid.NewGuid(), MetodoPago = "Efectivo", Total = total,
    Detalles = [new() { ProductoId = productoId, Cantidad = quantity, Precio = 10, Total = total,
        Lecturas = quantity > 0 ? Enumerable.Range(0, quantity).Select(_ => SiguienteCodigo()).ToList() : null }]
};
async Task<(Venta Venta, bool Repetida)> Sell(VentaRequest r) {
    await using var db = Db(); return await new OperacionVentaService(db).Registrar(r, "tester");
}
async Task Reject(VentaRequest r) {
    try { await Sell(r); } catch (InvalidOperationException) { return; }
    throw new Exception("Se aceptó una solicitud inválida.");
}
async Task Stock(int value) { await using var db = Db(); await db.ExistenciasSucursales.Where(p => p.ProductoId == productoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Cantidad, value)); }
ControllerContext UserContext() => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
    new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, "Administrador")], "test")) } };

try {
    await using (var db = Db()) {
        await ActualizacionComercial.Aplicar(db);
        db.Users.Add(new IdentityUser { Id="tester-id", UserName="tester", NormalizedUserName="TESTER" });
        var p = new Producto { Nombre="Prueba", Precio=10, Costo=4, Estado=true, Categoria=new Categoria { Nombre="Test", Estado=true } };
        await CodigosLecturas.PrepararAlta(db, p, Enumerable.Range(0, CodigosLecturas.MaxCodigos).Select(i => $"TEST-VENTA-{i:000}"));
        db.Productos.Add(p);
        var caja = new AperturaCaja { UsuarioId="tester", Estado=true, FechaApertura=HoraPeru.Ahora.AddHours(-1) };
        db.AperturasCaja.Add(caja);
        await db.SaveChangesAsync(); productoId=p.Id; cajaId=caja.Id;
        // Simula columnas ausentes de una instalación anterior sin perder sus filas.
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX IX_Ventas_OperacionId ON Ventas;
            ALTER TABLE Ventas DROP COLUMN OperacionId, HuellaSolicitud, AperturaCajaId, EstadoEmision, FechaAnulacion, AnuladaPor, MotivoAnulacion, AjustePosteriorCierre;
            ALTER TABLE DetalleVentas DROP COLUMN CostoUnitarioHistorico;
            ALTER TABLE AperturasCaja DROP COLUMN VentasEfectivo, AjustesAnulaciones;
            DROP TABLE UsuarioPerfiles;
            """);
        await ActualizacionComercial.Aplicar(db);
        await ActualizacionComercial.Aplicar(db);
        Assert(await db.Productos.CountAsync() == 1, "Upgrade perdió datos");
    }
    Console.WriteLine("PASS: esquema antiguo actualizado de forma repetible sin perder datos");

    await Reject(Request(-1, -10)); await Reject(Request(0,0)); await Reject(Request(1,1));
    var discount = Request(); discount.Descuento=-1; await Reject(discount);
    discount = Request(); discount.Descuento=11; await Reject(discount);
    discount = Request(total: 6); discount.Descuento=4; await Reject(discount);
    var adminDiscount = Request(total: 6); adminDiscount.Descuento=4; await using (var db = Db()) await new OperacionVentaService(db).Registrar(adminDiscount, "tester", true);
    discount = Request(total: 0); discount.Descuento=10; await Reject(discount);
    Console.WriteLine("PASS: rechazo de cantidades, descuentos e importes inválidos");

    var forged = Request(); forged.Detalles[0].Precio=0.01m; forged.Detalles[0].Total=0.01m;
    var saved = await Sell(forged);
    await using (var db = Db()) {
        var d = await db.DetalleVentas.SingleAsync(d => d.VentaId == saved.Venta.Id);
        Assert(d.PrecioUnitario == 10 && d.SubTotal == 10 && d.CostoUnitarioHistorico == 4, "Importes no autoritativos");
        await db.Productos.Where(p => p.Id == productoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Costo, 8));
        var report = new ReportesController(db);
        var view = (ViewResult)await report.Ventas(null,null);
        var model = (IEnumerable<Petmor.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(model.Single(v => v.VentaId == saved.Venta.Id).CostoTotal == 4, "Costo histórico cambió");
    }
    Console.WriteLine("PASS: precio del servidor y costo histórico estable al cambiar producto");

    await Stock(10);
    var duplicate = Request();
    var two = await Task.WhenAll(Sell(duplicate), Sell(duplicate));
    Assert(two[0].Venta.Id == two[1].Venta.Id && two.Count(v=>v.Repetida)==1, "Duplicación de venta");
    await using (var db = Db()) Assert((await db.Productos.FindAsync(productoId)).Stock == 9, "Descuento doble");
    duplicate.Total=9; await Reject(duplicate);
    Console.WriteLine("PASS: solicitudes simultáneas con mismo ID registran una sola venta; se rechaza reutilización distinta");

    await Stock(1);
    async Task<bool> TrySell() { try { await Sell(Request()); return true; } catch (InvalidOperationException) { return false; } }
    var lastUnit = await Task.WhenAll(TrySell(), TrySell());
    Assert(lastUnit.Count(x=>x)==1, "Dos ventas de última unidad");
    await using (var db = Db()) Assert((await db.Productos.FindAsync(productoId)).Stock == 0, "Stock final incorrecto");
    Console.WriteLine("PASS: dos operaciones concurrentes compiten por última unidad; solo una se confirma");

    await Stock(5);
    int count;
    await using (var db = Db()) {
        count=await db.Ventas.CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER Test_FailMovement ON MovimientosInventario AFTER INSERT AS THROW 51010, 'Fallo simulado', 1;");
    }
    bool rolledBack=false;
    try { await Sell(Request()); } catch { rolledBack=true; }
    await using (var db = Db()) {
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER Test_FailMovement");
        Assert(rolledBack && await db.Ventas.CountAsync()==count && (await db.Productos.FindAsync(productoId)).Stock==5, "Rollback incompleto");
    }
    Console.WriteLine("PASS: fallo después de guardar cabecera revierte venta, detalles y stock");

    var anulable = await Sell(Request());
    async Task VoidSale() { await using var db=Db(); await new OperacionVentaService(db).Anular(anulable.Venta.Id,"tester","Prueba concurrente"); }
    await Task.WhenAll(VoidSale(),VoidSale());
    await using (var db=Db()) {
        Assert((await db.Productos.FindAsync(productoId)).Stock==5,"Doble reposición");
        Assert(await db.MovimientosInventario.CountAsync(m=>m.Referencia==$"Anulación Venta #{anulable.Venta.Id}")==1,"Doble movimiento");
    }
    Console.WriteLine("PASS: anulación concurrente restaura stock una vez");

    var closeSale = await Sell(Request());
    var cardRequest = Request(total: 10.50m); cardRequest.MetodoPago="Tarjeta"; cardRequest.Recargo=0.50m; await Sell(cardRequest);
    decimal closingTotal;
    await using(var db=Db()) {
        var controller=new CajaController(db) { ControllerContext=UserContext() };
        var view=(ViewResult)await controller.Index();
        var box=(AperturaCaja)view.Model;
        Assert(box.TotalVentas==await db.Ventas.Where(v=>v.AperturaCajaId==cajaId && v.Estado=="Activa").SumAsync(v=>v.Total), "Caja suma anuladas");
        Assert(box.VentasEfectivo==box.TotalVentas-10.50m,"Caja incluye tarjeta como efectivo");
        await controller.Cerrar(20); closingTotal=box.TotalVentas;
    }
    await using(var db=Db()) await new OperacionVentaService(db).Anular(closeSale.Venta.Id,"tester","Después del cierre");
    await using(var db=Db()) {
        var box=await db.AperturasCaja.FindAsync(cajaId);
        Assert(box.TotalVentas==closingTotal && box.MontoCierre==20 && box.AjustesAnulaciones==10,"Se alteró cierre o falta ajuste");
    }
    var replay=await Sell(forged);
    Assert(replay.Repetida && replay.Venta.Id==saved.Venta.Id,"Reintento tras cierre no recuperó venta");
    await Reject(Request());
    Console.WriteLine("PASS: caja excluye anuladas, distingue efectivo y conserva cierres con ajuste posterior; reintentos recuperables tras cierre");

    await using(var db=Db()) {
        var box = await db.AperturasCaja.FindAsync(cajaId); box.Estado=true; box.FechaCierre=null; await db.SaveChangesAsync();
    }
    var handler=new RejectingHandler();
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> { ["FactilizaSettings:Token"]="test" }).Build();
    await using(var db=Db()) {
        var external=new FactilizaService(new HttpClient(handler),configuration);
        var controller=new VentasController(db,external,NullLogger<VentasController>.Instance) { ControllerContext=UserContext() };
        var request=Request();
        await controller.Registrar(request); await controller.Registrar(request);
        var v=await db.Ventas.AsNoTracking().SingleAsync(v=>v.OperacionId==request.OperacionId);
        Assert(v.EstadoEmision=="Requiere revisión" && handler.Calls==1,"Fallo externo no registrado o emisión duplicada");
    }
    Console.WriteLine("PASS: fallo de API conserva venta, queda visible y reintento no emite otra vez");
    var dniService = new FactilizaService(new HttpClient(new DniHandler()), configuration);
    var dniResult = await dniService.ConsultarDocumentoAsync("82000001");
    Assert(dniResult.Exito && dniResult.Nombres == "ANA MARIA" && dniResult.Apellidos == "PRUEBA DEMO" && dniResult.Nombre == "ANA MARIA PRUEBA DEMO", "Datos estructurados de DNI incorrectos");
    Console.WriteLine("PASS: proveedor DNI simulado devuelve nombres y apellidos separados");
    Assert(HoraPeru.DesdeInstante(new DateTimeOffset(2026,9,18,1,29,0,TimeSpan.Zero)) == new DateTime(2026,9,17,20,29,0), "Cambio de día UTC a Perú incorrecto");
    Assert(HoraPeru.DesdeInstante(new DateTimeOffset(2026,9,18,5,0,0,TimeSpan.Zero)) == new DateTime(2026,9,18,0,0,0), "Medianoche peruana incorrecta");
    await using(var db=Db()) {
        // Fechas de servidor europeo (UTC+2 en septiembre), como las del hosting antes de corregirlo.
        var legacy = new Venta { UsuarioId="tester-id", Fecha=new DateTime(2026,9,18,1,29,0), Total=10, FechaAnulacion=new DateTime(2026,9,18,2,0,0) };
        var historicalBox=new AperturaCaja { UsuarioId="tester", FechaApertura=new DateTime(2026,9,18,1,0,0), FechaCierre=null, Estado=false };
        db.Ventas.Add(legacy); db.AperturasCaja.Add(historicalBox); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AjustesHorario WHERE Nombre='PeruV1'; DELETE FROM RespaldoFechasPeru;");
        await ActualizacionComercial.Aplicar(db,"W. Europe Standard Time");
        await db.Entry(legacy).ReloadAsync(); await db.Entry(historicalBox).ReloadAsync();
        Assert(legacy.Fecha==new DateTime(2026,9,17,18,29,0) && legacy.FechaAnulacion==new DateTime(2026,9,17,19,0,0), "No se convirtió historial a Perú");
        Assert(historicalBox.FechaApertura==new DateTime(2026,9,17,18,0,0) && historicalBox.FechaCierre==null, "Conversión de caja incorrecta");
        Assert(await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM RespaldoFechasPeru WHERE Tabla='Ventas' AND RegistroId={legacy.Id} AND Campo='Fecha'").SingleAsync()==1,"Falta respaldo de fecha original");
        await ActualizacionComercial.Aplicar(db,"W. Europe Standard Time");
        await db.Entry(legacy).ReloadAsync(); Assert(legacy.Fecha==new DateTime(2026,9,17,18,29,0),"La fecha se convirtió dos veces");
        var report=new ReportesController(db);
        var view=(ViewResult)await report.Ventas(new DateTime(2026,9,17),new DateTime(2026,9,17));
        var list=(IEnumerable<Petmor.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(list.Any(v=>v.VentaId==legacy.Id),"El filtro diario de Perú excluyó una venta convertida");
        view=(ViewResult)await report.Ventas(new DateTime(2026,9,18),new DateTime(2026,9,18));
        list=(IEnumerable<Petmor.Web.Models.ViewModels.ReporteVentasViewModel>)view.Model;
        Assert(!list.Any(v=>v.VentaId==legacy.Id),"El filtro del día siguiente incluyó la venta anterior");
    }
    Console.WriteLine("PASS: hora Perú, medianoche, conversión histórica con respaldo, idempotencia y filtros diarios");
    await VeterinariaTests.Run();
    await PetmorWebTests.Run();
    Console.WriteLine("TODAS LAS PRUEBAS PASARON");
} finally {
    await using var db=Db(); await db.Database.EnsureDeletedAsync();
}

class RejectingHandler : HttpMessageHandler {
 public int Calls;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
    Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
 }
}
class DniHandler : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
    Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"nombres":"ANA MARIA","apellido_paterno":"PRUEBA","apellido_materno":"DEMO","nombre_completo":"ANA MARIA PRUEBA DEMO"}}""") });
}
