using System.Net.NetworkInformation;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>
/// Tráfico de entrada/salida (MB/s por diferencia entre lecturas y MB totales de consumo),
/// desglosado por Wi-Fi, UTP (Ethernet) y datos móviles (WWAN).
/// </summary>
public sealed class RedCollector : ICollector
{
    private const double Mb = 1024d * 1024;

    private readonly Dictionary<string, (long Rx, long Tx)> _porInterfaz = new(StringComparer.OrdinalIgnoreCase);
    private (long Errores, long Descartes, DateTimeOffset Ts)? _anterior;

    public string Nombre => "red";
    public bool Soportado => true;

    private enum TipoRed
    {
        Wifi,
        Utp,
        Movil,
        Otro
    }

    private static TipoRed Clasificar(NetworkInterface nic)
    {
        var tipo = nic.NetworkInterfaceType;
        if (tipo == NetworkInterfaceType.Wireless80211)
            return TipoRed.Wifi;

        if (tipo is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx)
        {
            var desc = nic.Description;
            if (desc.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                nic.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase))
            {
                return TipoRed.Otro;
            }
            return TipoRed.Utp;
        }

        if (tipo is NetworkInterfaceType.Wman
            or NetworkInterfaceType.Wwanpp
            or NetworkInterfaceType.Wwanpp2 ||
            nic.Description.Contains("Cellular", StringComparison.OrdinalIgnoreCase) ||
            nic.Description.Contains("Mobile Broadband", StringComparison.OrdinalIgnoreCase) ||
            nic.Description.Contains("WWAN", StringComparison.OrdinalIgnoreCase))
        {
            return TipoRed.Movil;
        }

        return TipoRed.Otro;
    }

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        long totalRx = 0, totalTx = 0, errores = 0, descartes = 0;
        long wifiRx = 0, wifiTx = 0;
        long utpRx = 0, utpTx = 0;
        long movilRx = 0, movilTx = 0;

        long deltaTotalRx = 0, deltaTotalTx = 0;
        long deltaWifiRx = 0, deltaWifiTx = 0;
        long deltaUtpRx = 0, deltaUtpTx = 0;
        long deltaMovilRx = 0, deltaMovilTx = 0;

        var activas = 0;
        var interfacesActivas = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();

        foreach (var nic in interfacesActivas)
        {
            long nicRx, nicTx, nicErr, nicDisc;
            try
            {
                var s = nic.GetIPStatistics();
                nicRx = s.BytesReceived;
                nicTx = s.BytesSent;
                nicErr = s.IncomingPacketsWithErrors + s.OutgoingPacketsWithErrors;
                nicDisc = s.IncomingPacketsDiscarded + s.OutgoingPacketsDiscarded;
            }
            catch
            {
                try
                {
                    var s = nic.GetIPv4Statistics();
                    nicRx = s.BytesReceived;
                    nicTx = s.BytesSent;
                    nicErr = s.IncomingPacketsWithErrors + s.OutgoingPacketsWithErrors;
                    nicDisc = s.IncomingPacketsDiscarded + s.OutgoingPacketsDiscarded;
                }
                catch
                {
                    continue;
                }
            }

            activas++;
            errores += nicErr;
            descartes += nicDisc;

            var tipo = Clasificar(nic);

            long dRx = 0, dTx = 0;
            if (_porInterfaz.TryGetValue(nic.Id, out var prev))
            {
                if (nicRx >= prev.Rx && nicTx >= prev.Tx)
                {
                    dRx = nicRx - prev.Rx;
                    dTx = nicTx - prev.Tx;
                }
            }
            _porInterfaz[nic.Id] = (nicRx, nicTx);

            deltaTotalRx += dRx;
            deltaTotalTx += dTx;
            totalRx += nicRx;
            totalTx += nicTx;

            switch (tipo)
            {
                case TipoRed.Wifi:
                    wifiRx += nicRx;
                    wifiTx += nicTx;
                    deltaWifiRx += dRx;
                    deltaWifiTx += dTx;
                    break;
                case TipoRed.Utp:
                    utpRx += nicRx;
                    utpTx += nicTx;
                    deltaUtpRx += dRx;
                    deltaUtpTx += dTx;
                    break;
                case TipoRed.Movil:
                    movilRx += nicRx;
                    movilTx += nicTx;
                    deltaMovilRx += dRx;
                    deltaMovilTx += dTx;
                    break;
            }
        }

        // Limpiar interfaces desconectadas de la memoria interna
        var idsActivas = new HashSet<string>(interfacesActivas.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var desconectadas = _porInterfaz.Keys.Where(id => !idsActivas.Contains(id)).ToList();
        foreach (var id in desconectadas)
        {
            _porInterfaz.Remove(id);
        }

        var lecturas = new List<Lectura> { new("red.interfaces_activas", activas, "nic", ahora) };

        if (_anterior is { } a)
        {
            var segundos = (ahora - a.Ts).TotalSeconds;
            if (segundos > 0)
            {
                lecturas.Add(new("red.entrada_mbs", deltaTotalRx / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.salida_mbs", deltaTotalTx / Mb / segundos, "MB/s", ahora));

                lecturas.Add(new("red.wifi_bajada_mbs", deltaWifiRx / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.wifi_subida_mbs", deltaWifiTx / Mb / segundos, "MB/s", ahora));

                lecturas.Add(new("red.utp_bajada_mbs", deltaUtpRx / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.utp_subida_mbs", deltaUtpTx / Mb / segundos, "MB/s", ahora));

                lecturas.Add(new("red.movil_bajada_mbs", deltaMovilRx / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.movil_subida_mbs", deltaMovilTx / Mb / segundos, "MB/s", ahora));

                lecturas.Add(new("red.errores", Math.Max(0, errores - a.Errores), "paquetes", ahora));
                lecturas.Add(new("red.descartes", Math.Max(0, descartes - a.Descartes), "paquetes", ahora));
            }
        }

        // Consumo total acumulado en MB
        lecturas.Add(new("red.wifi_bajada_mb", wifiRx / Mb, "MB", ahora));
        lecturas.Add(new("red.wifi_subida_mb", wifiTx / Mb, "MB", ahora));
        lecturas.Add(new("red.utp_bajada_mb", utpRx / Mb, "MB", ahora));
        lecturas.Add(new("red.utp_subida_mb", utpTx / Mb, "MB", ahora));
        lecturas.Add(new("red.movil_bajada_mb", movilRx / Mb, "MB", ahora));
        lecturas.Add(new("red.movil_subida_mb", movilTx / Mb, "MB", ahora));

        _anterior = (errores, descartes, ahora);

        var tcp = IPGlobalProperties.GetIPGlobalProperties();
        lecturas.Add(new("red.tcp_establecidas", tcp.GetActiveTcpConnections().Count(c => c.State == TcpState.Established), "conexiones", ahora));
        lecturas.Add(new("red.puertos_escucha", tcp.GetActiveTcpListeners().Length, "puertos", ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
