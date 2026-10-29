using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SysPulse.Agent.Android;

/// <summary>
/// Prueba técnica: DNS, TCP, ping y conexión HTTPS con la ruta de ingesta de SysPulse (la misma que usa el agente).
/// No abre conexiones a la base de datos: en Android esas conexiones usan una implementación TLS que puede cerrar
/// la app. Solo lee la configuración; no escribe nada.
/// </summary>
public static class PruebaTecnica
{
    public sealed record Resultado(string Prueba, bool Ok, string Detalle);

    public static async Task<IReadOnlyList<Resultado>> EjecutarAsync()
    {
        var resultados = new List<Resultado>();
        var importacion = await ConfiguracionAndroid.ImportarSiExisteAsync();
        if (importacion is not null) resultados.Add(new("Importación", !importacion.StartsWith("Configuración rechazada", StringComparison.Ordinal), importacion));

        var url = new Uri(ConfiguracionAndroid.IngestaUrl);
        resultados.Add(await MedirAsync($"DNS ({url.Host})", async () =>
        {
            var ips = await Dns.GetHostAddressesAsync(url.Host);
            return $"{ips.Length} IP";
        }));

        resultados.Add(await MedirAsync("TCP 443 (1.1.1.1)", async () =>
        {
            using var cliente = new TcpClient();
            await cliente.ConnectAsync(IPAddress.Parse("1.1.1.1"), 443).WaitAsync(TimeSpan.FromSeconds(10));
            return "conectado";
        }));

        resultados.Add(await PingAsync("8.8.8.8"));

        var clave = await SecureStorage.Default.GetAsync("AGENT_API_KEY");
        if (string.IsNullOrWhiteSpace(clave))
        {
            resultados.Add(new("SysPulse", false, "el equipo no está vinculado (usa un código de alta)"));
        }
        else
        {
            resultados.Add(await MedirAsync("SysPulse (HTTPS)", async () =>
            {
                using var http = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.Add("X-Agent-Key", clave);
                using var r = await http.GetAsync("api/agente/configuracion");
                return r.IsSuccessStatusCode ? "configuración recibida" : throw new HttpRequestException($"respuesta {(int)r.StatusCode}");
            }));
        }

        return resultados;
    }

    private static async Task<Resultado> PingAsync(string destino)
    {
        var nombre = $"Ping ICMP ({destino}, 5 paquetes)";
        try
        {
            using var ping = new Ping();
            var tiempos = new List<long>();
            for (var i = 0; i < 5; i++)
            {
                var r = await ping.SendPingAsync(destino, 3000);
                if (r.Status == IPStatus.Success) tiempos.Add(r.RoundtripTime);
            }
            var perdida = (5 - tiempos.Count) * 20;
            return tiempos.Count == 0
                ? new(nombre, false, "100 % de pérdida")
                : new(nombre, true, $"promedio {tiempos.Average():0} ms, pérdida {perdida} %");
        }
        catch (Exception ex)
        {
            return new(nombre, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<Resultado> MedirAsync(string nombre, Func<Task<string>> accion)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            var detalle = await accion();
            return new(nombre, true, $"{detalle} ({reloj.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            var interna = ex.InnerException is null ? "" : $" → {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            return new(nombre, false, $"{ex.GetType().Name}: {ex.Message}{interna}");
        }
    }
}
