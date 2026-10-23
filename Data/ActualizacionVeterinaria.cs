using Microsoft.EntityFrameworkCore;

namespace Petmor.Web.Data;

// Sigue la actualización aditiva de la instalación existente (creada con EnsureCreated).
public static class ActualizacionVeterinaria
{
    public static async Task Aplicar(ApplicationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('Productos','EsServicio') IS NULL ALTER TABLE Productos ADD EsServicio bit NOT NULL DEFAULT 0;
            IF OBJECT_ID('Configuraciones','U') IS NOT NULL AND COL_LENGTH('Configuraciones','FactilizaTokenCifrado') IS NULL ALTER TABLE Configuraciones ADD FactilizaTokenCifrado nvarchar(max) NULL;
            IF COL_LENGTH('Productos','UsoInterno') IS NULL ALTER TABLE Productos ADD UsoInterno bit NOT NULL DEFAULT 0;
            IF COL_LENGTH('Productos','AreaServicio') IS NULL ALTER TABLE Productos ADD AreaServicio int NULL;
            IF COL_LENGTH('Productos','DuracionMinutos') IS NULL ALTER TABLE Productos ADD DuracionMinutos int NOT NULL DEFAULT 30;
            IF OBJECT_ID('Mascotas','U') IS NULL
            CREATE TABLE Mascotas (
                Id int IDENTITY PRIMARY KEY, ClienteId int NOT NULL REFERENCES Clientes(Id),
                Nombre nvarchar(100) NOT NULL, Especie nvarchar(40) NOT NULL, Raza nvarchar(100) NULL,
                Sexo nvarchar(20) NOT NULL, FechaNacimiento datetime2 NULL, Peso decimal(8,2) NULL,
                Alergias nvarchar(1000) NULL, Observaciones nvarchar(2000) NULL, Activa bit NOT NULL);
            IF OBJECT_ID('Atenciones','U') IS NULL
            CREATE TABLE Atenciones (
                Id int IDENTITY PRIMARY KEY, MascotaId int NOT NULL REFERENCES Mascotas(Id), Area int NOT NULL,
                Inicio datetime2 NOT NULL, FinPrevisto datetime2 NOT NULL, ResponsableId nvarchar(450) NOT NULL,
                Motivo nvarchar(300) NOT NULL, Estado int NOT NULL, InicioReal datetime2 NULL, FinReal datetime2 NULL,
                Entrega datetime2 NULL, CreadoPor nvarchar(max) NULL, Creada datetime2 NOT NULL,
                Recepcion nvarchar(2000) NULL, Evaluacion nvarchar(4000) NULL, Diagnostico nvarchar(2000) NULL,
                Tratamiento nvarchar(4000) NULL, Indicaciones nvarchar(4000) NULL, Peso decimal(8,2) NULL,
                ProximoControl datetime2 NULL, Bano bit NOT NULL, Corte bit NOT NULL, Unas bit NOT NULL,
                Oidos bit NOT NULL, Perfume bit NOT NULL, VentaId int NULL REFERENCES Ventas(Id),
                OperacionCobro uniqueidentifier NOT NULL);
            IF OBJECT_ID('CargosAtencion','U') IS NULL
            CREATE TABLE CargosAtencion (
                Id int IDENTITY PRIMARY KEY, AtencionId int NOT NULL REFERENCES Atenciones(Id) ON DELETE CASCADE,
                ProductoId int NOT NULL REFERENCES Productos(Id), Cantidad int NOT NULL,
                Descripcion nvarchar(200) NOT NULL, Precio decimal(18,2) NOT NULL);
            IF OBJECT_ID('ConsumosAtencion','U') IS NULL
            CREATE TABLE ConsumosAtencion (
                Id int IDENTITY PRIMARY KEY, AtencionId int NOT NULL REFERENCES Atenciones(Id) ON DELETE CASCADE,
                ProductoId int NOT NULL REFERENCES Productos(Id), Cantidad int NOT NULL, Costo decimal(18,2) NOT NULL,
                OperacionId uniqueidentifier NOT NULL, Fecha datetime2 NOT NULL, Usuario nvarchar(max) NULL);
            IF OBJECT_ID('EventosAtencion','U') IS NULL
            CREATE TABLE EventosAtencion (
                Id int IDENTITY PRIMARY KEY, AtencionId int NOT NULL REFERENCES Atenciones(Id) ON DELETE CASCADE,
                Fecha datetime2 NOT NULL, Usuario nvarchar(450) NOT NULL, Descripcion nvarchar(1000) NOT NULL);
            IF OBJECT_ID('PreventivosMascota','U') IS NULL
            CREATE TABLE PreventivosMascota (
                Id int IDENTITY PRIMARY KEY, MascotaId int NOT NULL REFERENCES Mascotas(Id) ON DELETE CASCADE,
                Tipo nvarchar(30) NOT NULL, Nombre nvarchar(150) NOT NULL, Lote nvarchar(100) NULL,
                Fecha datetime2 NOT NULL, Proxima datetime2 NULL, Observaciones nvarchar(1000) NULL, Usuario nvarchar(max) NULL);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Atenciones_OperacionCobro' AND object_id=OBJECT_ID('Atenciones'))
                CREATE UNIQUE INDEX IX_Atenciones_OperacionCobro ON Atenciones(OperacionCobro);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Atenciones_ResponsableId_Inicio' AND object_id=OBJECT_ID('Atenciones'))
                CREATE INDEX IX_Atenciones_ResponsableId_Inicio ON Atenciones(ResponsableId,Inicio);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ConsumosAtencion_OperacionId' AND object_id=OBJECT_ID('ConsumosAtencion'))
                CREATE UNIQUE INDEX IX_ConsumosAtencion_OperacionId ON ConsumosAtencion(OperacionId);
            """);
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('Productos','Unidad') IS NULL ALTER TABLE Productos ADD Unidad nvarchar(20) NULL;
            IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Productos') AND name='Stock' AND system_type_id<>56)
                ALTER TABLE Productos ALTER COLUMN Stock int NOT NULL;
            IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MovimientosInventario') AND name='Cantidad' AND system_type_id<>56)
                ALTER TABLE MovimientosInventario ALTER COLUMN Cantidad int NOT NULL;
            IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('ConsumosAtencion') AND name='Cantidad' AND system_type_id<>56)
                ALTER TABLE ConsumosAtencion ALTER COLUMN Cantidad int NOT NULL;
            IF OBJECT_ID('ArchivosClinicos','U') IS NULL
                CREATE TABLE ArchivosClinicos (Id int IDENTITY PRIMARY KEY, MascotaId int NOT NULL REFERENCES Mascotas(Id),
                    AtencionId int NULL REFERENCES Atenciones(Id), Nombre nvarchar(200) NOT NULL,
                    TipoContenido nvarchar(50) NOT NULL, Contenido varbinary(max) NULL, EsFoto bit NOT NULL,
                    Fecha datetime2 NOT NULL, Usuario nvarchar(max) NULL);
            """);
        await db.Database.ExecuteSqlRawAsync("UPDATE Productos SET Unidad='unidad' WHERE Unidad IS NULL;");
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('Atenciones','MotivoCancelacion') IS NULL ALTER TABLE Atenciones ADD MotivoCancelacion nvarchar(300) NULL;
            IF COL_LENGTH('Atenciones','CanceladaPor') IS NULL ALTER TABLE Atenciones ADD CanceladaPor nvarchar(max) NULL;
            IF COL_LENGTH('Atenciones','FechaCancelacion') IS NULL ALTER TABLE Atenciones ADD FechaCancelacion datetime2 NULL;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('Clientes','U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM Clientes WHERE Documento = '00000000')
                    INSERT INTO Clientes (Documento, Nombre, Telefono, Correo, Direccion, Estado)
                    VALUES ('00000000', N'Publico General', '000000000', NULL, NULL, 1);

                DECLARE @ClienteSistemaId int = (SELECT TOP 1 Id FROM Clientes WHERE Documento = '00000000' ORDER BY Id);

                UPDATE Clientes
                 SET Nombre = N'Publico General', Telefono = CASE WHEN NULLIF(LTRIM(RTRIM(Telefono)), '') IS NULL THEN '000000000' ELSE Telefono END, Estado = 1
                WHERE Id = @ClienteSistemaId;

                IF OBJECT_ID('Mascotas','U') IS NOT NULL
                    UPDATE Mascotas
                    SET ClienteId = @ClienteSistemaId
                     WHERE ClienteId IN (SELECT Id FROM Clientes WHERE Nombre IN (N'Usuario de Sistema', N'Publico General') AND Documento <> '00000000');

                IF OBJECT_ID('Ventas','U') IS NOT NULL
                    UPDATE Ventas
                    SET ClienteId = @ClienteSistemaId
                     WHERE ClienteId IN (SELECT Id FROM Clientes WHERE Nombre IN (N'Usuario de Sistema', N'Publico General') AND Documento <> '00000000');

                 DELETE FROM Clientes WHERE Nombre IN (N'Usuario de Sistema', N'Publico General') AND Documento <> '00000000';
            END
            """);
        await tx.CommitAsync();
    }
}
