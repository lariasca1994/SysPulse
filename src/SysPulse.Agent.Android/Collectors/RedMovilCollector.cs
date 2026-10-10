using global::Android.Content;
using global::Android.Net;
using SysPulse.Agent.Core.Collectors;
using AndroidApp = global::Android.App.Application;

namespace SysPulse.Agent.Android.Collectors;

/// <summary>
/// Red del celular: tipo de conexión (Wi-Fi / datos móviles), señal, ancho de banda estimado y tráfico.
/// La señal sale de NetworkCapabilities.SignalStrength (Android 10+), sin permisos de ubicación.
/// </summary>
public sealed class RedMovilCollector : ICollector
{
    private const double Mb = 1024d * 1024;

    private (long Rx, long Tx, DateTimeOffset Ts)? _anterior;

    public string Nombre => "red";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>();

        if (AndroidApp.Context.GetSystemService(Context.ConnectivityService) is ConnectivityManager conectividad)
        {
            var activa = conectividad.ActiveNetwork;
            var capacidades = activa is null ? null : conectividad.GetNetworkCapabilities(activa);
            if (capacidades is null)
            {
                lecturas.Add(new("red.conectada", 0, "bool", ahora));
            }
            else
            {
                lecturas.Add(new("red.conectada", 1, "bool", ahora));
                // 1 = Wi-Fi, 2 = datos móviles, 3 = Ethernet, 0 = otra.
                var tipo = capacidades.HasTransport(TransportType.Wifi) ? 1
                    : capacidades.HasTransport(TransportType.Cellular) ? 2
                    : capacidades.HasTransport(TransportType.Ethernet) ? 3 : 0;
                lecturas.Add(new("red.tipo", tipo, "tipo", ahora));
                lecturas.Add(new("red.bajada_estimada_mbps", capacidades.LinkDownstreamBandwidthKbps / 1000.0, "Mbps", ahora));
                lecturas.Add(new("red.subida_estimada_mbps", capacidades.LinkUpstreamBandwidthKbps / 1000.0, "Mbps", ahora));
                if (OperatingSystem.IsAndroidVersionAtLeast(29) && capacidades.SignalStrength != int.MinValue)
                {
                    lecturas.Add(new("red.senal_dbm", capacidades.SignalStrength, "dBm", ahora));
                }
            }
        }

        var rx = TrafficStats.TotalRxBytes;
        var tx = TrafficStats.TotalTxBytes;
        if (rx >= 0 && tx >= 0)
        {
            if (_anterior is { } a && rx >= a.Rx && tx >= a.Tx)
            {
                var segundos = (ahora - a.Ts).TotalSeconds;
                if (segundos > 0)
                {
                    lecturas.Add(new("red.entrada_mbs", (rx - a.Rx) / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.salida_mbs", (tx - a.Tx) / Mb / segundos, "MB/s", ahora));
                }
            }
            _anterior = (rx, tx, ahora);
        }

        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
