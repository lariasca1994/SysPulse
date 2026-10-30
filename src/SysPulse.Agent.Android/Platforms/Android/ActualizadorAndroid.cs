using global::Android.App;
using global::Android.Content;
using Microsoft.Extensions.Logging;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Android;

/// <summary>
/// Actualización obligatoria del agente Android sin que la app instale nada por su cuenta: al llegar una versión nueva
/// en la configuración central, queda una notificación fija (no se puede descartar) y un aviso en la pantalla; ambos
/// abren la descarga oficial (GitHub Releases) en el navegador y la instala Android con su pantalla de siempre.
/// Antes la app descargaba e instalaba el APK sola (permiso REQUEST_INSTALL_PACKAGES): Play Protect trata ese patrón
/// como el de una app maliciosa que instala otras y la marcaba como peligrosa.
/// </summary>
internal static partial class ActualizadorAndroid
{
    private const string Canal = "syspulse_actualizacion_obligatoria";
    private const int IdNotificacion = 1002;

    /// <summary>Versión nueva pendiente de instalar (la pantalla muestra el aviso y el botón de descarga).</summary>
    internal static (string Version, string Url)? Pendiente { get; private set; }

    public static void Revisar(Context contexto, ConfiguracionCentral config, ILogger log)
    {
        var paquete = config.ActualizacionAndroid;
        if (paquete?.Codigo is not { } codigo || codigo <= CodigoInstalado() || string.IsNullOrWhiteSpace(paquete.Url))
        {
            if (Pendiente is not null) QuitarNotificacion(contexto);
            return;
        }
        if (Pendiente?.Version == paquete.Version) return;
        Pendiente = (paquete.Version, paquete.Url);
        Notificar(contexto, paquete.Version, paquete.Url);
        LogPendiente(log, paquete.Version);
    }

    /// <summary>Abre la descarga en el navegador (botón de la pantalla).</summary>
    public static void AbrirDescarga(Context contexto)
    {
        if (Pendiente is not { } p) return;
        try
        {
            contexto.StartActivity(Descarga(p.Url));
        }
        catch (ActivityNotFoundException)
        {
            // Sin navegador: queda la notificación.
        }
    }

    private static Intent Descarga(string url) =>
        new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url)).AddFlags(ActivityFlags.NewTask);

    private static int CodigoInstalado() => int.TryParse(AppInfo.Current.BuildString, out var c) ? c : 0;

    /// <summary>Notificación fija mientras haya una actualización obligatoria sin instalar.</summary>
    private static void Notificar(Context contexto, string version, string url)
    {
        if (contexto.GetSystemService(Context.NotificationService) is not NotificationManager gestor) return;
        using (var canal = new NotificationChannel(Canal, "Actualización obligatoria de SysPulse", NotificationImportance.High)
        {
            Description = "Hay una versión nueva del agente que debe instalarse",
        })
        {
            gestor.CreateNotificationChannel(canal);
        }
        var accion = PendingIntent.GetActivity(contexto, IdNotificacion, Descarga(url), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notificacion = new Notification.Builder(contexto, Canal)
            .SetContentTitle($"Actualización obligatoria de SysPulse ({version})")
            .SetContentText("Toca para descargarla e instalarla. El monitoreo necesita esta versión.")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(accion)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .Build();
        gestor.Notify(IdNotificacion, notificacion);
    }

    private static void QuitarNotificacion(Context contexto)
    {
        Pendiente = null;
        (contexto.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(IdNotificacion);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Actualización obligatoria {Version} disponible: notificación para descargarla")]
    private static partial void LogPendiente(ILogger logger, string version);
}
