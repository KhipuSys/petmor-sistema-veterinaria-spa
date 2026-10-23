using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Models;

namespace Petmor.Web.Data;

public static class SembradorDatos
{
    public static async Task Inicializar(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var nombre in new[] { "Administrador", "Recepcionista", "Veterinario", "Groomer" })
        {
            if (!await roles.RoleExistsAsync(nombre)) Comprobar(await roles.CreateAsync(new IdentityRole(nombre)));
        }
        foreach (var (anterior, nuevo) in new[] { ("Recepcion", "Recepcionista"), ("Cajero", "Recepcionista"), ("Spa", "Groomer") })
        {
            if (!await roles.RoleExistsAsync(anterior)) continue;
            foreach (var user in await users.GetUsersInRoleAsync(anterior))
                if (!await users.IsInRoleAsync(user, nuevo)) Comprobar(await users.AddToRoleAsync(user, nuevo));
            var rolAnterior = await roles.FindByNameAsync(anterior);
            if (rolAnterior != null) Comprobar(await roles.DeleteAsync(rolAnterior));
        }
        var admin = await users.FindByNameAsync("admin@petmor.pe")
            ?? await users.FindByNameAsync("admin@petmor.local");

        if (admin == null)
        {
            admin = new IdentityUser { UserName = "admin@petmor.pe", Email = "admin@petmor.pe", EmailConfirmed = true };
            Comprobar(await users.CreateAsync(admin, "Admin123*"));
        }
        else
        {
            admin.UserName = "admin@petmor.pe";
            admin.NormalizedUserName = "ADMIN@PETMOR.PE";
            admin.Email = "admin@petmor.pe";
            admin.NormalizedEmail = "ADMIN@PETMOR.PE";
            admin.EmailConfirmed = true;
            admin.AccessFailedCount = 0;
            admin.LockoutEnd = null;
            Comprobar(await users.UpdateAsync(admin));
            Comprobar(await users.ResetPasswordAsync(admin, await users.GeneratePasswordResetTokenAsync(admin), "Admin123*"));
        }

