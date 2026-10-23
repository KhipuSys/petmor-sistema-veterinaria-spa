using Petmor.Web.Models;

namespace Petmor.Web.Services;

// Scoped: una sede fija por solicitud; nunca se comparte entre usuarios.
public class SedeActual
{
    public int Id { get; set; }
    public string Nombre { get; set; }
    public bool PuedeCambiar { get; set; }
    public IReadOnlyList<Sucursal> Disponibles { get; set; } = Array.Empty<Sucursal>();
}
