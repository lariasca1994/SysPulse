using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Envio;

/// <summary>
/// Destino de los datos del agente: Atlas directo (Windows) o la ruta HTTPS de ingesta (Android, que así no usa la
/// implementación TLS nativa de .NET para Android, que puede cerrar la app al reconectar).
/// </summary>
public interface IPublicador
{
    Task AsegurarIndicesAsync(CancellationToken ct);
    Task RegistrarHostAsync(string host, string tipo, string version, string? modelo, CancellationToken ct);
    Task PublicarAgregadosAsync(IEnumerable<string> jsonAgregados, CancellationToken ct);
    Task ActualizarEstadoAsync(string host, string tipo, IEnumerable<Lectura> lecturas, CancellationToken ct);
    Task<IReadOnlyList<(TiemposIncidente Tiempos, IReadOnlyList<string> Avisos)>> LeerIncidentesAbiertosAsync(CancellationToken ct);
    Task<Configuracion.ConfiguracionCentral?> LeerConfiguracionAsync(CancellationToken ct);
}
