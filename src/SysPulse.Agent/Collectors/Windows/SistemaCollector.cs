using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>
/// Software y sistema: procesos e hilos, uptime, errores del Event Log (últimos 5 min), reinicios inesperados,
/// Windows Update pendiente de reinicio y batería (si es portátil).
/// </summary>
public sealed class SistemaCollector : ICollector, IDisposable
{
    // Nivel 1 = Crítico, 2 = Error. Ventana de 5 min en milisegundos.
    private const string ConsultaErrores = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 300000]]]";
    // Kernel-Power 41 (apagado inesperado) y EventLog 6008 (cierre inesperado anterior), últimas 24 h.
    private const string ConsultaReinicios = "*[System[(EventID=41 or EventID=6008) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";

    private readonly PerformanceCounter? _procesos = Contadores.Crear("System", "Processes");
    private readonly PerformanceCounter? _hilos = Contadores.Crear("System", "Threads");

    public string Nombre => "sistema";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>
        {
            new("sistema.uptime_h", Environment.TickCount64 / 3_600_000d, "h", ahora),
            new("eventlog.errores_5m", Contar("System", ConsultaErrores) + Contar("Application", ConsultaErrores), "eventos", ahora),
            new("sistema.reinicios_inesperados_24h", Contar("System", ConsultaReinicios), "eventos", ahora),
            new("sistema.reinicio_pendiente", ReinicioPendiente() ? 1 : 0, "bool", ahora),
        };
        if (_procesos is not null) lecturas.Add(new("sistema.procesos", _procesos.NextValue(), "procesos", ahora));
        if (_hilos is not null) lecturas.Add(new("sistema.hilos", _hilos.NextValue(), "hilos", ahora));
        lecturas.AddRange(Bateria(ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }

    private static int Contar(string registro, string consulta)
    {
        try
        {
            using var lector = new EventLogReader(new EventLogQuery(registro, PathType.LogName, consulta));
            var total = 0;
            for (var evento = lector.ReadEvent(); evento is not null; evento = lector.ReadEvent())
            {
                evento.Dispose();
                total++;
            }
            return total;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool ReinicioPendiente()
    {
        using var clave = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        return clave is not null;
    }

    private static List<Lectura> Bateria(DateTimeOffset ahora)
    {
        var lecturas = new List<Lectura>();
        try
        {
            using var consulta = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            using var resultados = consulta.Get();
            foreach (var bateria in resultados)
            {
                using (bateria)
                {
                    var pct = Convert.ToDouble(bateria["EstimatedChargeRemaining"], CultureInfo.InvariantCulture);
                    // BatteryStatus 2 = conectado a corriente.
                    var cargando = Convert.ToInt32(bateria["BatteryStatus"], CultureInfo.InvariantCulture) == 2;
                    lecturas.Add(new("bateria.pct", pct, "%", ahora));
                    lecturas.Add(new("bateria.cargando", cargando ? 1 : 0, "bool", ahora));
                    // Portátiles y tablets que deben estar enchufados 24/7: alerta si se desconectan y bajan de 20 %.
                    lecturas.Add(new("bateria.baja_sin_cargar", pct < 20 && !cargando ? 1 : 0, "bool", ahora));
                }
                break;
            }
        }
        catch (ManagementException)
        {
            // Equipo de escritorio o WMI no disponible: sin métricas de batería.
        }
        return lecturas;
    }

    public void Dispose()
    {
        _procesos?.Dispose();
        _hilos?.Dispose();
    }
}
