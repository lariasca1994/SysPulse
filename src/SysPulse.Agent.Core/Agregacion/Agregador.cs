using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Core.Agregacion;

/// <summary>Resumen de una métrica en una ventana (por defecto 5 min): lo que se guarda en Atlas.</summary>
public sealed record Resumen(double Min, double Avg, double Max, double P95, int Muestras, string Unidad);

public static class Agregador
{
    /// <summary>Agrupa lecturas por métrica y calcula min/avg/max/p95.</summary>
    public static IReadOnlyDictionary<string, Resumen> Resumir(IEnumerable<Lectura> lecturas)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        return lecturas
            .GroupBy(l => l.Metrica, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var valores = g.Select(l => l.Valor).Order().ToArray();
                    return new Resumen(valores[0], valores.Average(), valores[^1], Percentil(valores, 95), valores.Length, g.First().Unidad);
                },
                StringComparer.Ordinal);
    }

    /// <summary>Percentil por interpolación lineal sobre valores ya ordenados.</summary>
    public static double Percentil(IReadOnlyList<double> ordenados, double percentil)
    {
        ArgumentNullException.ThrowIfNull(ordenados);
        if (ordenados.Count == 0) throw new ArgumentException("Sin valores.", nameof(ordenados));
        if (ordenados.Count == 1) return ordenados[0];

        var posicion = percentil / 100 * (ordenados.Count - 1);
        var inferior = (int)Math.Floor(posicion);
        var superior = (int)Math.Ceiling(posicion);
        var fraccion = posicion - inferior;
        return ordenados[inferior] + (ordenados[superior] - ordenados[inferior]) * fraccion;
    }

    /// <summary>Inicio de la ventana a la que pertenece un instante (ej. 10:07 → 10:05 con ventana de 5 min).</summary>
    public static DateTimeOffset InicioVentana(DateTimeOffset ts, TimeSpan ventana)
    {
        var ticks = ts.UtcTicks - ts.UtcTicks % ventana.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
