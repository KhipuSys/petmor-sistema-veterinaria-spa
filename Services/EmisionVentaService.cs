using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;

namespace MiniMarket.Web.Services;

public class EmisionVentaService(ApplicationDbContext db, FactilizaService factiliza, ILogger<EmisionVentaService> logger)
{
    // La transacción de cobro ya terminó. Un fallo externo nunca repite la venta ni el stock.
    public async Task Intentar(int ventaId)
    {
        try
        {
            var claim = await db.Ventas.Where(v => v.Id == ventaId && v.Estado == "Activa" && v.EstadoEmision == "Pendiente")
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EstadoEmision, "Resultado pendiente de conciliar")
                    .SetProperty(v => v.FechaIntentoEmision, MiniMarket.Web.Infrastructure.HoraPeru.Ahora));
            if (claim != 1) return;
            var venta = await db.Ventas.AsNoTracking().Include(v => v.Cliente).SingleAsync(v => v.Id == ventaId);
            var historico = string.IsNullOrWhiteSpace(venta.TicketHistoricoJson) ? null : System.Text.Json.JsonSerializer.Deserialize<MiniMarket.Web.Models.TicketHistorico>(venta.TicketHistoricoJson);
            var tasa = historico?.Empresa?.IgvPorcentaje ?? (await db.Configuraciones.FirstOrDefaultAsync())?.IgvPorcentaje ?? 18;
            var igv = historico?.Igv ?? decimal.Round(venta.Total - venta.Total / (1 + tasa / 100m), 2);
            var resultado = await factiliza.EmitirBoletaDetalladaAsync(historico?.Documento ?? venta.Cliente?.Documento ?? "00000000", historico?.Cliente ?? venta.Cliente?.Nombre ?? "Clientes Varios", venta.Total, igv);
            await db.Ventas.Where(v => v.Id == ventaId && v.EstadoEmision == "Resultado pendiente de conciliar")
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EstadoEmision, resultado.Enviada ? "Enviada a API; verificar comprobante" : "Requiere revisión")
                    .SetProperty(v => v.RespuestaEmision, resultado.Respuesta));
        }
        catch (Exception ex) { logger.LogError(ex, "Revisar emisión de la venta {VentaId}", ventaId); }
    }
}
