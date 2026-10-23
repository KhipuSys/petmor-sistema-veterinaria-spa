namespace MiniMarket.Web.Models;

public class ArchivoClinico
{
    public int Id { get; set; }
    public int MascotaId { get; set; }
    public Mascota Mascota { get; set; }
    public int? AtencionId { get; set; }
    public Atencion Atencion { get; set; }
    public string Nombre { get; set; }
    public string TipoContenido { get; set; }
    public byte[] Contenido { get; set; }
    public bool EsFoto { get; set; }
    public DateTime Fecha { get; set; } = HoraPeru.Ahora;
    public string Usuario { get; set; }
}
