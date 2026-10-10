using System.Diagnostics;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>CPU total (mismo valor que el Administrador de tareas) y cola del procesador (equivalente a load average).</summary>
public sealed class CpuCollector : ICollector, IDisposable
{
    // "% Processor Utility" coincide con el Administrador de tareas; si no existe, "% Processor Time".
    private readonly PerformanceCounter? _total =
        Contadores.Crear("Processor Information", "% Processor Utility", "_Total") ?? Contadores.Crear("Processor", "% Processor Time", "_Total");
    private readonly PerformanceCounter? _cola = Contadores.Crear("System", "Processor Queue Length");

    public string Nombre => "cpu";
    public bool Soportado => _total is not null;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura> { new("cpu.total", Math.Clamp(_total!.NextValue(), 0, 100), "%", ahora) };
        if (_cola is not null) lecturas.Add(new("cpu.cola", _cola.NextValue(), "hilos", ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }

    public void Dispose()
    {
        _total?.Dispose();
        _cola?.Dispose();
    }
}
