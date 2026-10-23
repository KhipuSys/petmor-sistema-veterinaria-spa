using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Base de Datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2. Identidad (Usuarios y Roles)
builder.Services.AddDefaultIdentity<IdentityUser>(options => 
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();
// Rechaza inmediatamente sesiones antiguas cuando cambia el correo/credenciales.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// 3. Servicios Web
builder.Services.AddControllersWithViews(options => {
    options.MaxModelBindingCollectionSize = MiniMarket.Web.Services.CodigosLecturas.MaxLecturas;
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new MiniMarket.Web.Infrastructure.FiltroFechas());
});
builder.Services.AddRazorPages(); // ¡IMPORTANTE PARA EL LOGIN!
builder.Services.AddScoped<MiniMarket.Web.Services.SedeActual>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Antiforgery.IAntiforgeryAdditionalDataProvider, MiniMarket.Web.Infrastructure.SedeAntiforgeryData>();
builder.Services.AddHttpClient<MiniMarket.Web.Services.FactilizaService>();
builder.Services.AddScoped<MiniMarket.Web.Services.AtencionService>();
builder.Services.AddScoped<MiniMarket.Web.Services.EmisionVentaService>();

var app = builder.Build();

// 4. Inicializar Datos (Sembrador)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    services.GetRequiredService<MiniMarket.Web.Services.SedeActual>().Id = 1;
    await ActualizacionComercial.Aplicar(services.GetRequiredService<ApplicationDbContext>());
    await ActualizacionVeterinaria.Aplicar(services.GetRequiredService<ApplicationDbContext>());
    await ActualizacionSucursales.Aplicar(services.GetRequiredService<ApplicationDbContext>());
    try
    {
        await SembradorDatos.Inicializar(services);
        await ActualizacionSucursales.Aplicar(services.GetRequiredService<ApplicationDbContext>());
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error al sembrar datos.");
    }
}

// 5. Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<MiniMarket.Web.Infrastructure.SedeMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages(); // ¡IMPORTANTE!

app.Run();
