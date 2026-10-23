using Microsoft.EntityFrameworkCore;

namespace MiniMarket.Web.Data;

// Upgrade additive para instalaciones anteriores creadas con EnsureCreated.
// No asigna costos actuales a ventas históricas ni altera los importes de cierres existentes.
public static class ActualizacionComercial
{
    public static async Task Aplicar(ApplicationDbContext db, string zonaHorariaAnterior = null)
    {
        await db.Database.EnsureCreatedAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await BloquearOperaciones(db);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource='MiniMarket.Schema.Comercial', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
            IF @r < 0 THROW 51000, 'No se pudo bloquear la actualizacion del esquema.', 1;
            IF COL_LENGTH('Productos','ConCaducidad') IS NULL ALTER TABLE Productos ADD ConCaducidad bit NOT NULL DEFAULT 0;
            IF COL_LENGTH('Productos','FechaVencimiento') IS NULL ALTER TABLE Productos ADD FechaVencimiento datetime2 NULL;
            IF COL_LENGTH('Ventas','OperacionId') IS NULL ALTER TABLE Ventas ADD OperacionId uniqueidentifier NULL;
            IF COL_LENGTH('Ventas','TicketHistoricoJson') IS NULL ALTER TABLE Ventas ADD TicketHistoricoJson nvarchar(max) NULL;
            IF COL_LENGTH('Ventas','FechaIntentoEmision') IS NULL ALTER TABLE Ventas ADD FechaIntentoEmision datetime2 NULL;
            IF COL_LENGTH('Ventas','RespuestaEmision') IS NULL ALTER TABLE Ventas ADD RespuestaEmision nvarchar(4000) NULL;
            IF COL_LENGTH('DetalleVentas','DescripcionHistorica') IS NULL ALTER TABLE DetalleVentas ADD DescripcionHistorica nvarchar(max) NULL;
            IF COL_LENGTH('DetalleVentas','LecturasJson') IS NULL ALTER TABLE DetalleVentas ADD LecturasJson nvarchar(max) NULL;
            IF COL_LENGTH('Compras','OperacionId') IS NULL ALTER TABLE Compras ADD OperacionId uniqueidentifier NULL;
            IF COL_LENGTH('Compras','HuellaSolicitud') IS NULL ALTER TABLE Compras ADD HuellaSolicitud nvarchar(64) NULL;
            IF COL_LENGTH('Ventas','HuellaSolicitud') IS NULL ALTER TABLE Ventas ADD HuellaSolicitud nvarchar(64) NULL;
            IF COL_LENGTH('Ventas','AperturaCajaId') IS NULL ALTER TABLE Ventas ADD AperturaCajaId int NULL;
            IF COL_LENGTH('Ventas','EstadoEmision') IS NULL ALTER TABLE Ventas ADD EstadoEmision nvarchar(40) NOT NULL CONSTRAINT DF_Ventas_Emision DEFAULT 'Sin registro';
            IF COL_LENGTH('Ventas','FechaAnulacion') IS NULL ALTER TABLE Ventas ADD FechaAnulacion datetime2 NULL;
            IF COL_LENGTH('Ventas','AnuladaPor') IS NULL ALTER TABLE Ventas ADD AnuladaPor nvarchar(max) NULL;
            IF COL_LENGTH('Ventas','MotivoAnulacion') IS NULL ALTER TABLE Ventas ADD MotivoAnulacion nvarchar(max) NULL;
            IF COL_LENGTH('Ventas','AjustePosteriorCierre') IS NULL ALTER TABLE Ventas ADD AjustePosteriorCierre bit NOT NULL CONSTRAINT DF_Ventas_Ajuste DEFAULT 0;
            IF COL_LENGTH('Ventas','Recargo') IS NULL ALTER TABLE Ventas ADD Recargo decimal(18,2) NOT NULL CONSTRAINT DF_Ventas_Recargo DEFAULT 0;
            IF COL_LENGTH('DetalleVentas','CostoUnitarioHistorico') IS NULL ALTER TABLE DetalleVentas ADD CostoUnitarioHistorico decimal(18,2) NULL;
            IF COL_LENGTH('AperturasCaja','VentasEfectivo') IS NULL ALTER TABLE AperturasCaja ADD VentasEfectivo decimal(18,2) NULL;
            IF COL_LENGTH('AperturasCaja','AjustesAnulaciones') IS NULL ALTER TABLE AperturasCaja ADD AjustesAnulaciones decimal(18,2) NOT NULL CONSTRAINT DF_Caja_Ajustes DEFAULT 0;
            """);
        // Batch separado para que SQL compile con las columnas ya presentes.
        await ActualizacionCodigos.ValidarHistoricos(db);
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Ventas_OperacionId' AND object_id=OBJECT_ID('Ventas'))
                CREATE UNIQUE INDEX IX_Ventas_OperacionId ON Ventas(OperacionId) WHERE OperacionId IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Compras_OperacionId' AND object_id=OBJECT_ID('Compras'))
                CREATE UNIQUE INDEX IX_Compras_OperacionId ON Compras(OperacionId) WHERE OperacionId IS NOT NULL;
            IF COL_LENGTH('Productos','CodigoBarras') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Productos') AND name='CodigoBarras' AND max_length=-1)
                BEGIN
                    IF EXISTS (SELECT 1 FROM Productos WHERE CodigoBarras IS NOT NULL AND LEN(CodigoBarras) > 100)
                        THROW 51002, 'Hay códigos de barras con más de 100 caracteres. Corríjalos antes de crear el índice único.', 1;
                    ALTER TABLE Productos ALTER COLUMN CodigoBarras nvarchar(100) NULL;
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Productos_CodigoBarras' AND object_id=OBJECT_ID('Productos'))
                    AND NOT EXISTS (SELECT CodigoBarras FROM Productos WHERE CodigoBarras IS NOT NULL AND CodigoBarras <> '' GROUP BY CodigoBarras HAVING COUNT(*) > 1)
                    CREATE UNIQUE INDEX IX_Productos_CodigoBarras ON Productos(CodigoBarras) WHERE CodigoBarras IS NOT NULL AND CodigoBarras <> '';
            END;
            UPDATE v SET AperturaCajaId = c.Id
            FROM Ventas v JOIN AspNetUsers u ON u.Id=v.UsuarioId
            CROSS APPLY (SELECT TOP(1) a.Id FROM AperturasCaja a
                WHERE a.UsuarioId=u.UserName AND v.Fecha>=a.FechaApertura
                AND (a.FechaCierre IS NULL OR v.Fecha<=a.FechaCierre)
                ORDER BY a.FechaApertura DESC, a.Id DESC) c
            WHERE v.AperturaCajaId IS NULL;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('UsuarioPerfiles', 'U') IS NULL
            BEGIN
                CREATE TABLE UsuarioPerfiles (
                    UsuarioId nvarchar(450) NOT NULL CONSTRAINT PK_UsuarioPerfiles PRIMARY KEY,
                    Dni nvarchar(8) NOT NULL,
                    Nombres nvarchar(100) NOT NULL,
                    Apellidos nvarchar(100) NOT NULL,
                    CONSTRAINT FK_UsuarioPerfiles_AspNetUsers_UsuarioId FOREIGN KEY (UsuarioId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
                );
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UsuarioPerfiles_Dni' AND object_id=OBJECT_ID('UsuarioPerfiles'))
                CREATE UNIQUE INDEX IX_UsuarioPerfiles_Dni ON UsuarioPerfiles(Dni);
            """);
        await CorreccionHorarioPeru.Aplicar(db, zonaHorariaAnterior);
        await tx.CommitAsync();
        await tx.DisposeAsync();
        await ActualizacionCodigos.Aplicar(db);
        await ActualizacionCodigos.PurgarVendidos(db);
    }

    public static Task BloquearOperaciones(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @r int;
        EXEC @r=sp_getapplock @Resource='MiniMarket.OperacionesCaja', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
        IF @r < 0 THROW 51001, 'Otra operacion esta en curso. Reintente.', 1;
        """);
}
