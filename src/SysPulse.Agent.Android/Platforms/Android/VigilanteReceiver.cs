using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using global::Android.Runtime;

namespace SysPulse.Agent.Android;

/// <summary>
/// Vigilante del monitoreo: una alarma cada 15 minutos (también con el teléfono en reposo) revisa que el servicio
/// siga vivo y midiendo. Si el sistema lo cerró (ahorro de batería de Xiaomi/HyperOS, falta de memoria, la app
/// quitada de Recientes) lo vuelve a iniciar; si sigue vivo pero el motor dejó de medir, reinicia el motor.
/// Usa una alarma normal (no exacta), sin permisos especiales. Si Android no deja reabrir el servicio desde segundo
/// plano (sin "Sin restricciones" de batería), muestra una notificación para reanudarlo con un toque.
/// </summary>
[Register("dev.syspulse.agent.VigilanteReceiver")]
public sealed class VigilanteReceiver : BroadcastReceiver
{
    public const string Accion = "dev.syspulse.agent.VIGILAR";
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    /// <summary>Sin ciclos del motor por más de este tiempo (mide cada minuto), se considera detenido.</summary>
    public static readonly TimeSpan SinLatido = TimeSpan.FromMinutes(10);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Accion) return;
        if (!ConfiguracionAndroid.MonitoreoActivo) return;

        Programar(context, Intervalo);
        if (ServicioMonitoreo.Detenido)
        {
            // Servicio vivo pero motor sin medir: se reinicia el motor dentro del mismo servicio.
            ServicioMonitoreo.ReiniciarMotor();
            return;
        }
        if (ServicioMonitoreo.Motor is null) ServicioMonitoreo.IniciarSeguro(context);
    }

    /// <summary>Programa (o reprograma) la próxima revisión. Una sola alarma: la nueva reemplaza a la anterior.</summary>
    public static void Programar(Context contexto, TimeSpan? dentroDe = null)
    {
        if (contexto.GetSystemService(Context.AlarmService) is not AlarmManager alarmas) return;
        var intento = new Intent(contexto, typeof(VigilanteReceiver)).SetAction(Accion);
        var pendiente = PendingIntent.GetBroadcast(contexto, 0, intento, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        if (pendiente is null) return;
        var cuando = Java.Lang.JavaSystem.CurrentTimeMillis() + (long)(dentroDe ?? Intervalo).TotalMilliseconds;
        alarmas.SetAndAllowWhileIdle(AlarmType.RtcWakeup, cuando, pendiente);
    }

    public static void Cancelar(Context contexto)
    {
        if (contexto.GetSystemService(Context.AlarmService) is not AlarmManager alarmas) return;
        var intento = new Intent(contexto, typeof(VigilanteReceiver)).SetAction(Accion);
        var pendiente = PendingIntent.GetBroadcast(contexto, 0, intento, PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate);
        if (pendiente is not null) alarmas.Cancel(pendiente);
    }
}
