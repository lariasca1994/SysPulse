using System.Diagnostics;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>Uso por unidad fija (disco.C.pct) y E/S de los discos físicos (MB/s, % activo, cola).</summary>
public sealed class DiscoCollector : ICollector, IDisposable
{
    private const double Gb = 1024d * 1024 * 1024;
    private const double Mb = 1024d * 1024;

    private readonly PerformanceCounter? _inactivo = Contadores.Crear("PhysicalDisk", "% Idle Time", "_Total");
    private readonly PerformanceCounter? _lectura = Contadores.Crear("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
    private readonly PerformanceCounter? _escritura = Contadores.Crear("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
    private readonly PerformanceCounter? _cola = Contadores.Crear("PhysicalDisk", "Current Disk Queue Length", "_Total");

    public string Nombre => "disco";
    public bool Soportado => true;

    /// <summary>Unidades fijas listas (las que reciben umbral de disco).</summary>
    public static IEnumerable<string> Unidades() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name[..1].ToUpperInvariant());

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>();
        foreach (var unidad in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var letra = unidad.Name[..1].ToUpperInvariant();
            var usado = unidad.TotalSize - unidad.TotalFreeSpace;
            lecturas.Add(new($"disco.{letra}.pct", usado * 100d / unidad.TotalSize, "%", ahora));
            lecturas.Add(new($"disco.{letra}.libre_gb", unidad.TotalFreeSpace / Gb, "GB", ahora));
        }
        if (_inactivo is not null) lecturas.Add(new("disco.activo_pct", Math.Clamp(100 - _inactivo.NextValue(), 0, 100), "%", ahora));
        if (_lectura is not null) lecturas.Add(new("disco.lectura_mbs", _lectura.NextValue() / Mb, "MB/s", ahora));
        if (_escritura is not null) lecturas.Add(new("disco.escritura_mbs", _escritura.NextValue() / Mb, "MB/s", ahora));
        if (_cola is not null) lecturas.Add(new("disco.cola", _cola.NextValue(), "ops", ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }

    public void Dispose()
    {
        _inactivo?.Dispose();
        _lectura?.Dispose();
        _escritura?.Dispose();
        _cola?.Dispose();
    }
}
