namespace MiniMarket.Web.Models;

public class TicketHistorico
{
    public Configuracion Empresa { get; set; }
    public string Cliente { get; set; }
    public string Documento { get; set; }
    public string Cajero { get; set; }
    public string Sede { get; set; }
    public decimal BaseImponible { get; set; }
    public decimal Igv { get; set; }
}
