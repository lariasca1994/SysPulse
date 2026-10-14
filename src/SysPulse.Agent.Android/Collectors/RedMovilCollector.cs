using global::Android.Content;
using global::Android.Net;
using SysPulse.Agent.Core.Collectors;
using AndroidApp = global::Android.App.Application;

namespace SysPulse.Agent.Android.Collectors;

/// <summary>
/// Red del celular: tipo de conexión (Wi-Fi / datos móviles / Ethernet), señal,
/// ancho de banda estimado y consumo de red desglosado por datos móviles, Wi-Fi y UTP.
/// La señal sale de NetworkCapabilities.SignalStrength (Android 10+), sin permisos de ubicación.
/// </summary>
public sealed class RedMovilCollector : ICollector
{
    private const double Mb = 1024d * 1024;

    private (long TotalRx, long TotalTx, long MobileRx, long MobileTx, long WifiRx, long WifiTx, long UtpRx, long UtpTx, DateTimeOffset Ts)? _anterior;

    public string Nombre => "red";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>();
        int tipoTransporte = 0; // 0 = otra, 1 = Wi-Fi, 2 = Móvil, 3 = Ethernet

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
                tipoTransporte = capacidades.HasTransport(TransportType.Wifi) ? 1
                    : capacidades.HasTransport(TransportType.Cellular) ? 2
                    : capacidades.HasTransport(TransportType.Ethernet) ? 3 : 0;
                lecturas.Add(new("red.tipo", tipoTransporte, "tipo", ahora));
                lecturas.Add(new("red.bajada_estimada_mbps", capacidades.LinkDownstreamBandwidthKbps / 1000.0, "Mbps", ahora));
                lecturas.Add(new("red.subida_estimada_mbps", capacidades.LinkUpstreamBandwidthKbps / 1000.0, "Mbps", ahora));
                if (OperatingSystem.IsAndroidVersionAtLeast(29) && capacidades.SignalStrength != int.MinValue)
                {
                    lecturas.Add(new("red.senal_dbm", capacidades.SignalStrength, "dBm", ahora));
                }
            }
        }

        var totalRx = TrafficStats.TotalRxBytes;
        var totalTx = TrafficStats.TotalTxBytes;
        var mobileRx = TrafficStats.MobileRxBytes;
        var mobileTx = TrafficStats.MobileTxBytes;

        if (totalRx >= 0 && totalTx >= 0)
        {
            if (mobileRx < 0) mobileRx = 0;
            if (mobileTx < 0) mobileTx = 0;

            long acumWifiRx = 0, acumWifiTx = 0;
            long acumUtpRx = 0, acumUtpTx = 0;

            if (_anterior is { } a && totalRx >= a.TotalRx && totalTx >= a.TotalTx)
            {
                var segundos = (ahora - a.Ts).TotalSeconds;
                if (segundos > 0)
                {
                    var deltaTotalRx = totalRx - a.TotalRx;
                    var deltaTotalTx = totalTx - a.TotalTx;
                    var deltaMobileRx = mobileRx >= a.MobileRx ? mobileRx - a.MobileRx : 0;
                    var deltaMobileTx = mobileTx >= a.MobileTx ? mobileTx - a.MobileTx : 0;
                    var deltaNoMobileRx = Math.Max(0, deltaTotalRx - deltaMobileRx);
                    var deltaNoMobileTx = Math.Max(0, deltaTotalTx - deltaMobileTx);

                    long deltaWifiRx = 0, deltaWifiTx = 0;
                    long deltaUtpRx = 0, deltaUtpTx = 0;

                    if (tipoTransporte == 3) // Ethernet
                    {
                        deltaUtpRx = deltaNoMobileRx;
                        deltaUtpTx = deltaNoMobileTx;
                    }
                    else
                    {
                        deltaWifiRx = deltaNoMobileRx;
                        deltaWifiTx = deltaNoMobileTx;
                    }

                    acumWifiRx = a.WifiRx + deltaWifiRx;
                    acumWifiTx = a.WifiTx + deltaWifiTx;
                    acumUtpRx = a.UtpRx + deltaUtpRx;
                    acumUtpTx = a.UtpTx + deltaUtpTx;

                    // MB/s
                    lecturas.Add(new("red.entrada_mbs", deltaTotalRx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.salida_mbs", deltaTotalTx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.wifi_bajada_mbs", deltaWifiRx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.wifi_subida_mbs", deltaWifiTx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.utp_bajada_mbs", deltaUtpRx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.utp_subida_mbs", deltaUtpTx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.movil_bajada_mbs", deltaMobileRx / Mb / segundos, "MB/s", ahora));
                    lecturas.Add(new("red.movil_subida_mbs", deltaMobileTx / Mb / segundos, "MB/s", ahora));
                }
            }
            else
            {
                acumWifiRx = Math.Max(0, totalRx - mobileRx);
                acumWifiTx = Math.Max(0, totalTx - mobileTx);
            }

            // Consumo total acumulado en MB
            lecturas.Add(new("red.wifi_bajada_mb", acumWifiRx / Mb, "MB", ahora));
            lecturas.Add(new("red.wifi_subida_mb", acumWifiTx / Mb, "MB", ahora));
            lecturas.Add(new("red.utp_bajada_mb", acumUtpRx / Mb, "MB", ahora));
            lecturas.Add(new("red.utp_subida_mb", acumUtpTx / Mb, "MB", ahora));
            lecturas.Add(new("red.movil_bajada_mb", mobileRx / Mb, "MB", ahora));
            lecturas.Add(new("red.movil_subida_mb", mobileTx / Mb, "MB", ahora));

            _anterior = (totalRx, totalTx, mobileRx, mobileTx, acumWifiRx, acumWifiTx, acumUtpRx, acumUtpTx, ahora);
        }

        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
