using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;
using MiniMarket.Web.Models;

namespace MiniMarket.Web.Services;

public static class CodigosLecturas
{
    public const int MaxLecturas = 2000;
    public const int MaxCodigos = 200;
    private const int TamanoLote = 100;

    // ASCII imprimible: no eliminar tabuladores, NBSP, BOM, caracteres invisibles o Unicode.
    // La única limpieza permitida en entradas nuevas son espacios ASCII exteriores.
    public static string Canonicalizar(string codigo)
    {
        if (codigo == null || codigo.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException("El código debe contener únicamente caracteres ASCII imprimibles, sin controles ni Unicode.");
        codigo = codigo.Trim(' ');
        if (codigo.Length is < 1 or > 100)
            throw new InvalidOperationException("El código debe tener entre 1 y 100 caracteres.");
        return codigo;
    }

    public static List<string> Normalizar(IEnumerable<string> lecturas)
    {
        var lista = (lecturas ?? []).Take(MaxLecturas + 1).ToList();
        if (lista.Count > MaxLecturas) throw new InvalidOperationException($"Máximo {MaxLecturas} lecturas por detalle.");
        var normalizadas = lista.Select(Canonicalizar).ToList();
        var repetido = normalizadas.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (repetido != null) throw new InvalidOperationException($"El código {repetido} ya fue escaneado en esta operación. Cada código cuenta una sola unidad.");
        return normalizadas;
    }

    public static void ValidarUnicasEnOperacion(IEnumerable<IEnumerable<string>> lecturasPorDetalle)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var codigo in lecturasPorDetalle.Where(x => x != null).SelectMany(x => x))
            if (!vistos.Add(codigo)) throw new InvalidOperationException($"El código {codigo} ya fue escaneado en esta operación. Cada código cuenta una sola unidad.");
    }

    public static async Task PrepararAlta(ApplicationDbContext db, Producto producto, IEnumerable<string> lecturas)
    {
        var lista = Normalizar(lecturas);
        producto.Codigos.Clear();
        await Asociar(db, producto, lista);
        producto.CodigoBarras = lista.FirstOrDefault();
        producto.Stock = lista.Count;
    }

    // Asociación aditiva: cambiar el código principal nunca libera un código histórico.
    public static async Task Asociar(ApplicationDbContext db, Producto producto, IEnumerable<string> codigos)
    {
        var solicitados = Normalizar(codigos).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (solicitados.Count > MaxCodigos) throw new InvalidOperationException($"Máximo {MaxCodigos} códigos por producto.");
        var propios = producto.Id == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
            (await db.CodigosProducto.Where(c => c.ProductoId == producto.Id).Select(c => c.Codigo).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        propios.UnionWith(producto.Codigos.Select(c => c.Codigo));
        if (propios.Union(solicitados, StringComparer.OrdinalIgnoreCase).Count() > MaxCodigos)
            throw new InvalidOperationException($"Máximo {MaxCodigos} códigos por producto.");
        // Consultas acotadas por lote; validar todo antes de modificar la colección rastreada.
        foreach (var lote in solicitados.Chunk(TamanoLote))
        {
            var propietarios = await db.CodigosProducto.Where(c => lote.Contains(c.Codigo)).Select(c => new { c.Codigo, c.ProductoId }).ToListAsync();
            var principales = await db.Productos.Where(p => lote.Contains(p.CodigoBarras)).Select(p => new { Codigo = p.CodigoBarras, ProductoId = p.Id }).ToListAsync();
            var conflicto = propietarios.Concat(principales).FirstOrDefault(c => c.ProductoId != producto.Id);
            if (conflicto != null) throw new InvalidOperationException($"El código {conflicto.Codigo} ya está asignado a otro producto.");
        }
        foreach (var codigo in solicitados)
            if (propios.Add(codigo)) producto.Codigos.Add(new CodigoProducto { Codigo = codigo });
    }

    public static async Task<Producto> ResolverProducto(ApplicationDbContext db, string codigo)
    {
        codigo = Canonicalizar(codigo);
        var ids = await ResolverIds(db, [codigo]);
        if (!ids.TryGetValue(codigo, out var productoId)) return null;
        return await db.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId);
    }

    public static async Task<int> Cantidad(ApplicationDbContext db, int productoId, IEnumerable<string> lecturas)
    {
        var lista = Normalizar(lecturas);
        if (lista.Count == 0) throw new InvalidOperationException("Registre al menos una lectura.");
        if (!await db.Productos.AnyAsync(p => p.Id == productoId && !p.EsServicio)) throw new InvalidOperationException("Producto inventariable inexistente.");
        var resueltos = await ResolverIds(db, lista.Distinct(StringComparer.OrdinalIgnoreCase));
        if (lista.Any(c => !resueltos.TryGetValue(c, out var id) || id != productoId))
            throw new InvalidOperationException("Hay códigos que no pertenecen al producto seleccionado.");
        return lista.Count;
    }

    public static async Task<Dictionary<string, int>> ResolverIds(ApplicationDbContext db, IEnumerable<string> codigos)
    {
        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in codigos.ToArray().Chunk(TamanoLote))
        {
            var principales = await db.Productos.AsNoTracking().Where(p => lote.Contains(p.CodigoBarras))
                .Select(p => new { Codigo = p.CodigoBarras, ProductoId = p.Id }).ToListAsync();
            var asociados = await db.CodigosProducto.AsNoTracking().Where(c => lote.Contains(c.Codigo))
                .Select(c => new { c.Codigo, c.ProductoId }).ToListAsync();
            foreach (var grupo in principales.Concat(asociados).Where(x => x.Codigo != null).GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase))
            {
                var ids = grupo.Select(x => x.ProductoId).Distinct().ToList();
                if (ids.Count > 1)
                    throw new InvalidOperationException($"El código {grupo.Key} está asignado a más de un producto. Corrija la asociación antes de usarlo.");
                resultado[grupo.Key] = ids[0];
            }
        }
        return resultado;
    }
}
