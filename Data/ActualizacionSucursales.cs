using Microsoft.EntityFrameworkCore;

namespace MiniMarket.Web.Data;

public static class ActualizacionSucursales
{
    public static async Task Aplicar(ApplicationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('Sucursales','U') IS NULL
                CREATE TABLE Sucursales(Id int NOT NULL PRIMARY KEY, Nombre nvarchar(100) NOT NULL, Activa bit NOT NULL DEFAULT 1);
            IF NOT EXISTS(SELECT 1 FROM Sucursales WHERE Id=0) INSERT INTO Sucursales VALUES(0,N'Ambas sedes',1);
            IF NOT EXISTS(SELECT 1 FROM Sucursales WHERE Id=1) INSERT INTO Sucursales VALUES(1,N'San Miguel',1);
            IF NOT EXISTS(SELECT 1 FROM Sucursales WHERE Id=2) INSERT INTO Sucursales VALUES(2,N'Centro de Lima',1);
            IF OBJECT_ID('UsuariosSedes','U') IS NULL
                CREATE TABLE UsuariosSedes(UsuarioId nvarchar(450) NOT NULL PRIMARY KEY REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
                    SucursalId int NOT NULL REFERENCES Sucursales(Id));
            IF OBJECT_ID('ExistenciasSucursales','U') IS NULL
            BEGIN
                CREATE TABLE ExistenciasSucursales(ProductoId int NOT NULL REFERENCES Productos(Id) ON DELETE CASCADE,
                    SucursalId int NOT NULL REFERENCES Sucursales(Id), Cantidad int NOT NULL,
                    CONSTRAINT PK_ExistenciasSucursales PRIMARY KEY(ProductoId,SucursalId));
                INSERT INTO ExistenciasSucursales(ProductoId,SucursalId,Cantidad) SELECT Id,1,Stock FROM Productos;
            END;
            """);
        foreach (var tabla in new[] { "Ventas", "Compras", "Gastos", "AperturasCaja", "MovimientosInventario", "Atenciones" })
        {
            // Nombres definidos por la aplicación, nunca procedentes de solicitudes.
            var sql = $"""
                IF COL_LENGTH('{tabla}','SucursalId') IS NULL
                BEGIN
                    ALTER TABLE [{tabla}] ADD SucursalId int NOT NULL CONSTRAINT [DF_{tabla}_Sede] DEFAULT 1;
                    ALTER TABLE [{tabla}] ADD CONSTRAINT [FK_{tabla}_Sede] FOREIGN KEY(SucursalId) REFERENCES Sucursales(Id);
                    CREATE INDEX [IX_{tabla}_SucursalId] ON [{tabla}](SucursalId);
                END;
                """;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO UsuariosSedes(UsuarioId,SucursalId)
                SELECT Id,1 FROM AspNetUsers u WHERE NOT EXISTS(SELECT 1 FROM UsuariosSedes s WHERE s.UsuarioId=u.Id);
            IF OBJECT_ID('ReservasProductos','U') IS NULL
            BEGIN
                CREATE TABLE ReservasProductos (
                    Id int IDENTITY PRIMARY KEY,
                    SucursalId int NOT NULL DEFAULT 1 REFERENCES Sucursales(Id),
                    ClienteId int NOT NULL REFERENCES Clientes(Id),
                    ProductoId int NOT NULL REFERENCES Productos(Id),
                    Cantidad int NOT NULL, Precio decimal(18,2) NOT NULL,
                    Descripcion nvarchar(max) NOT NULL, Estado nvarchar(20) NULL,
                    Observaciones nvarchar(300) NULL, Usuario nvarchar(max) NULL,
                    Fecha datetime2 NOT NULL, OperacionId uniqueidentifier NOT NULL,
                    OperacionCobro uniqueidentifier NOT NULL, VentaId int NULL REFERENCES Ventas(Id),
                    FechaCierre datetime2 NULL, CerradaPor nvarchar(max) NULL);
                CREATE UNIQUE INDEX IX_ReservasProductos_OperacionId ON ReservasProductos(OperacionId);
                CREATE UNIQUE INDEX IX_ReservasProductos_OperacionCobro ON ReservasProductos(OperacionCobro);
                CREATE INDEX IX_ReservasProductos_SucursalId ON ReservasProductos(SucursalId);
                CREATE INDEX IX_ReservasProductos_ClienteId ON ReservasProductos(ClienteId);
                CREATE INDEX IX_ReservasProductos_ProductoId ON ReservasProductos(ProductoId);
                CREATE INDEX IX_ReservasProductos_VentaId ON ReservasProductos(VentaId);
            END;
            """);
        await tx.CommitAsync();
    }
}
