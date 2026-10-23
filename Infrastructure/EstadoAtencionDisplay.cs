using Petmor.Web.Models;

namespace Petmor.Web.Infrastructure;

public static class EstadoAtencionDisplay
{
    public static string Texto(this EstadoAtencion estado) => estado switch
    {
        EstadoAtencion.EnEspera => "En espera",
        EstadoAtencion.EnAtencion => "En atención",
        EstadoAtencion.Entregada => "Completado",
        _ => estado.ToString()
    };
}
