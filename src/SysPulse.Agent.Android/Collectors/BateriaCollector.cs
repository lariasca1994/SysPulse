using global::Android.Content;
using global::Android.OS;
using SysPulse.Agent.Core.Collectors;
using AndroidApp = global::Android.App.Application;

namespace SysPulse.Agent.Android.Collectors;

/// <summary>Batería (%, carga, temperatura, voltaje, salud) y estado térmico del sistema (Android 10+).</summary>
public sealed class BateriaCollector : ICollector
{
    public string Nombre => "bateria";
    public bool Soportado => true;

    public Task<IReadOnlyList<Lectura>> LeerAsync(CancellationToken cancellationToken)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lecturas = new List<Lectura>();
        var contexto = AndroidApp.Context;

        // ACTION_BATTERY_CHANGED es "sticky": registrar con receptor nulo devuelve el último estado sin suscribirse.
        using var filtro = new IntentFilter(Intent.ActionBatteryChanged);
        using var estado = contexto.RegisterReceiver(null, filtro);
        if (estado is not null)
        {
            var nivel = estado.GetIntExtra(BatteryManager.ExtraLevel, -1);
            var escala = estado.GetIntExtra(BatteryManager.ExtraScale, 100);
            var enchufado = estado.GetIntExtra(BatteryManager.ExtraPlugged, 0) != 0;
            var temperatura = estado.GetIntExtra(BatteryManager.ExtraTemperature, int.MinValue);
            var voltaje = estado.GetIntExtra(BatteryManager.ExtraVoltage, 0);
            var salud = estado.GetIntExtra(BatteryManager.ExtraHealth, 0);

            if (nivel >= 0 && escala > 0)
            {
                var pct = nivel * 100.0 / escala;
                lecturas.Add(new("bateria.pct", pct, "%", ahora));
                lecturas.Add(new("bateria.cargando", enchufado ? 1 : 0, "bool", ahora));
                lecturas.Add(new("bateria.baja_sin_cargar", pct < 20 && !enchufado ? 1 : 0, "bool", ahora));
            }
            if (temperatura != int.MinValue) lecturas.Add(new("bateria.temp_c", temperatura / 10.0, "°C", ahora));
            if (voltaje > 0) lecturas.Add(new("bateria.voltaje_v", voltaje / 1000.0, "V", ahora));
            // BatteryManager.BATTERY_HEALTH_GOOD = 2; cualquier otro valor indica un problema.
            if (salud > 0) lecturas.Add(new("bateria.salud_ok", salud == 2 ? 1 : 0, "bool", ahora));
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(29) && contexto.GetSystemService(Context.PowerService) is PowerManager energia)
        {
            lecturas.Add(new("termico.estado", (int)energia.CurrentThermalStatus, "nivel", ahora));
        }

        return Task.FromResult<IReadOnlyList<Lectura>>(lecturas);
    }
}
