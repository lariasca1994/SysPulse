using System.Diagnostics;
using System.Management;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>RAM usada/disponible, archivo de paginación (equivalente a swap) y presión de memoria (páginas/s).</summary>
public sealed class MemoriaCollector : ICollector, IDisposable
{
    private const double Mb = 1024 * 1024;

    private readonly PerformanceCounter? _disponibleMb = Contadores.Crear("Memory", "Available MBytes");
    private readonly PerformanceCounter? _paginasSeg = Contadores.Crear("Memory", "Pages/sec");
    private readonly double _totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / Mb;

    public string Nombre => "memoria";
    public bool Soportado => _disponibleMb is not null && _totalMb > 0;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var disponible = _disponibleMb!.NextValue();
        var usada = _totalMb - disponible;
        var lecturas = new List<Lectura>
        {
            new("ram.pct", Math.Clamp(usada / _totalMb * 100, 0, 100), "%", ahora),
            new("ram.usada_mb", usada, "MB", ahora),
            new("ram.disponible_mb", disponible, "MB", ahora),
        };
        if (_paginasSeg is not null) lecturas.Add(new("ram.paginas_seg", _paginasSeg.NextValue(), "pág/s", ahora));
        lecturas.AddRange(LeerPaginacion(ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }

    private static IEnumerable<Lectura> LeerPaginacion(DateTimeOffset ahora)
    {
        double asignado = 0, usado = 0;
        try
        {
            using var consulta = new ManagementObjectSearcher("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            using var resultados = consulta.Get();
            foreach (var archivo in resultados)
            {
                using (archivo)
                {
                    asignado += Convert.ToDouble(archivo["AllocatedBaseSize"], System.Globalization.CultureInfo.InvariantCulture);
                    usado += Convert.ToDouble(archivo["CurrentUsage"], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (ManagementException)
        {
            yield break;
        }

        if (asignado <= 0) yield break;
        yield return new("paginacion.pct", usado / asignado * 100, "%", ahora);
        yield return new("paginacion.usada_mb", usado, "MB", ahora);
    }

    public void Dispose()
    {
        _disponibleMb?.Dispose();
        _paginasSeg?.Dispose();
    }
}
