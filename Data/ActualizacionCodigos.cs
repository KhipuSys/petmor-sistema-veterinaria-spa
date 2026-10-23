using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Data;

// El catálogo es global; las existencias y los movimientos siguen perteneciendo a una sede.
public static class ActualizacionCodigos
{
    public sealed class CodigoHistorico
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; }
    }

    // Se ejecuta bajo el bloqueo de operaciones, también ANTES de que Comercial altere la columna.
    public static async Task ValidarHistoricos(ApplicationDbContext db)
    {
        var filas = await db.Database.SqlQueryRaw<CodigoHistorico>(
            "SELECT Id AS ProductoId, CodigoBarras AS Codigo FROM Productos WHERE CodigoBarras IS NOT NULL AND DATALENGTH(CodigoBarras)>0").ToListAsync();
        var existe = await db.Database.SqlQueryRaw<int>(
            "SELECT CASE WHEN OBJECT_ID('CodigosProducto','U') IS NULL THEN 0 ELSE 1 END AS Value").SingleAsync();
        if (existe == 1) filas.AddRange(await db.Database.SqlQueryRaw<CodigoHistorico>("SELECT ProductoId, Codigo FROM CodigosProducto").ToListAsync());
        var propietarios = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var fila in filas)
        {
            string canonico;
            try { canonico = CodigosLecturas.Canonicalizar(fila.Codigo); }
            catch (InvalidOperationException ex) { throw new InvalidOperationException($"Código histórico inválido del producto {fila.ProductoId}. No se modificaron códigos. {ex.Message}", ex); }
            if (!string.Equals(canonico, fila.Codigo, StringComparison.Ordinal))
                throw new InvalidOperationException($"El código histórico del producto {fila.ProductoId} contiene espacios exteriores. Corríjalo explícitamente; no se normaliza al migrar.");
            if (propietarios.TryGetValue(canonico, out var id) && id != fila.ProductoId)
                continue;
            propietarios[canonico] = fila.ProductoId;
        }
    }

    public static async Task Aplicar(ApplicationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActualizacionComercial.BloquearOperaciones(db);
        await ValidarHistoricos(db);
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('MovimientosInventario','OperacionId') IS NULL
                ALTER TABLE MovimientosInventario ADD OperacionId uniqueidentifier NULL;
            IF OBJECT_ID('CodigosProducto', 'U') IS NULL
            BEGIN
                CREATE TABLE CodigosProducto (
                    Id int IDENTITY NOT NULL CONSTRAINT PK_CodigosProducto PRIMARY KEY,
                    Codigo nvarchar(100) NOT NULL,
                    ProductoId int NOT NULL,
                    CONSTRAINT FK_CodigosProducto_Productos_ProductoId FOREIGN KEY(ProductoId) REFERENCES Productos(Id) ON DELETE CASCADE);
                CREATE INDEX IX_CodigosProducto_ProductoId ON CodigosProducto(ProductoId);
            END;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('MovimientosInventario') AND name='IX_MovimientosInventario_OperacionId')
                CREATE UNIQUE INDEX IX_MovimientosInventario_OperacionId ON MovimientosInventario(OperacionId) WHERE OperacionId IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('CodigosProducto') AND name='IX_CodigosProducto_Codigo')
                CREATE UNIQUE INDEX IX_CodigosProducto_Codigo ON CodigosProducto(Codigo);
            INSERT INTO CodigosProducto(Codigo, ProductoId)
                SELECT Codigo, ProductoId
                FROM (
                    SELECT p.CodigoBarras AS Codigo, p.Id AS ProductoId,
                           ROW_NUMBER() OVER (PARTITION BY p.CodigoBarras ORDER BY p.Id) AS rn
                    FROM Productos p
                    WHERE p.CodigoBarras IS NOT NULL AND DATALENGTH(p.CodigoBarras)>0
                      AND NOT EXISTS (SELECT 1 FROM CodigosProducto c WHERE c.Codigo=p.CodigoBarras)
                ) pendientes
                WHERE rn = 1;
            """);
        await tx.CommitAsync();
    }

    public static async Task PurgarVendidos(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('DetalleVentas','LecturasJson') IS NULL OR OBJECT_ID('CodigosProducto', 'U') IS NULL RETURN;

            IF OBJECT_ID('tempdb..#CodigosVendidos') IS NOT NULL DROP TABLE #CodigosVendidos;
            CREATE TABLE #CodigosVendidos(ProductoId int NOT NULL, Codigo nvarchar(100) NOT NULL);

            INSERT INTO #CodigosVendidos(ProductoId, Codigo)
            SELECT DISTINCT d.ProductoId, CONVERT(nvarchar(100), j.[value])
            FROM DetalleVentas d
            INNER JOIN Ventas v ON v.Id = d.VentaId
            CROSS APPLY OPENJSON(d.LecturasJson) j
            WHERE d.LecturasJson IS NOT NULL
              AND ISJSON(d.LecturasJson) = 1
              AND (v.Estado IS NULL OR v.Estado <> 'Anulada')
              AND j.[value] IS NOT NULL;

            DELETE c
            FROM CodigosProducto c
            INNER JOIN #CodigosVendidos v ON v.ProductoId = c.ProductoId AND v.Codigo = c.Codigo;

            UPDATE p
            SET CodigoBarras = ca.Codigo
            FROM Productos p
            INNER JOIN #CodigosVendidos v ON v.ProductoId = p.Id AND v.Codigo = p.CodigoBarras
            OUTER APPLY (
                SELECT TOP(1) c.Codigo
                FROM CodigosProducto c
                WHERE c.ProductoId = p.Id
                ORDER BY c.Id
            ) ca;

            UPDATE p
            SET CodigoBarras = ca.Codigo
            FROM Productos p
            OUTER APPLY (
                SELECT TOP(1) c.Codigo
                FROM CodigosProducto c
                WHERE c.ProductoId = p.Id
                ORDER BY c.Id
            ) ca
            WHERE p.CodigoBarras IS NOT NULL
              AND p.CodigoBarras <> ''
              AND NOT EXISTS (SELECT 1 FROM CodigosProducto c WHERE c.ProductoId = p.Id AND c.Codigo = p.CodigoBarras);
            """);
    }
}
