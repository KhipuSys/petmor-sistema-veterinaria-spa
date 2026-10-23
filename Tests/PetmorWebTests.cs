using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Data;
using Petmor.Web.Models;
using Petmor.Web.Infrastructure;

static class PetmorWebTests
{
    public static async Task Run()
    {
        var database = "Petmor_WebTest_" + Guid.NewGuid().ToString("N");
        var conexion = $"Server=localhost\\SQLEXPRESS;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(conexion).Options;
        using var reserva = new TcpListener(IPAddress.Loopback, 0);
        reserva.Start(); int port = ((IPEndPoint)reserva.LocalEndpoint).Port; reserva.Stop();
        var configuracion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyConfigurationAttribute>(typeof(ApplicationDbContext).Assembly)?.Configuration ?? "Debug";
        var inicio = new ProcessStartInfo("dotnet", $"bin/{configuracion}/net9.0/Petmor.Web.dll") {
            WorkingDirectory = Directory.GetCurrentDirectory(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        inicio.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        inicio.Environment["ConnectionStrings__DefaultConnection"] = conexion;
        // Este ejercicio no invoca emisión externa; el cobro se prueba en la suite de servicios.
        using var proceso = Process.Start(inicio);
        var salida = proceso.StandardOutput.ReadToEndAsync(); var error = proceso.StandardError.ReadToEndAsync();
        using var cliente = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        async Task<string> Get(string ruta)
        {
            var r = await cliente.GetAsync(ruta); var html = await r.Content.ReadAsStringAsync();
            if (r.StatusCode != HttpStatusCode.OK) throw new Exception($"HTTP {r.StatusCode}: {ruta}\n{html[..Math.Min(html.Length,1500)]}");
            return html;
        }
        async Task<string> Post(string ruta, Dictionary<string, string> datos, string pagina = null)
        {
            var html = await Get(pagina ?? ruta);
            datos["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var r = await cliente.PostAsync(ruta, new FormUrlEncodedContent(datos));
            var respuesta = await r.Content.ReadAsStringAsync();
            if (r.StatusCode != HttpStatusCode.OK || r.RequestMessage.RequestUri.AbsolutePath.Contains("AccessDenied")) throw new Exception($"POST falló: {ruta} {r.StatusCode}\n{respuesta[..Math.Min(1500,respuesta.Length)]}");
            return respuesta;
        }
        async Task<string> PostJsonConToken(string ruta, string paginaToken, string json)
        {
            var html = await Get(paginaToken);
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            using var request = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("RequestVerificationToken", token);
            var r = await cliente.SendAsync(request);
            var respuesta = await r.Content.ReadAsStringAsync();
            if (r.StatusCode != HttpStatusCode.OK) throw new Exception($"POST JSON falló: {ruta} {r.StatusCode}\n{respuesta[..Math.Min(1500,respuesta.Length)]}");
            return respuesta;
        }
        try
        {
            bool listo = false;
            for (int n = 0; n < 45; n++)
            {
                if (proceso.HasExited) throw new Exception("Servidor de prueba terminó: " + await salida + await error);
                try { await Get("/Identity/Account/Login"); listo = true; break; } catch (HttpRequestException) { await Task.Delay(1000); }
            }
            if (!listo) throw new Exception("El servidor no arrancó.");
            var login = await Post("/Identity/Account/Login", new() { ["Input.Email"] = "admin@petmor.pe", ["Input.Password"] = "Admin123*" });
            if (!login.Contains("Mascotas activas")) throw new Exception("Login no llegó al panel autenticado.");
            if (login.Contains("ai-chat-widget") || login.Contains("enviarMensajeIA")) throw new Exception("El asistente retirado sigue en el layout.");
            var asistenteRetirado = await cliente.PostAsync("/IA/Consultar", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            if (asistenteRetirado.StatusCode != HttpStatusCode.NotFound) throw new Exception("La ruta retirada del asistente sigue activa.");
            foreach (var ruta in new[] { "/", "/Home", "/Home/Index", "/Petmor", "/Petmor/Index" })
            {
                var panel = await Get(ruta);
                if (!panel.Contains("Mascotas activas") || panel.Contains("Resumen comercial") || panel.Contains("graficoVentas"))
                    throw new Exception($"Dashboard distinto o incompleto en {ruta}");
            }
            foreach (var ruta in new[] { "/Petmor/Mascotas", "/Petmor/Mascota", "/Petmor/Agenda", "/Petmor/Agendar", "/Petmor/Spa", "/Petmor/Cuentas", "/Petmor/Servicios", "/Petmor/Servicio", "/Petmor/Reporte", "/Productos/Create", "/Reservas/Create" }) await Get(ruta);
            foreach (var ruta in new[] { "/Ventas?desde=2026-09-30&hasta=2026-09-01", "/Gastos?desde=2026-09-30&hasta=2026-09-01", "/Caja/Historial?desde=2026-09-30&hasta=2026-09-01", "/Kardex?desde=2026-09-30&hasta=2026-09-01", "/Reportes/Ventas?fechaInicio=2026-09-30&fechaFin=2026-09-01", "/Petmor/Agenda?fecha=9999-12-31" })
            {
                var response = await cliente.GetAsync(ruta);
                if (response.StatusCode != HttpStatusCode.BadRequest || !(await response.Content.ReadAsStringAsync()).Contains("Revisa el filtro"))
                    throw new Exception("Filtro inválido sin respuesta controlada: " + ruta);
            }
            Console.WriteLine("PASS HTTP AUDITORIA: rangos invertidos y límites de fecha en siete rutas");
            var sinToken = await cliente.PostAsync("/Reservas/Create", new FormUrlEncodedContent(new Dictionary<string, string>()));
            if (sinToken.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Reservas sin protección CSRF");
            int propietario; string responsable;
            await using(var db = new ApplicationDbContext(options))
            {
                var p = new Cliente { Nombre = "Propietario HTTP", Documento = "87654321", Telefono = "999999999" }; db.Clientes.Add(p); await db.SaveChangesAsync(); propietario = p.Id;
                responsable = await db.Users.Select(x => x.Id).SingleAsync();
            }
            await Post("/Petmor/Mascota", new() { ["Id"] = "0", ["ClienteId"] = propietario.ToString(), ["Nombre"] = "Luna HTTP", ["Especie"] = "Canino", ["Sexo"] = "Hembra", ["Activa"] = "true" });
            int mascota;
            await using(var db = new ApplicationDbContext(options)) mascota = (await db.Mascotas.SingleAsync()).Id;
            await Post("/Petmor/Servicio", new() { ["Id"] = "0", ["Nombre"] = "Consulta HTTP", ["Precio"] = "50", ["Area"] = "1", ["Duracion"] = "30", ["Estado"] = "true" });
            int servicio;
            await using(var db = new ApplicationDbContext(options)) servicio = (await db.Productos.SingleAsync(x => x.EsServicio && x.Nombre == "Consulta HTTP")).Id;
            await Post("/Petmor/Agendar", new() { ["MascotaId"] = mascota.ToString(), ["ServicioId"] = servicio.ToString(), ["Area"] = "1", ["ResponsableId"] = responsable, ["Motivo"] = "Consulta HTTP", ["Inicio"] = HoraPeru.Ahora.ToString("s"), ["Duracion"] = "30" });
            int atencion;
            await using(var db = new ApplicationDbContext(options)) atencion = (await db.Atenciones.SingleAsync()).Id;
            var agenda = "/Petmor/Agenda?activas=true";
            await Get(agenda);
            await Post("/Petmor/Estado", new() { ["id"] = atencion.ToString(), ["estado"] = "EnEspera" }, agenda);
            await Post("/Petmor/Estado", new() { ["id"] = atencion.ToString(), ["estado"] = "EnAtencion" }, agenda);
            await Post("/Petmor/Ficha", new() { ["id"] = atencion.ToString(), ["Evaluacion"] = "Evaluación HTTP", ["Peso"] = "8.25", ["Tratamiento"] = "Indicaciones de prueba" }, agenda);
            await Post("/Petmor/Cargo", new() { ["id"] = atencion.ToString(), ["productoId"] = servicio.ToString(), ["cantidad"] = "1" }, agenda);
            await Post("/Petmor/Estado", new() { ["id"] = atencion.ToString(), ["estado"] = "Lista" }, agenda);
            await Get($"/Petmor/Indicaciones/{atencion}");
            await using(var db = new ApplicationDbContext(options))
            {
                var a = await db.Atenciones.SingleAsync();
                if (a.Estado != EstadoAtencion.Lista || a.Peso != 8.25m) throw new Exception("El flujo HTTP no persistió la ficha o su estado.");
            }
            await Post("/Petmor/Estado", new() { ["id"] = atencion.ToString(), ["estado"] = "Entregada" }, agenda);
            await Post("/Petmor/Agendar", new() { ["MascotaId"] = mascota.ToString(), ["ServicioId"] = servicio.ToString(), ["ResponsableId"] = responsable, ["Duracion"] = "30" });
            int cancelada;
            await using(var db = new ApplicationDbContext(options)) cancelada = await db.Atenciones.MaxAsync(a => a.Id);
            await Post("/Petmor/Estado", new() { ["id"] = cancelada.ToString(), ["estado"] = "Cancelada", ["motivo"] = "Motivo QA <texto> & detalle" }, agenda);
            foreach (var ruta in new[] { "/", "/Petmor/Agenda", "/Petmor/Reporte", $"/Petmor/FichaMascota/{mascota}" })
            {
                var html = WebUtility.HtmlDecode(await Get(ruta));
                if (!html.Contains("data-reason=\"Motivo QA <texto> & detalle\"")) throw new Exception("Motivo ausente en " + ruta);
            }
            await using(var db = new ApplicationDbContext(options))
            {
                db.Clientes.AddRange(Enumerable.Range(1,55).Select(i => new Cliente { Nombre = $"PaginaQA {i:D3}", Documento = (10000000+i).ToString(), Telefono = "999999999" }));
                await db.SaveChangesAsync();
            }
            var segundaPagina = WebUtility.HtmlDecode(await Get("/Clientes?q=PaginaQA&pagina=2"));
            if (!segundaPagina.Contains("Página 2 de 2") || !segundaPagina.Contains("PaginaQA 055") || segundaPagina.Contains("PaginaQA 001"))
                throw new Exception("Paginación de clientes incorrecta");
            Console.WriteLine("PASS HTTP AUDITORIA: motivo consistente en cuatro vistas y paginación conservando búsqueda");
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=PaginaQA&draw=7&start=50&length=50")))
            {
                var root = datos.RootElement;
                if (root.GetProperty("draw").GetInt32() != 7 || root.GetProperty("recordsFiltered").GetInt32() != 55 ||
                    root.GetProperty("data").GetArrayLength() != 5 || root.GetProperty("data")[0].GetProperty("nombre").GetString() != "PaginaQA 051")
                    throw new Exception("DataTables clientes pierde registros o filtros al cambiar de página");
            }
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=Luna%20HTTP&length=10")))
                if (datos.RootElement.GetProperty("recordsFiltered").GetInt32() != 1 ||
                    datos.RootElement.GetProperty("data")[0].GetProperty("id").GetInt32() != propietario)
                    throw new Exception("DataTables clientes no busca por mascota");
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=PaginaQA&length=10&descendente=true")))
                if (datos.RootElement.GetProperty("data")[0].GetProperty("nombre").GetString() != "PaginaQA 055")
                    throw new Exception("DataTables clientes no ordena el conjunto completo");
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=sin-coincidencias-qa&length=10")))
                if (datos.RootElement.GetProperty("recordsFiltered").GetInt32() != 0 || datos.RootElement.GetProperty("data").GetArrayLength() != 0)
                    throw new Exception("DataTables clientes no devuelve vacío consistente");
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=PaginaQA&length=100")))
                if (datos.RootElement.GetProperty("recordsFiltered").GetInt32() != 55 || datos.RootElement.GetProperty("data").GetArrayLength() != 55)
                    throw new Exception("DataTables clientes no permite mostrar 100 registros");
            using (var datos = System.Text.Json.JsonDocument.Parse(await Get("/Clientes/DatosTabla?q=PaginaQA&length=-1")))
                if (datos.RootElement.GetProperty("recordsFiltered").GetInt32() != 55 || datos.RootElement.GetProperty("data").GetArrayLength() != 55)
                    throw new Exception("DataTables clientes no permite mostrar todos los registros");
            foreach (var parametros in new[] { "length=1000", "start=-1", "columna=9", "draw=-1" })
                if ((await cliente.GetAsync("/Clientes/DatosTabla?" + parametros)).StatusCode != HttpStatusCode.BadRequest)
                    throw new Exception("DataTables clientes permite parámetros inválidos: " + parametros);
            using (var anonimo = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = cliente.BaseAddress })
                if ((await anonimo.GetAsync("/Clientes/DatosTabla")).StatusCode != HttpStatusCode.Redirect)
                    throw new Exception("DataTables clientes expone datos sin autenticación");
            Console.WriteLine("PASS DATATABLES CLIENTES: paginación, búsqueda por mascota, orden, vacío, límites y autenticación.");
            int categoriaReserva;
            await using (var db = new ApplicationDbContext(options)) categoriaReserva = await db.Categorias.Select(c => c.Id).FirstAsync();
            var productoForm = new Dictionary<string, string> { ["Nombre"] = "Caducidad HTTP", ["CodigoBarras"] = "001234567891", ["CategoriaId"] = categoriaReserva.ToString(),
                ["Precio"] = "15", ["Costo"] = "5", ["Stock"] = "999", ["Estado"] = "true", ["ConCaducidad"] = "true",
                ["lecturas[0]"] = "001234567891", ["lecturas[1]"] = "001234567892", ["lecturas[2]"] = "ALT-HTTP", ["lecturas[3]"] = "ALT-HTTP-2" };
            await Post("/Productos/Create", productoForm);
            await using (var db = new ApplicationDbContext(options))
                if (await db.Productos.AnyAsync(p => p.Nombre == "Caducidad HTTP")) throw new Exception("Caducidad sin fecha guardada vía HTTP");
            productoForm["FechaVencimiento"] = HoraPeru.Hoy.AddDays(20).ToString("yyyy-MM-dd");
            await Post("/Productos/Create", productoForm);
            int productoReserva;
            await using (var db = new ApplicationDbContext(options)) {
                var creado = await db.Productos.Include(p => p.Codigos).SingleAsync(p => p.Nombre == "Caducidad HTTP");
                productoReserva = creado.Id;
                if (creado.Stock != 4 || creado.Codigos.Count != 4) throw new Exception("Alta HTTP aceptó stock manipulado o perdió códigos");
            }
            var comprasIndex = await Get("/Compras");
            if (!comprasIndex.Contains("Compras fue retirado") || !comprasIndex.Contains("Caducidad HTTP")) throw new Exception("Compras/Index no redirige al inventario con aviso");
            var comprasCreate = await Get($"/Compras/Create?productoId={productoReserva}");
            if (!comprasCreate.Contains("Caducidad HTTP") || !comprasCreate.Contains("Ajuste de stock")) throw new Exception("Compras/Create no redirige a Productos/Edit");
            var compraRetirada = await PostJsonConToken("/Compras/Registrar", $"/Productos/Edit/{productoReserva}", "{\"operacionId\":\"" + Guid.NewGuid() + "\",\"proveedorId\":1,\"numeroDocumento\":\"RETIRADA\",\"total\":1,\"detalles\":[{\"productoId\":" + productoReserva + ",\"cantidad\":1,\"precio\":1,\"total\":1,\"lecturas\":[\"ALT-COMPRA-HTTP\"]}]}");
            using (var respuestaCompra = System.Text.Json.JsonDocument.Parse(compraRetirada))
                if (respuestaCompra.RootElement.GetProperty("exito").GetBoolean() || !respuestaCompra.RootElement.GetProperty("mensaje").GetString().Contains("Compras fue retirado")) throw new Exception("Compras/Registrar no rechazó la ruta retirada");
            await using (var db = new ApplicationDbContext(options))
                if ((await db.Productos.Include(p => p.Codigos).SingleAsync(p => p.Id == productoReserva)).Stock != 4 || await db.CodigosProducto.AnyAsync(c => c.Codigo == "ALT-COMPRA-HTTP"))
                    throw new Exception("Compras/Registrar mutó stock o códigos pese a estar retirado");
            var opReserva = Guid.NewGuid().ToString();
            await Post("/Reservas/Create", new() { ["OperacionId"] = opReserva, ["ClienteId"] = propietario.ToString(), ["ProductoId"] = productoReserva.ToString(), ["Cantidad"] = "2", ["lecturas[0]"] = "ALT-HTTP", ["lecturas[1]"] = "ALT-HTTP-2" });
            int reservaId;
            await using (var db = new ApplicationDbContext(options))
            {
                reservaId = await db.ReservasProductos.Select(r => r.Id).SingleAsync();
                if ((await db.Productos.FindAsync(productoReserva)).Stock != 2) throw new Exception("Reserva HTTP no separó stock");
            }
            var cuentasReserva = await Get("/Petmor/Cuentas");
            if (!cuentasReserva.Contains("Caducidad HTTP") || !cuentasReserva.Contains("/Reservas/Cobrar")) throw new Exception("Reserva ausente en cuentas");
            await Post("/Reservas/Cancelar", new() { ["id"] = reservaId.ToString() }, "/Petmor/Cuentas");
            await using (var db = new ApplicationDbContext(options))
                if ((await db.Productos.FindAsync(productoReserva)).Stock != 4) throw new Exception("Cancelación HTTP no liberó stock");
            Console.WriteLine("PASS RESERVAS HTTP: CSRF; validación de caducidad; alta; listado en cuentas y cancelación con restitución de stock.");
            using (var lectura = System.Text.Json.JsonDocument.Parse(await Get("/Lecturas/Resolver?codigo=ALT-HTTP")))
                if (lectura.RootElement.GetProperty("id").GetInt32() != productoReserva) throw new Exception("No resuelve código alternativo HTTP");
            Dictionary<string, string> ProductoEditBase() => new() { ["Id"] = productoReserva.ToString(), ["Nombre"] = "Caducidad HTTP", ["CodigoBarras"] = "001234567891", ["CategoriaId"] = categoriaReserva.ToString(),
                ["Precio"] = "15", ["Costo"] = "5", ["Stock"] = "999", ["Estado"] = "true", ["ConCaducidad"] = "true", ["FechaVencimiento"] = HoraPeru.Hoy.AddDays(20).ToString("yyyy-MM-dd") };
            var ajuste = ProductoEditBase();
            ajuste["tipoAjuste"] = "ENTRADA"; ajuste["cantidad"] = "999"; ajuste["motivo"] = "Ajuste positivo por inventario físico"; ajuste["operacionId"] = Guid.NewGuid().ToString(); ajuste["lecturas[0]"] = "ALT-HTTP-3"; ajuste["lecturas[1]"] = "ALT-HTTP-4";
            await Post($"/Productos/Edit/{productoReserva}", ajuste); await Post($"/Productos/Edit/{productoReserva}", ajuste);
            var ajusteConflictivo = new Dictionary<string, string>(ajuste) { ["lecturas[0]"] = "ALT-HTTP-5", ["lecturas[1]"] = "ALT-HTTP-6" };
            await Post($"/Productos/Edit/{productoReserva}", ajusteConflictivo);
            await using (var db = new ApplicationDbContext(options))
                if ((await db.Productos.Include(p => p.Codigos).SingleAsync(p => p.Id == productoReserva)).Stock != 6 || await db.MovimientosInventario.CountAsync(m => m.OperacionId != null) != 1 ||
                    await db.CodigosProducto.AnyAsync(c => c.Codigo == "ALT-HTTP-5" || c.Codigo == "ALT-HTTP-6"))
                    throw new Exception("Editar producto aceptó cantidad manipulada o repitió ajuste");
            var salidaEscaneada = ProductoEditBase(); salidaEscaneada["tipoAjuste"] = "SALIDA"; salidaEscaneada["cantidad"] = "999"; salidaEscaneada["motivo"] = "Corrección administrativa"; salidaEscaneada["operacionId"] = Guid.NewGuid().ToString(); salidaEscaneada["lecturas[0]"] = "ALT-HTTP-3"; salidaEscaneada["lecturas[1]"] = "ALT-HTTP-4";
            await Post($"/Productos/Edit/{productoReserva}", salidaEscaneada);
            var sinMotivo = ProductoEditBase(); sinMotivo["tipoAjuste"] = "SALIDA"; sinMotivo["cantidad"] = "2"; sinMotivo["motivo"] = ""; sinMotivo["operacionId"] = Guid.NewGuid().ToString(); sinMotivo["lecturas[0]"] = "ALT-HTTP"; sinMotivo["lecturas[1]"] = "ALT-HTTP-2";
            await Post($"/Productos/Edit/{productoReserva}", sinMotivo);
            var sinExcepcion = ProductoEditBase(); sinExcepcion["tipoAjuste"] = "SALIDA"; sinExcepcion["cantidad"] = "2"; sinExcepcion["motivo"] = "Sin excepción explícita"; sinExcepcion["operacionId"] = Guid.NewGuid().ToString();
            await Post($"/Productos/Edit/{productoReserva}", sinExcepcion);
            await using (var db = new ApplicationDbContext(options))
                if ((await db.Productos.FindAsync(productoReserva)).Stock != 4) throw new Exception("Ajuste sin motivo modificó stock");
            Console.WriteLine("PASS LECTURAS HTTP: resolución alternativa; Editar deriva cantidad; reintento único; ajuste exige motivo y lecturas.");
            var editForm = ProductoEditBase(); editForm["tipoAjuste"] = "ENTRADA"; editForm["motivo"] = "Ingreso físico por escaneo"; editForm["operacionId"] = Guid.NewGuid().ToString(); editForm["lecturas[0]"] = "ALT-EDIT";
            await Post($"/Productos/Edit/{productoReserva}", editForm);
            await using (var db = new ApplicationDbContext(options)) {
                var editado = await db.Productos.Include(p => p.Codigos).SingleAsync(p => p.Id == productoReserva);
                if (editado.Stock != 5 || editado.Codigos.Count != 7) throw new Exception("Editar producto no ajustó stock o perdió asociaciones");
            }
            editForm["Nombre"] = "";
            editForm["lecturas[1]"] = "ALT-REINTENTO";
            var errorEdicion = await Post($"/Productos/Edit/{productoReserva}", editForm);
            if (!errorEdicion.Contains("\"ALT-REINTENTO\"")) throw new Exception("La vista perdió lecturas tras un error de validación");
            await using (var db = new ApplicationDbContext(options))
                if (await db.CodigosProducto.AnyAsync(c => c.Codigo == "ALT-REINTENTO")) throw new Exception("Una edición inválida persistió códigos");
            editForm["Nombre"] = "Caducidad HTTP"; editForm["operacionId"] = Guid.NewGuid().ToString();
            await Post($"/Productos/Edit/{productoReserva}", editForm);
            await using (var db = new ApplicationDbContext(options))
                if (!await db.CodigosProducto.AnyAsync(c => c.Codigo == "ALT-REINTENTO")) throw new Exception("No se pudo recuperar el borrador de códigos");
            await Post("/Usuarios/Create", new() { ["SucursalId"] = "1", ["UsuarioAcceso"] = "equipo", ["Email"] = "equipo@petmor.pe", ["Dni"] = "23456789", ["Nombres"] = "Equipo", ["Apellidos"] = "Prueba", ["Password"] = "Admin123*", ["Rol"] = "Recepcionista", ["RolesAdicionales[0]"] = "Veterinario", ["RolesAdicionales[1]"] = "Groomer" });
            await using(var db = new ApplicationDbContext(options))
            {
                var equipo = await db.Users.SingleAsync(x => x.Email == "equipo@petmor.pe");
                if (await db.UserRoles.CountAsync(x => x.UserId == equipo.Id) != 3) throw new Exception("No se asignaron múltiples perfiles.");
            }
            await Post("/Identity/Account/Login", new() { ["Input.Email"] = "equipo@petmor.pe", ["Input.Password"] = "Admin123*" });
            await Get("/Petmor/Mascotas");
            await Get("/Ventas/Create");
            await Get("/Caja");
            var panelClinico = await Get("/Home/Index");
            if (!panelClinico.Contains("Mascotas activas") || panelClinico.Contains("graficoVentas"))
                throw new Exception("El dashboard clínico no respeta los permisos financieros.");
            foreach(var restringida in new[]{"/Usuarios", $"/Productos/Edit/{productoReserva}", "/Productos/Create"})
            {
                var rechazo = await cliente.GetAsync(restringida);
                if (!rechazo.RequestMessage.RequestUri.AbsolutePath.Contains("AccessDenied") && rechazo.StatusCode != HttpStatusCode.Forbidden)
                    throw new Exception("Un perfil clínico accedió a una función de caja/administración.");
            }
            var tokenExcepcion = WebUtility.HtmlDecode(Regex.Match(await Get("/Reservas/Create"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var rechazoManual = await cliente.PostAsync($"/Productos/Edit/{productoReserva}", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = tokenExcepcion, ["Id"] = productoReserva.ToString(), ["cantidad"] = "1", ["tipoAjuste"] = "SALIDA",
                ["motivo"] = "Corrección administrativa", ["lecturas[0]"] = "ALT-HTTP", ["operacionId"] = Guid.NewGuid().ToString() }));
            if (!rechazoManual.RequestMessage.RequestUri.AbsolutePath.Contains("AccessDenied") && rechazoManual.StatusCode != HttpStatusCode.Forbidden)
                throw new Exception("El servidor permitió excepción manual a un no administrador");
            Console.WriteLine("PASS PETMOR HTTP: login real; todas las pantallas; registro de mascota, servicio y cita; atención y ficha clínica; finalización e impresión autenticadas.");
            Console.WriteLine("PASS PETMOR HTTP: alta de usuario con tres perfiles; recepción cobra y caja; administración bloqueada.");
        }
        finally
        {
            if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
            await proceso.WaitForExitAsync();
            await using var db = new ApplicationDbContext(options); await db.Database.EnsureDeletedAsync();
        }
    }
}
