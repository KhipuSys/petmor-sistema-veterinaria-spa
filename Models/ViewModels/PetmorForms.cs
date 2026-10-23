using System.ComponentModel.DataAnnotations;

namespace Petmor.Web.Models.ViewModels;

public class AgendaForm
{
    [Range(1, int.MaxValue)] public int MascotaId { get; set; }
    [Required] public int? ServicioId { get; set; }
    [EnumDataType(typeof(AreaAtencion))] public AreaAtencion Area { get; set; } = AreaAtencion.Veterinaria;
    public DateTime Inicio { get; set; } = HoraPeru.Ahora;
    [Range(10, 480)] public int Duracion { get; set; } = 30;
    [Required] public string ResponsableId { get; set; }
    [StringLength(300)] public string Motivo { get; set; }
    [StringLength(2000)] public string Recepcion { get; set; }
    public bool SinCita { get; set; }
}

public class FichaForm
{
    [StringLength(4000)] public string Evaluacion { get; set; }
    [StringLength(2000)] public string Diagnostico { get; set; }
    [StringLength(4000)] public string Tratamiento { get; set; }
    [StringLength(4000)] public string Indicaciones { get; set; }
    [Range(0.01, 2000)] public decimal? Peso { get; set; }
    public DateTime? ProximoControl { get; set; }
    public bool Bano { get; set; }
    public bool Corte { get; set; }
    public bool Unas { get; set; }
    public bool Oidos { get; set; }
    public bool Perfume { get; set; }
}

public class ServicioForm
{
    public int Id { get; set; }
    [Required, StringLength(150)] public string Nombre { get; set; }
    [StringLength(2000)] public string Descripcion { get; set; }
    [Range(0.01, 100000)] public decimal Precio { get; set; }
    [EnumDataType(typeof(AreaAtencion))] public AreaAtencion Area { get; set; } = AreaAtencion.Veterinaria;
    [Range(10, 480)] public int Duracion { get; set; } = 30;
    public bool Estado { get; set; } = true;
}
