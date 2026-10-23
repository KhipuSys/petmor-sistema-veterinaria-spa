using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiniMarket.Web.Infrastructure;

// Texto localizado para lectura, clave invariable para ordenar/exportar tablas.
[HtmlTargetElement("td")]
public class FechaTablaTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (output.Attributes.ContainsName("data-order")) return;
        var contenido = await output.GetChildContentAsync();
        var texto = System.Net.WebUtility.HtmlDecode(Regex.Replace(contenido.GetContent(), "<[^>]+>", " ")).Trim();
        texto = Regex.Replace(texto, @"\s+", " ");
        if (DateTime.TryParseExact(texto, new[] { "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            output.Attributes.SetAttribute("data-order", fecha.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
    }
}
