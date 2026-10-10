using System.Net.Http.Json;
using SysPulse.Contracts;

namespace SysPulse.Agent.Core.Envio;

/// <summary>
/// Llamadas del agente a la API: solo eventos de alerta y cruces de SLA (regla de oro: nunca métricas).
/// Cada llamada lleva su propio tiempo límite: 90 s la primera vez (Render gratuito tarda en despertar), menos en reintentos.
/// </summary>
public sealed class ClienteApi
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<bool> EnviarEventoAsync(string url, string clave, EventoDto evento, TimeSpan limite, CancellationToken ct) =>
        await PostAsync($"{url.TrimEnd('/')}/api/eventos", clave, evento, limite, ct).ConfigureAwait(false);

    public static async Task<bool> AvisarSlaAsync(string url, string clave, TimeSpan limite, CancellationToken ct) =>
        await PostAsync($"{url.TrimEnd('/')}/api/sla/umbral", clave, new { }, limite, ct).ConfigureAwait(false);

    private static async Task<bool> PostAsync<T>(string url, string clave, T cuerpo, TimeSpan limite, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limite);
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, new Uri(url)) { Content = JsonContent.Create(cuerpo) };
        solicitud.Headers.Add("X-Agent-Key", clave);
        try
        {
            using var respuesta = await Http.SendAsync(solicitud, cts.Token).ConfigureAwait(false);
            return respuesta.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
