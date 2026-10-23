using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Petmor.Web.Models;
using Petmor.Web.Services;

namespace Petmor.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        private readonly SedeActual sede;
        public int SucursalId => sede.Id;
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, SedeActual sede = null)
            : base(options)
        {
            this.sede = sede ?? new SedeActual { Id = 1, Nombre = "San Miguel" };
        }

        public DbSet<Sucursal> Sucursales { get; set; }
        public DbSet<UsuarioSede> UsuariosSedes { get; set; }
        public DbSet<ExistenciaSucursal> ExistenciasSucursales { get; set; }

        public DbSet<Producto> Productos { get; set; }
        public DbSet<CodigoProducto> CodigosProducto { get; set; }
        public DbSet<ReservaProducto> ReservasProductos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
        public DbSet<Venta> Ventas { get; set; }
        public DbSet<DetalleVenta> DetalleVentas { get; set; }
        public DbSet<Compra> Compras { get; set; }
        public DbSet<DetalleCompra> DetalleCompras { get; set; }
        
        // --- RESTAURADO: Tabla de Caja que faltaba ---
        public DbSet<AperturaCaja> AperturasCaja { get; set; }

        public DbSet<Gasto> Gastos { get; set; }

        // --- NUEVO: Tabla de Configuración ---
        public DbSet<Configuracion> Configuraciones { get; set; }
        public DbSet<UsuarioPerfil> UsuarioPerfiles { get; set; }
        public DbSet<Mascota> Mascotas { get; set; }
        public DbSet<Atencion> Atenciones { get; set; }
        public DbSet<CargoAtencion> CargosAtencion { get; set; }
        public DbSet<ConsumoAtencion> ConsumosAtencion { get; set; }
        public DbSet<EventoAtencion> EventosAtencion { get; set; }
        public DbSet<PreventivoMascota> PreventivosMascota { get; set; }
        public DbSet<ArchivoClinico> ArchivosClinicos { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Sucursal>().Property(x => x.Id).ValueGeneratedNever();
            builder.Entity<Sucursal>().HasData(new Sucursal { Id = 0, Nombre = "Ambas sedes" }, new Sucursal { Id = 1, Nombre = "San Miguel" }, new Sucursal { Id = 2, Nombre = "Centro de Lima" });
            builder.Entity<UsuarioSede>().HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<UsuarioSede>().HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ExistenciaSucursal>().HasKey(x => new { x.ProductoId, x.SucursalId });
            builder.Entity<ExistenciaSucursal>().HasOne(x => x.Producto).WithMany(x => x.Existencias).HasForeignKey(x => x.ProductoId);
            builder.Entity<ExistenciaSucursal>().HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ExistenciaSucursal>().HasQueryFilter(x => x.SucursalId == SucursalId);
            builder.Entity<Producto>().Navigation(x => x.Existencias).AutoInclude();
            builder.Entity<CodigoProducto>().HasIndex(x => x.Codigo).IsUnique();
            builder.Entity<CodigoProducto>().HasOne(x => x.Producto).WithMany(x => x.Codigos).HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Producto>().Property(x => x.CodigoBarras).HasMaxLength(100);
            builder.Entity<Producto>().HasIndex(x => x.CodigoBarras).IsUnique().HasFilter("[CodigoBarras] IS NOT NULL AND [CodigoBarras] <> ''");
            // Se exige teléfono en formularios; se conservan NULL históricos sin inventar datos.
            builder.Entity<Cliente>().Property(x => x.Telefono).IsRequired(false);
            builder.Entity<Compra>().HasIndex(x => x.OperacionId).IsUnique().HasFilter("[OperacionId] IS NOT NULL");
            ConfigurarSede<Venta>(builder);
            ConfigurarSede<Compra>(builder);
            ConfigurarSede<Gasto>(builder);
            ConfigurarSede<AperturaCaja>(builder);
            ConfigurarSede<MovimientoInventario>(builder);
            builder.Entity<MovimientoInventario>().HasIndex(x => x.OperacionId).IsUnique().HasFilter("[OperacionId] IS NOT NULL");
            ConfigurarSede<Atencion>(builder);
            ConfigurarSede<ReservaProducto>(builder);
            builder.Entity<ReservaProducto>().HasIndex(x => x.OperacionId).IsUnique();
            builder.Entity<ReservaProducto>().HasIndex(x => x.OperacionCobro).IsUnique();
            builder.Entity<ReservaProducto>().HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ReservaProducto>().HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ReservaProducto>().HasOne(x => x.Venta).WithMany().HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<DetalleVenta>().HasQueryFilter(x => EF.Property<int>(x.Venta, "SucursalId") == SucursalId);
            builder.Entity<DetalleCompra>().HasQueryFilter(x => EF.Property<int>(x.Compra, "SucursalId") == SucursalId);
            builder.Entity<CargoAtencion>().HasQueryFilter(x => EF.Property<int>(x.Atencion, "SucursalId") == SucursalId);
            builder.Entity<ConsumoAtencion>().HasQueryFilter(x => EF.Property<int>(x.Atencion, "SucursalId") == SucursalId);
            builder.Entity<EventoAtencion>().HasQueryFilter(x => EF.Property<int>(x.Atencion, "SucursalId") == SucursalId);
            builder.Entity<Mascota>().HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Atencion>().HasOne(x => x.Mascota).WithMany().HasForeignKey(x => x.MascotaId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Atencion>().HasOne(x => x.Venta).WithMany().HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Atencion>().HasIndex(x => x.OperacionCobro).IsUnique();
            builder.Entity<Atencion>().HasIndex(x => new { x.ResponsableId, x.Inicio });
            builder.Entity<CargoAtencion>().HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ConsumoAtencion>().HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ConsumoAtencion>().HasIndex(x => x.OperacionId).IsUnique();
            builder.Entity<ArchivoClinico>().HasOne(x => x.Mascota).WithMany().HasForeignKey(x => x.MascotaId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ArchivoClinico>().HasOne(x => x.Atencion).WithMany().HasForeignKey(x => x.AtencionId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<UsuarioPerfil>().HasIndex(p => p.Dni).IsUnique();
            builder.Entity<Venta>().HasIndex(v => v.OperacionId).IsUnique()
                .HasFilter("[OperacionId] IS NOT NULL");
        }

        private void ConfigurarSede<T>(ModelBuilder builder) where T : class
        {
            builder.Entity<T>().Property<int>("SucursalId").HasDefaultValue(1);
            builder.Entity<T>().HasIndex("SucursalId");
            builder.Entity<T>().HasOne<Sucursal>().WithMany().HasForeignKey("SucursalId").OnDelete(DeleteBehavior.Restrict);
            builder.Entity<T>().HasQueryFilter(x => EF.Property<int>(x, "SucursalId") == SucursalId);
        }

        private void PrepararSede()
        {
            // También cubre sembradores y editores antiguos que escriben el código principal.
            // El índice de CodigosProducto arbitra conflictos concurrentes entre principales y alternativos.
            foreach (var entry in ChangeTracker.Entries<Producto>().Where(e => e.State == EntityState.Added ||
                (e.State == EntityState.Modified && e.Property(p => p.CodigoBarras).IsModified)).ToList())
            {
                var codigo = entry.Entity.CodigoBarras;
                if (string.IsNullOrEmpty(codigo)) continue;
                codigo = CodigosLecturas.Canonicalizar(codigo);
                entry.Entity.CodigoBarras = codigo;
                var existente = CodigosProducto.AsNoTracking().SingleOrDefault(c => c.Codigo == codigo);
                if (existente != null && existente.ProductoId != entry.Entity.Id)
                    throw new InvalidOperationException("El código ya pertenece a otro producto.");
                if (existente == null && !entry.Entity.Codigos.Any(c => string.Equals(c.Codigo, codigo, StringComparison.OrdinalIgnoreCase)))
                    entry.Entity.Codigos.Add(new CodigoProducto { Codigo = codigo });
            }
            ChangeTracker.DetectChanges();
            foreach (var entry in ChangeTracker.Entries<CodigoProducto>().Where(e => e.State is EntityState.Added or EntityState.Modified))
                entry.Entity.Codigo = CodigosLecturas.Canonicalizar(entry.Entity.Codigo);
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                if (entry.Metadata.FindProperty("SucursalId") == null || entry.Entity is UsuarioSede) continue;
                if (SucursalId <= 0) throw new InvalidOperationException("Seleccione una sede válida.");
                var propiedad = entry.Property("SucursalId");
                if (entry.State == EntityState.Added)
                {
                    if ((int)propiedad.CurrentValue != 0 && (int)propiedad.CurrentValue != SucursalId)
                        throw new InvalidOperationException("La operación pertenece a otra sede.");
                    propiedad.CurrentValue = SucursalId;
                }
                else if ((int)propiedad.OriginalValue != SucursalId || (int)propiedad.CurrentValue != SucursalId)
                    throw new InvalidOperationException("No se puede modificar una operación de otra sede.");
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepararSede();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            PrepararSede();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}
