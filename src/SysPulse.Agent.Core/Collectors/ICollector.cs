namespace SysPulse.Agent.Core.Collectors;

/// <summary>Una lectura puntual de una métrica (ej. "cpu.total" = 37.5 "%").</summary>
public sealed record Lectura(string Metrica, double Valor, string Unidad, DateTimeOffset Ts);

/// <summary>
/// Colector independiente del sistema operativo. Cada host (Windows, Android) aporta sus implementaciones;
/// alertas, buffer y envío no cambian.
/// </summary>
public interface ICollector
{
    string Nombre { get; }

    /// <summary>Falso si el hardware o el sistema no expone la métrica (ej. temperatura en algunos equipos).</summary>
    bool Soportado { get; }

    Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken);
}
