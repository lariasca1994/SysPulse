using global::Android.Content;
using global::Android.Runtime;

namespace SysPulse.Agent.Android;

/// <summary>
/// Reanuda el monitoreo al encender el teléfono y tras actualizar la app, si el usuario lo dejó encendido.
/// El tipo specialUse sí puede iniciarse desde BOOT_COMPLETED en Android 15/16.
/// </summary>
[Register("dev.syspulse.agent.ArranqueReceiver")]
public sealed class ArranqueReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        if (intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced)) return;
        if (!ConfiguracionAndroid.MonitoreoActivo) return;
        VigilanteReceiver.Programar(context);
        ServicioMonitoreo.IniciarSeguro(context);
    }
}
