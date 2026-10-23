using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Petmor.Web.Models;

public class ReservaProducto
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; }
    public int Cantidad { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Precio { get; set; }
    [Required] public string Descripcion { get; set; }
    [MaxLength(20)] public string Estado { get; set; } = "Pendiente";
    [MaxLength(300)] public string Observaciones { get; set; }
    public string Usuario { get; set; }
    public DateTime Fecha { get; set; } = HoraPeru.Ahora;
    public Guid OperacionId { get; set; }
    public Guid OperacionCobro { get; set; } = Guid.NewGuid();
    public int? VentaId { get; set; }
    public Venta Venta { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string CerradaPor { get; set; }
    [NotMapped] public decimal Total => Precio * Cantidad;
}

public class ReservaForm
{
    public List<string> Lecturas { get; set; }
    public Guid OperacionId { get; set; } = Guid.NewGuid();
    [Range(1, int.MaxValue)] public int ClienteId { get; set; }
    [Range(1, int.MaxValue)] public int ProductoId { get; set; }
    [Range(1, 100000)] public int Cantidad { get; set; } = 1;
    [MaxLength(300)] public string Observaciones { get; set; }
}
