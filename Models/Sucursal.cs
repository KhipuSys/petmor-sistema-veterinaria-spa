using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MiniMarket.Web.Models;

public class Sucursal
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Nombre { get; set; }
    public bool Activa { get; set; } = true;
}

public class UsuarioSede
{
    [Key] public string UsuarioId { get; set; }
    public IdentityUser Usuario { get; set; }
    public int SucursalId { get; set; }
    public Sucursal Sucursal { get; set; }
}

public class ExistenciaSucursal
{
    public int ProductoId { get; set; }
    public Producto Producto { get; set; }
    public int SucursalId { get; set; }
    public Sucursal Sucursal { get; set; }
    public int Cantidad { get; set; }
}
