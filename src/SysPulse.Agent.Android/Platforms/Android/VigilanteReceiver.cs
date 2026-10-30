using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using global::Android.Runtime;

namespace SysPulse.Agent.Android;

/// <summary>
/// Vigilante del monitoreo: una alarma cada 15 minutos (también con el teléfono en reposo) revisa que el servicio
/// siga vivo y midiendo. Si el sistema lo cerró (ahorro de batería de Xiaomi/HyperOS, falta de memoria, la app
/// quitada de Recientes) lo vuelve a iniciar; si sigue vivo pero el motor dejó de medir, reinicia la app.
/// La alarma exacta permite iniciar el servicio en primer plano desde segundo plano (Android 12+).
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
            // Proceso vivo pero motor sin medir: se reinicia la app entera (la alarma corta la vuelve a abrir).
            Programar(context, TimeSpan.FromSeconds(5));
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
            return;
        }
        ServicioMonitoreo.IniciarSeguro(context);
    }

    /// <summary>Programa (o reprograma) la próxima revisión. Una sola alarma: la nueva reemplaza a la anterior.</summary>
    public static void Programar(Context contexto, TimeSpan? dentroDe = null)
    {
        if (contexto.GetSystemService(Context.AlarmService) is not AlarmManager alarmas) return;
        var intento = new Intent(contexto, typeof(VigilanteReceiver)).SetAction(Accion);
        var pendiente = PendingIntent.GetBroadcast(contexto, 0, intento, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        if (pendiente is null) return;
        var cuando = Java.Lang.JavaSystem.CurrentTimeMillis() + (long)(dentroDe ?? Intervalo).TotalMilliseconds;
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarmas.CanScheduleExactAlarms())
        {
            alarmas.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, cuando, pendiente);
        }
        else
        {
            alarmas.SetAndAllowWhileIdle(AlarmType.RtcWakeup, cuando, pendiente);
        }
    }

    public static void Cancelar(Context contexto)
    {
        if (contexto.GetSystemService(Context.AlarmService) is not AlarmManager alarmas) return;
        var intento = new Intent(contexto, typeof(VigilanteReceiver)).SetAction(Accion);
        var pendiente = PendingIntent.GetBroadcast(contexto, 0, intento, PendingIntentFlags.Immutable | PendingIntentFlags.NoCreate);
        if (pendiente is not null) alarmas.Cancel(pendiente);
    }
}