        if (!await users.IsInRoleAsync(admin, "Administrador"))
            Comprobar(await users.AddToRoleAsync(admin, "Administrador"));
        var config = await db.Configuraciones.FirstOrDefaultAsync();
        if (config == null)
        {
            db.Configuraciones.Add(new Configuracion { NombreEmpresa = "Petmor", Ruc = "00000000000", Direccion = "Configure el sistema", IgvPorcentaje = 18, MonedaSimbolo = "S/." });
        }
        else
        {
            if (config.MonedaSimbolo == "S/") config.MonedaSimbolo = "S/.";
        }
        foreach (var nombre in new[] { "Medicamentos", "Alimentos para mascotas", "Accesorios", "Insumos veterinarios", "Insumos de spa", "Servicios veterinarios", "Servicios de spa" })
        {
            if (!await db.Categorias.AnyAsync(c => c.Nombre == nombre)) db.Categorias.Add(new Categoria { Nombre = nombre, Estado = true });
        }
        // Solo catálogos: las ventas y existencias deben proceder de operaciones reales.
        await db.SaveChangesAsync();
        await SembrarServiciosPeluqueria(db);
        await db.SaveChangesAsync();
    }

    private static async Task SembrarServiciosPeluqueria(ApplicationDbContext db)
    {
        var categoria = await db.Categorias.FirstAsync(c => c.Nombre == "Servicios de spa");
        var servicios = ServiciosPeluqueria();
        var nombres = servicios.Select(s => s.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existentes = await db.Productos.Where(p => p.EsServicio).ToListAsync();
        var usados = await db.CargosAtencion.IgnoreQueryFilters().Select(c => c.ProductoId).Distinct().ToListAsync();
        var usadosSet = usados.ToHashSet();

        foreach (var anterior in existentes.Where(p => !nombres.Contains(p.Nombre)).ToList())
        {
            if (usadosSet.Contains(anterior.Id)) anterior.Estado = false;
            else db.Productos.Remove(anterior);
        }

        for (var i = 0; i < servicios.Length; i++)
        {
            var item = servicios[i];
            var servicio = existentes.FirstOrDefault(p => string.Equals(p.Nombre, item.Nombre, StringComparison.OrdinalIgnoreCase));
            if (servicio == null)
            {
                servicio = new Producto { Nombre = item.Nombre, EsServicio = true, CodigoBarras = $"SRV-PELU-{i + 1:000}" };
                db.Productos.Add(servicio);
            }
            servicio.Descripcion = null;
            servicio.CategoriaId = categoria.Id;
            servicio.Precio = item.Precio;
            servicio.Costo = 0;
            servicio.Estado = true;
            servicio.UsoInterno = false;
            servicio.ConCaducidad = false;
            servicio.FechaVencimiento = null;
            servicio.AreaServicio = AreaAtencion.Spa;
            servicio.DuracionMinutos = item.Duracion;
            servicio.Unidad = "unidad";
        }
    }

    private static ServicioSeed[] ServiciosPeluqueria() =>
    [
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO PEQUEÑO 1 - 5 KG", 35, 60),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO PEQUEÑO 6 - 10 KG", 40, 60),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO MEDIANO 11 - 15 KG", 45, 75),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO MEDIANO 16 - 20 KG", 50, 75),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO GRANDE 21 - 25 KG", 55, 90),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO GRANDE 26 - 30 KG", 60, 90),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO GRANDE 31 - 40 KG", 70, 105),
        new("BAÑO REGULAR - COSMETICO O AVENA PERRO GRANDE 32 - 40 KG", 70, 105),
        new("BAÑO REGULAR - ANTIPULGAS PERRO PEQUEÑO 1 - 5 KG", 40, 60),
        new("BAÑO REGULAR - ANTIPULGAS PERRO PEQUEÑO 6 - 10 KG", 45, 60),
        new("BAÑO REGULAR - ANTIPULGAS PERRO MEDIANO 11 - 15 KG", 50, 75),
        new("BAÑO REGULAR - ANTIPULGAS PERRO MEDIANO 16 - 20 KG", 55, 75),
        new("BAÑO REGULAR - ANTIPULGAS PERRO GRANDE 21 - 25 KG", 60, 90),
        new("BAÑO REGULAR - ANTIPULGAS PERRO GRANDE 26 - 30 KG", 65, 90),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO PEQUEÑO 1 - 5 KG", 45, 75),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO PEQUEÑO 6 - 10 KG", 50, 75),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO MEDIANO 11 - 15 KG", 55, 90),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO MEDIANO 16 - 20 KG", 60, 90),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO GRANDE 21 - 25 KG", 65, 105),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO GRANDE 26 - 30 KG", 75, 105),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO ENORME 31 - 35 KG", 85, 120),
        new("BAÑO PIEL SENSIBLE - MEDICADO PERRO ENORME 36 - 40 KG", 90, 120),
        new("BAÑO CACHORRO REGULAR - CACHORROS 2 MESES", 25, 45),
        new("BAÑO CACHORRO REGULAR - CACHORROS DE 3 A 6 MESES", 30, 45),
        new("BAÑO REGULAR - GATO", 50, 75),
        new("BAÑO REGULAR - GATO EN SECO", 50, 60),
        new("BAÑO REGULAR - RETOQUE CONEJO", 45, 45),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO PEQUEÑO 1 - 5 KG", 50, 90),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO PEQUEÑO 6 - 10 KG", 60, 90),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO MEDIANO 11 - 15 KG", 65, 105),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO MEDIANO 16 - 20 KG", 70, 105),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO GRANDE 21 - 25 KG", 75, 120),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO GRANDE 26 - 30 KG", 80, 120),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO GRANDE 31 - 35 KG", 85, 135),
        new("CORTE TOTAL - BAÑO COSMETICO + CORTE PERRO GRANDE 36 - 40 KG", 90, 135),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO PEQUEÑO 1 - 5 KG", 55, 90),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO PEQUEÑO 6 - 10 KG", 60, 90),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO MEDIANO 11 - 15 KG", 65, 105),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO MEDIANO 16 - 20 KG", 70, 105),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO GRANDE 21 - 25 KG", 75, 120),
        new("CORTE TOTAL - BAÑO ANTIPULGAS + CORTE PERRO GRANDE 26 - 30 KG", 80, 120),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO PEQUEÑO 1 - 5 KG", 60, 105),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO PEQUEÑO 6 - 10 KG", 65, 105),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO MEDIANO 11 - 15 KG", 70, 120),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO MEDIANO 16 - 20 KG", 75, 120),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO GRANDE 21 - 25 KG", 80, 135),
        new("CORTE TOTAL - BAÑO MEDICADO + CORTE PERRO GRANDE 26 - 30 KG", 95, 135),
        new("CORTE TOTAL - PERRO PEQUEÑO 1 - 10 KG", 35, 60),
        new("CORTE TOTAL - PERRO MEDIANO 11 - 20 KG", 45, 75),
        new("CORTE TOTAL - PERRO GRANDE 21 - 30 KG", 55, 90),
        new("CORTE ESTILO - GATO", 15, 45),
        new("ESPECIAL - ACICALADO PERRO PEQUEÑO 1 - 10 KG", 15, 30),
        new("ESPECIAL - ACICALADO PERRO MEDIANO 11 - 20 KG", 20, 45),
        new("ESPECIAL - ACICALADO PERRO GRANDE 21 - 30 KG", 30, 60),
        new("ESPECIAL - CARDING O DESLANADO PERRO PEQUEÑO 1 - 10 KG", 10, 30),
        new("ESPECIAL - CARDING O DESLANADO PERRO MEDIANO 11 - 20 KG", 20, 45),
        new("ESPECIAL - CARDING O DESLANADO PERRO GRANDE 21 - 30 KG", 30, 60),
        new("EXTRAS TODOS - GOTAS TRANQUILIZANTES", 10, 15),
        new("EXTRAS TODOS - CORTE DE UÑAS", 10, 15),
        new("EXTRAS TODOS - CORTE HIGIENICO", 15, 20),
        new("EXTRAS TODOS - DESMOTADO LEVE", 10, 30),
        new("EXTRAS TODOS - DESMOTADO MEDIO", 20, 45),
        new("EXTRAS TODOS - DESMOTADO GRAVE", 30, 60),
        new("EXTRAS TODOS - REACONDICIONADOR PERRO PEQUEÑO 1 - 10 KG", 5, 15),
        new("EXTRAS TODOS - REACONDICIONADOR PERRO MEDIANO 11 - 20 KG", 10, 15),
        new("EXTRAS TODOS - REACONDICIONADOR PERRO GRANDE 21 - 30 KG", 15, 15),
        new("EXTRAS TODOS - DEPILACION DE OREJAS", 10, 15),
        new("EXTRAS TODOS - CEPILLADO DE DIENTES", 15, 15),
        new("EXTRAS TODOS - LIMPIEZA DE OIDOS", 15, 15),
    ];

    private record ServicioSeed(string Nombre, decimal Precio, int Duracion);

    private static void Comprobar(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
