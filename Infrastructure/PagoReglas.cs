namespace MiniMarket.Web.Infrastructure;

public static class PagoReglas
{
    public const decimal RecargoTarjetaPct = 0.05m;

    public static decimal CalcularRecargo(string metodoPago, decimal baseCobro) =>
        metodoPago == "Tarjeta" ? decimal.Round(baseCobro * RecargoTarjetaPct, 2, MidpointRounding.AwayFromZero) : 0m;
}
