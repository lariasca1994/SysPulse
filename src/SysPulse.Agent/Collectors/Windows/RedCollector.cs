using System.Net.NetworkInformation;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>Tráfico de entrada/salida (MB/s, por diferencia entre lecturas), errores, descartes y conexiones TCP.</summary>
public sealed class RedCollector : ICollector
{
    private const double Mb = 1024d * 1024;

    private (long Rx, long Tx, long Errores, long Descartes, DateTimeOffset Ts)? _anterior;

    public string Nombre => "red";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        long rx = 0, tx = 0, errores = 0, descartes = 0;
        var activas = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
        {
            var s = nic.GetIPv4Statistics();
            rx += s.BytesReceived;
            tx += s.BytesSent;
            errores += s.IncomingPacketsWithErrors + s.OutgoingPacketsWithErrors;
            descartes += s.IncomingPacketsDiscarded + s.OutgoingPacketsDiscarded;
            activas++;
        }

        var lecturas = new List<Lectura> { new("red.interfaces_activas", activas, "nic", ahora) };
        if (_anterior is { } a)
        {
            var segundos = (ahora - a.Ts).TotalSeconds;
            if (segundos > 0 && rx >= a.Rx && tx >= a.Tx)
            {
                lecturas.Add(new("red.entrada_mbs", (rx - a.Rx) / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.salida_mbs", (tx - a.Tx) / Mb / segundos, "MB/s", ahora));
                lecturas.Add(new("red.errores", Math.Max(0, errores - a.Errores), "paquetes", ahora));
                lecturas.Add(new("red.descartes", Math.Max(0, descartes - a.Descartes), "paquetes", ahora));
            }
        }
        _anterior = (rx, tx, errores, descartes, ahora);

        var tcp = IPGlobalProperties.GetIPGlobalProperties();
        lecturas.Add(new("red.tcp_establecidas", tcp.GetActiveTcpConnections().Count(c => c.State == TcpState.Established), "conexiones", ahora));
        lecturas.Add(new("red.puertos_escucha", tcp.GetActiveTcpListeners().Length, "puertos", ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
