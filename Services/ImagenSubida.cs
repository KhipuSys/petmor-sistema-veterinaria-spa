namespace MiniMarket.Web.Services;

public static class ImagenSubida
{
    public static async Task<string> Extension(IFormFile archivo)
    {
        if (archivo == null) return null;
        if (archivo.Length is < 12 or > 5_000_000) throw new InvalidOperationException("Seleccione una imagen de hasta 5 MB.");
        await using var stream = archivo.OpenReadStream();
        var bytes = new byte[12];
        await stream.ReadExactlyAsync(bytes);
        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ".png";
        if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        if (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a") return ".gif";
        if (System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        throw new InvalidOperationException("El contenido debe corresponder a una imagen PNG, JPEG, GIF o WebP.");
    }

    public static void BorrarAnterior(string webRoot, string url, string carpeta, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/" + carpeta + "/", StringComparison.Ordinal)) return;
        var raiz = Path.GetFullPath(Path.Combine(webRoot, carpeta)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(webRoot, url.TrimStart('/')));
        if (!path.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException ex) { logger.LogWarning(ex, "No se pudo limpiar imagen anterior"); }
        catch (UnauthorizedAccessException ex) { logger.LogWarning(ex, "No se pudo limpiar imagen anterior"); }
    }
}
