using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Petmor.Web.Models;

public enum AreaAtencion { Veterinaria = 1, Spa = 2 }
public enum EstadoAtencion { Programada = 1, Confirmada = 2, EnEspera = 3, EnAtencion = 4, Lista = 5, Entregada = 6, Cancelada = 7 }

public class Mascota
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; }
    [Required, MaxLength(100)] public string Nombre { get; set; }
    [Required, MaxLength(40)] public string Especie { get; set; } = "Canino";
    [MaxLength(100)] public string Raza { get; set; }
    [Required, MaxLength(20)] public string Sexo { get; set; } = "Macho";
    public DateTime? FechaNacimiento { get; set; }
    [Column(TypeName = "decimal(8,2)"), Range(0.01, 2000)] public decimal? Peso { get; set; }
    [MaxLength(1000)] public string Alergias { get; set; }
    [MaxLength(2000)] public string Observaciones { get; set; }
    public bool Activa { get; set; } = true;
}

public class Atencion
{
    public int Id { get; set; }
    public int MascotaId { get; set; }
    public Mascota Mascota { get; set; }
    public AreaAtencion Area { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime FinPrevisto { get; set; }
    [Required, MaxLength(450)] public string ResponsableId { get; set; }
    [Required, MaxLength(300)] public string Motivo { get; set; }
    public EstadoAtencion Estado { get; set; } = EstadoAtencion.Programada;
    [MaxLength(300)] public string MotivoCancelacion { get; set; }
    public string CanceladaPor { get; set; }
    public DateTime? FechaCancelacion { get; set; }
    [NotMapped] public EventoAtencion CancelacionHistorica => Eventos
        .Where(e => e.Descripcion?.StartsWith("Estado: Cancelada.", StringComparison.Ordinal) == true)
        .OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id).FirstOrDefault();
    [NotMapped] public string MotivoCancelacionVisible => MotivoCancelacion ??
        (CancelacionHistorica?.Descripcion["Estado: Cancelada.".Length..].Trim() is { Length: > 0 } texto ? texto : "Sin motivo registrado.");
    [NotMapped] public string AuditoriaCancelacion => FechaCancelacion.HasValue
        ? $"{FechaCancelacion:dd/MM/yyyy HH:mm} · {CanceladaPor}"
        : CancelacionHistorica is { } e ? $"{e.Fecha:dd/MM/yyyy HH:mm} · {e.Usuario}" : "";
    public DateTime? InicioReal { get; set; }
    public DateTime? FinReal { get; set; }
    public DateTime? Entrega { get; set; }
    public string CreadoPor { get; set; }
    public DateTime Creada { get; set; } = HoraPeru.Ahora;
    [MaxLength(2000)] public string Recepcion { get; set; }
    [MaxLength(4000)] public string Evaluacion { get; set; }
    [MaxLength(2000)] public string Diagnostico { get; set; }
    [MaxLength(4000)] public string Tratamiento { get; set; }
    [MaxLength(4000)] public string Indicaciones { get; set; }
    [Column(TypeName = "decimal(8,2)")] public decimal? Peso { get; set; }
    public DateTime? ProximoControl { get; set; }
    public bool Bano { get; set; }
    public bool Corte { get; set; }
    public bool Unas { get; set; }
    public bool Oidos { get; set; }
    public bool Perfume { get; set; }
    public int? VentaId { get; set; }
    public Venta Venta { get; set; }
    public Guid OperacionCobro { get; set; } = Guid.NewGuid();
    public List<CargoAtencion> Cargos { get; set; } = new();
    public List<ConsumoAtencion> Consumos { get; set; } = new();
    public List<EventoAtencion> Eventos { get; set; } = new();
}

public class CargoAtencion
{
    public int Id { get; set; }
    public int AtencionId { get; set; }
    public Atencion Atencion { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; }
    public int Cantidad { get; set; }
    [Required, MaxLength(200)] public string Descripcion { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Precio { get; set; }
}

public class ConsumoAtencion
{
    public int Id { get; set; }
    public int AtencionId { get; set; }
    public Atencion Atencion { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; }
    public int Cantidad { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Costo { get; set; }
    public Guid OperacionId { get; set; }
    public DateTime Fecha { get; set; } = HoraPeru.Ahora;
    public string Usuario { get; set; }
}

public class EventoAtencion
{
    public int Id { get; set; }
    public int AtencionId { get; set; }
    public Atencion Atencion { get; set; }
    public DateTime Fecha { get; set; } = HoraPeru.Ahora;
    [Required, MaxLength(450)] public string Usuario { get; set; }
    [Required, MaxLength(1000)] public string Descripcion { get; set; }
}

public class PreventivoMascota
{
    public int Id { get; set; }
    public int MascotaId { get; set; }
    public Mascota Mascota { get; set; }
    [Required, MaxLength(30)] public string Tipo { get; set; }
    [Required, MaxLength(150)] public string Nombre { get; set; }
    [MaxLength(100)] public string Lote { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? Proxima { get; set; }
    [MaxLength(1000)] public string Observaciones { get; set; }
    public string Usuario { get; set; }
}
