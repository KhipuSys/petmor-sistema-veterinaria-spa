using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MiniMarket.Web.Infrastructure;

public class FiltroFechas : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)) return;
        var nombres = new[] { "desde", "hasta", "fechaInicio", "fechaFin", "fecha" };
        var fechas = context.ActionArguments.Where(x => nombres.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value as DateTime?);
        if (fechas.Count == 0) return;
        var error = fechas.Keys.Any(k => context.ModelState.TryGetValue(k, out var entry) && entry.Errors.Count > 0)
            ? "Introduce fechas válidas." : null;
        if (fechas.Values.Any(f => f.HasValue && (f.Value.Year < 1900 || f.Value.Year > 2100)))
            error = "Selecciona fechas entre 1900 y 2100.";
        foreach (var (inicio, fin) in new[] { ("desde", "hasta"), ("fechaInicio", "fechaFin") })
            if (fechas.GetValueOrDefault(inicio) is DateTime d && fechas.GetValueOrDefault(fin) is DateTime h && d.Date > h.Date)
                error = "La fecha inicial no puede ser posterior a la fecha final.";
        if (error != null && context.Controller is Controller controller)
        {
            controller.ViewData["ErrorFiltro"] = error;
            context.Result = new ViewResult { ViewName = "~/Views/Shared/FiltroInvalido.cshtml", ViewData = controller.ViewData, StatusCode = 400 };
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
