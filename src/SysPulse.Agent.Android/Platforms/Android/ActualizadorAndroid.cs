using global::Android.App;
using global::Android.Content;
using Microsoft.Extensions.Logging;
using SysPulse.Agent.Core.Actualizaciones;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Android;

/// <summary>
/// Actualización del agente Android. Android no deja que una app instalada fuera de Play Store se actualice sin
/// confirmación, así que: al llegar una versión nueva en la configuración central, se descarga sola (GitHub Releases,
/// huella SHA-256 verificada) y se muestra una notificación "Actualizar SysPulse"; un toque abre el instalador.
/// </summary>
internal static partial class ActualizadorAndroid
{
    public const string Autoridad = "dev.syspulse.agent.archivos";
    private const string Canal = "syspulse_actualizaciones";
    private const int IdNotificacion = 1002;
    private static int _avisado;
    private static int _enCurso;

    public static void Revisar(Context contexto, ConfiguracionCentral config, ILogger log)
    {
        var paquete = config.ActualizacionAndroid;
        if (paquete?.Codigo is not { } codigo || codigo <= CodigoInstalado() || Volatile.Read(ref _avisado) == codigo) return;
        if (Interlocked.Exchange(ref _enCurso, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var carpeta = Path.Combine(contexto.CacheDir!.AbsolutePath, "actualizaciones");
                var apk = Path.Combine(carpeta, $"syspulse-{codigo}.apk");
                if (!File.Exists(apk) || !VerificadorPaquete.HuellaValida(apk, paquete.Sha256))
                {
                    // Solo se conserva la última descarga.
                    if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
                    await VerificadorPaquete.DescargarAsync(paquete, apk, CancellationToken.None).ConfigureAwait(false);
                }
                Notificar(contexto, apk, paquete.Version);
                Volatile.Write(ref _avisado, codigo);
                LogListo(log, paquete.Version);
            }
#pragma warning disable CA1031 // Una actualización fallida no debe afectar el monitoreo: se reintenta con la próxima configuración.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFallo(log, paquete.Version, ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _enCurso, 0);
            }
        });
    }

    private static int CodigoInstalado() => int.TryParse(AppInfo.Current.BuildString, out var c) ? c : 0;

    private static void Notificar(Context contexto, string apk, string version)
    {
        if (contexto.GetSystemService(Context.NotificationService) is not NotificationManager gestor) return;
        using (var canal = new NotificationChannel(Canal, "Actualizaciones de SysPulse", NotificationImportance.Default)
        {
            Description = "Aviso cuando hay una versión nueva lista para instalar",
        })
        {
            gestor.CreateNotificationChannel(canal);
        }

        // El APK se entrega al instalador de Android con un permiso temporal de lectura (FileProvider), sin exponer archivos.
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(contexto, Autoridad, new Java.IO.File(apk));
        var instalar = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        var accion = PendingIntent.GetActivity(contexto, IdNotificacion, instalar, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notificacion = new Notification.Builder(contexto, Canal)
            .SetContentTitle("Actualizar SysPulse")
            .SetContentText($"La versión {version} está lista. Toca para instalarla.")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(accion)
            .SetAutoCancel(true)
            .Build();
        gestor.Notify(IdNotificacion, notificacion);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Actualización {Version} descargada y verificada: notificación mostrada")]
    private static partial void LogListo(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preparar la actualización {Version}: {Mensaje}")]
    private static partial void LogFallo(ILogger logger, string version, string mensaje);
}
