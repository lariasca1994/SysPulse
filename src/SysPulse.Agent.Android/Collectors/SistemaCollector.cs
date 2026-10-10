using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using SysPulse.Agent.Core.Collectors;
using AndroidApp = global::Android.App.Application;
using AndroidEnv = global::Android.OS.Environment;

namespace SysPulse.Agent.Android.Collectors;

/// <summary>RAM disponible y "memoria baja", almacenamiento interno y uptime del teléfono.</summary>
public sealed class SistemaCollector : ICollector
{
    private const double Mb = 1024d * 1024;
    private const double Gb = 1024d * 1024 * 1024;

    public string Nombre => "sistema";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>();

        if (AndroidApp.Context.GetSystemService(Context.ActivityService) is ActivityManager actividad)
        {
            using var memoria = new ActivityManager.MemoryInfo();
            actividad.GetMemoryInfo(memoria);
            if (memoria.TotalMem > 0)
            {
                lecturas.Add(new("ram.pct", (memoria.TotalMem - memoria.AvailMem) * 100.0 / memoria.TotalMem, "%", ahora));
                lecturas.Add(new("ram.disponible_mb", memoria.AvailMem / Mb, "MB", ahora));
                lecturas.Add(new("ram.baja", memoria.LowMemory ? 1 : 0, "bool", ahora));
            }
        }

        var datos = AndroidEnv.DataDirectory?.AbsolutePath;
        if (datos is not null)
        {
            using var disco = new StatFs(datos);
            var total = disco.TotalBytes;
            if (total > 0)
            {
                lecturas.Add(new("almacenamiento.pct", (total - disco.AvailableBytes) * 100.0 / total, "%", ahora));
                lecturas.Add(new("almacenamiento.libre_gb", disco.AvailableBytes / Gb, "GB", ahora));
            }
        }

        lecturas.Add(new("sistema.uptime_h", SystemClock.ElapsedRealtime() / 3_600_000d, "h", ahora));
        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
