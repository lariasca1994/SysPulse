using System.Text.Json;
using System.Text.Json.Serialization;
using SysPulse.Agent.Core.Alertas;

namespace SysPulse.Agent.Core.Configuracion;

/// <summary>Umbral editable desde la plataforma. <see cref="TipoHost"/> nulo = aplica a todos los tipos.</summary>
public sealed record UmbralCentral(
    string Metrica,
    double Advertencia,
    double Excepcion,
    double Recuperacion,
    int LecturasParaDisparar = 3,
    int LecturasParaRecuperar = 3,
    string? TipoHost = null);

/// <summary>
/// Paquete de actualización publicado en GitHub Releases. La huella SHA-256 viaja por Atlas (no por GitHub): un
/// archivo alterado en la descarga no coincide y se descarta. <see cref="Codigo"/> es el versionCode de Android.
/// </summary>
public sealed record PaqueteActualizacion(string Version, string Url, string Sha256, int? Codigo = null);

/// <summary>
/// Configuración compartida por todos los agentes, guardada en Atlas (colección "configuracion", documento "agentes").
/// Los agentes la releen periódicamente y la aplican en caliente: cambiar un umbral no requiere reinstalar nada.
/// La leen todos los equipos, así que nunca lleva secretos de la plataforma (token de Telegram, cuentas de personas):
/// los avisos los envía solo la API.
/// </summary>
public sealed record ConfiguracionCentral
{
    public const string Coleccion = "configuracion";
    public const string Documento = "agentes";

    /// <summary>URL de la API para los eventos (POST /api/eventos). Vacía: los eventos no se entregan.</summary>
    public string? ApiUrl { get; init; }
    /// <summary>Clave con la que el agente se identifica ante la API (solo permite registrar eventos).</summary>
    public string? AgentApiKey { get; init; }
    public IReadOnlyList<UmbralCentral> Umbrales { get; init; } = [];
    /// <summary>Versión disponible del agente de Windows (actualización automática). Nula: no hay nada que instalar.</summary>
    public PaqueteActualizacion? ActualizacionWindows { get; init; }
    /// <summary>Versión disponible del agente Android (se descarga sola; la persona confirma la instalación).</summary>
    public PaqueteActualizacion? ActualizacionAndroid { get; init; }
    /// <summary>Minutos que una condición debe persistir antes de abrir, escalar o cerrar una alerta. Nulo: el valor del agente (8).</summary>
    public int? PersistenciaMinutos { get; init; }
    public DateTimeOffset Actualizado { get; init; }

    /// <summary>Umbrales efectivos para un tipo de host: los locales por defecto, reemplazados por los centrales de la misma métrica.</summary>
    public IReadOnlyList<Umbral> Combinar(IEnumerable<Umbral> locales, string tipoHost)
    {
        ArgumentNullException.ThrowIfNull(locales);
        var resultado = locales.ToDictionary(u => u.Metrica, StringComparer.Ordinal);
        foreach (var u in Umbrales.Where(u => u.TipoHost is null || string.Equals(u.TipoHost, tipoHost, StringComparison.OrdinalIgnoreCase)))
        {
            resultado[u.Metrica] = new Umbral(u.Metrica, u.Advertencia, u.Excepcion, u.Recuperacion, u.LecturasParaDisparar, u.LecturasParaRecuperar);
        }
        return [.. resultado.Values];
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Copia local de la última configuración recibida (para arrancar sin internet).</summary>
    public void GuardarCopia(string ruta) => File.WriteAllText(ruta, JsonSerializer.Serialize(this, Json));

    public static ConfiguracionCentral? LeerCopia(string ruta)
    {
        try
        {
            return File.Exists(ruta) ? JsonSerializer.Deserialize<ConfiguracionCentral>(File.ReadAllText(ruta), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
