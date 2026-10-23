using System.ComponentModel.DataAnnotations;

namespace Petmor.Web.Models;

public class CodigoProducto
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Codigo { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; }
}
