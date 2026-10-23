using Microsoft.AspNetCore.Antiforgery;
using MiniMarket.Web.Services;

namespace MiniMarket.Web.Infrastructure;

// Impide procesar formularios/carritos abiertos en otra pestaña antes del cambio de sede.
public class SedeAntiforgeryData : IAntiforgeryAdditionalDataProvider
{
    public string GetAdditionalData(HttpContext context) => context.RequestServices.GetRequiredService<SedeActual>().Id.ToString();
    public bool ValidateAdditionalData(HttpContext context, string additionalData) => additionalData == GetAdditionalData(context);
}
