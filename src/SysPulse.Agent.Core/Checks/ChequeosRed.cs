using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Core.Checks;

/// <summary>
/// Chequeos de red cada 5 min (sección 5): latencia TCP (solo handshake), ping (RTT, jitter, pérdida) y DNS.
/// Validado sin privilegios en Windows y en Android.
/// </summary>
public static class ChequeosRed
{
    private const int PaquetesPing = 5;

    public static async Task<IReadOnlyList<Lectura>> EjecutarAsync(
        IEnumerable<string> destinosTcp, IEnumerable<string> destinosPing, IEnumerable<string> nombresDns, CancellationToken ct)
    {
        var tareas = new List<Task<IReadOnlyList<Lectura>>>();
        tareas.AddRange(destinosTcp.Select(d => TcpAsync(d, ct)));
        tareas.AddRange(destinosPing.Select(d => PingAsync(d, ct)));
        tareas.AddRange(nombresDns.Select(n => DnsAsync(n, ct)));

        var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);
        var lecturas = resultados.SelectMany(r => r).ToList();

        // Pérdida global de ping (la que tiene umbral): promedio de los destinos.
        var perdidas = lecturas.Where(l => l.Metrica.StartsWith("red.ping.", StringComparison.Ordinal) && l.Metrica.EndsWith(".perdida_pct", StringComparison.Ordinal)).ToList();
        if (perdidas.Count > 0)
        {
            lecturas.Add(new Lectura("red.ping.perdida_pct", perdidas.Average(l => l.Valor), "%", DateTimeOffset.UtcNow));
        }
        return lecturas;
    }

    private static string Clave(string destino) => destino.Replace('.', '_').Replace(':', '_');

    private static async Task<IReadOnlyList<Lectura>> TcpAsync(string destino, CancellationToken ct)
    {
        var partes = destino.Split(':');
        var ahora = DateTimeOffset.UtcNow;
        var reloj = Stopwatch.StartNew();
        try
        {
            using var cliente = new TcpClient();
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(5));
            await cliente.ConnectAsync(partes[0], int.Parse(partes[1], CultureInfo.InvariantCulture), limite.Token).ConfigureAwait(false);
            return [new($"red.tcp.{Clave(destino)}.ms", reloj.Elapsed.TotalMilliseconds, "ms", ahora), new($"red.tcp.{Clave(destino)}.ok", 1, "bool", ahora)];
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return [new($"red.tcp.{Clave(destino)}.ok", 0, "bool", ahora)];
        }
    }

    private static async Task<IReadOnlyList<Lectura>> PingAsync(string destino, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var tiempos = new List<double>();
        using var ping = new Ping();
        for (var i = 0; i < PaquetesPing && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(destino, 2000).ConfigureAwait(false);
                if (r.Status == IPStatus.Success) tiempos.Add(r.RoundtripTime);
            }
            catch (PingException)
            {
                // Se cuenta como paquete perdido.
            }
        }

        var clave = Clave(destino);
        var lecturas = new List<Lectura> { new($"red.ping.{clave}.perdida_pct", (PaquetesPing - tiempos.Count) * 100.0 / PaquetesPing, "%", ahora) };
        if (tiempos.Count > 0)
        {
            lecturas.Add(new($"red.ping.{clave}.rtt_ms", tiempos.Average(), "ms", ahora));
            var jitter = tiempos.Count > 1 ? tiempos.Zip(tiempos.Skip(1), (a, b) => Math.Abs(b - a)).Average() : 0;
            lecturas.Add(new($"red.ping.{clave}.jitter_ms", jitter, "ms", ahora));
        }
        return lecturas;
    }

    private static async Task<IReadOnlyList<Lectura>> DnsAsync(string nombre, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var reloj = Stopwatch.StartNew();
        try
        {
            await Dns.GetHostAddressesAsync(nombre, ct).ConfigureAwait(false);
            return [new($"red.dns.{Clave(nombre)}.ms", reloj.Elapsed.TotalMilliseconds, "ms", ahora)];
        }
        catch (SocketException)
        {
            return [new($"red.dns.{Clave(nombre)}.ok", 0, "bool", ahora)];
        }
    }
}
