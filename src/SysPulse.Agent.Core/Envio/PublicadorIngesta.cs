using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Envio;

/// <summary>
/// Envía los datos del agente por HTTPS a la ruta de ingesta (funciones de Vercel), que los guarda en Atlas con el
/// mismo formato que <see cref="PublicadorAtlas"/>. En Android, HttpClient usa la pila de red de Java: evita la
/// implementación TLS nativa de .NET (SslStream), que aborta el proceso en algunos reintentos de conexión.
/// La API de Render no interviene, así que puede seguir durmiendo.
/// </summary>
public sealed class PublicadorIngesta : IPublicador, IDisposable
{
    private readonly HttpClient _http;

    public PublicadorIngesta(string urlBase, string claveAgente, HttpMessageHandler? manejador = null)
    {
        _http = manejador is null ? new HttpClient() : new HttpClient(manejador);
        _http.BaseAddress = new Uri(urlBase.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Agent-Key", claveAgente);
    }

    /// <summary>Los índices los mantienen la API y los agentes de Windows.</summary>
    public Task AsegurarIndicesAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task RegistrarHostAsync(string host, string tipo, string version, string? modelo, CancellationToken ct)
    {
        using var r = await _http.PostAsJsonAsync("api/agente/host", new { host, tipo, version, modelo }, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
    }

    public async Task PublicarAgregadosAsync(IEnumerable<string> jsonAgregados, CancellationToken ct)
    {
        var docs = jsonAgregados.Select(j => JsonSerializer.Deserialize<DocumentoAgregado>(j)!)
            .Select(a => new
            {
                host = a.Host,
                tipo_host = a.TipoHost,
                ts = a.Ventana.UtcDateTime,
                metricas = a.Metricas.ToDictionary(
                    m => PublicadorAtlas.ClaveCampo(m.Key),
                    m => new { min = m.Value.Min, avg = m.Value.Avg, max = m.Value.Max, p95 = m.Value.P95, n = m.Value.Muestras, u = m.Value.Unidad }),
            })
            .ToList();
        if (docs.Count == 0) return;
        using var r = await _http.PostAsJsonAsync("api/agente/metricas", docs, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
    }

    public async Task ActualizarEstadoAsync(string host, string tipo, IEnumerable<Lectura> lecturas, CancellationToken ct)
    {
        var valores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var l in lecturas)
        {
            if (double.IsFinite(l.Valor)) valores[PublicadorAtlas.ClaveCampo(l.Metrica)] = l.Valor;
        }
        using var r = await _http.PutAsJsonAsync("api/agente/estado", new { host, tipo, lecturas = valores }, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
    }

    /// <summary>Solo el agente de Windows vigila el SLA; Android no lo necesita.</summary>
    public Task<IReadOnlyList<(TiemposIncidente Tiempos, IReadOnlyList<string> Avisos)>> LeerIncidentesAbiertosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(TiemposIncidente, IReadOnlyList<string>)>>([]);

    /// <summary>La ruta devuelve el documento de configuración en JSON extendido de MongoDB; se interpreta igual que en Atlas.</summary>
    public async Task<Configuracion.ConfiguracionCentral?> LeerConfiguracionAsync(CancellationToken ct)
    {
        using var r = await _http.GetAsync("api/agente/configuracion", ct).ConfigureAwait(false);
        if (r.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return PublicadorAtlas.DesdeBson(BsonDocument.Parse(json));
    }

    public void Dispose() => _http.Dispose();
}
