using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.Runtime;
using Microsoft.Extensions.Logging;
using SysPulse.Agent.Core.Actualizaciones;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Android;

/// <summary>
/// Actualización obligatoria del agente Android, con el instalador de paquetes del sistema (PackageInstaller):
/// 1. al llegar una versión nueva en la configuración central se descarga sola (GitHub Releases, huella SHA-256);
/// 2. se instala pidiendo a Android que no requiera confirmación (Android 12+). Android solo lo concede si esta app es
///    su propio "instalador de registro": la primera actualización pide confirmación y desde ahí son automáticas;
/// 3. si Android pide confirmación, queda una notificación fija (no se puede descartar) que se repite hasta instalar,
///    y al abrir la app se muestra directamente la pantalla de instalación.
/// </summary>
internal static partial class ActualizadorAndroid
{
    public const string Autoridad = "dev.syspulse.agent.archivos";
    public const string AccionResultado = "dev.syspulse.agent.RESULTADO_INSTALACION";
    private const string Canal = "syspulse_actualizacion_obligatoria";
    private const int IdNotificacion = 1002;
    private static readonly TimeSpan Reintento = TimeSpan.FromMinutes(30);
    private static long _ultimoIntentoTicks;
    private static int _enCurso;
    private static ILogger? _log;

    /// <summary>Confirmación pendiente que entregó Android: se lanza al abrir la app.</summary>
    internal static Intent? ConfirmacionPendiente { get; set; }

    public static void Revisar(Context contexto, ConfiguracionCentral config, ILogger log)
    {
        _log = log;
        var paquete = config.ActualizacionAndroid;
        if (paquete?.Codigo is not { } codigo || codigo <= CodigoInstalado()) return;
        var ahora = DateTime.UtcNow.Ticks;
        if (ahora - Interlocked.Read(ref _ultimoIntentoTicks) < Reintento.Ticks) return;
        if (Interlocked.Exchange(ref _enCurso, 1) == 1) return;
        Interlocked.Exchange(ref _ultimoIntentoTicks, ahora);
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
                Instalar(contexto, apk);
                LogListo(log, paquete.Version);
            }
#pragma warning disable CA1031 // Una actualización fallida no debe afectar el monitoreo: se reintenta en 30 minutos.
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

    /// <summary>Al abrir la app: si Android espera confirmación de una actualización, se muestra de inmediato.</summary>
    public static void MostrarConfirmacionSiHay(Context contexto)
    {
        if (ConfirmacionPendiente is not { } confirmar) return;
        try
        {
            contexto.StartActivity(new Intent(confirmar).AddFlags(ActivityFlags.NewTask));
        }
        catch (ActivityNotFoundException)
        {
            ConfirmacionPendiente = null;
        }
    }

    private static int CodigoInstalado() => int.TryParse(AppInfo.Current.BuildString, out var c) ? c : 0;

    private static void Instalar(Context contexto, string apk)
    {
        var instalador = contexto.PackageManager!.PackageInstaller;

        // Sesiones anteriores sin terminar (p. ej. una confirmación ignorada) se descartan para no acumularlas.
        foreach (var vieja in instalador.MySessions)
        {
            try
            {
                instalador.AbandonSession(vieja.SessionId);
            }
            catch (Java.Lang.SecurityException)
            {
                // Ya no es nuestra: se ignora.
            }
        }

        var parametros = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parametros.SetAppPackageName(contexto.PackageName);
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) parametros.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);

        var id = instalador.CreateSession(parametros);
        using var sesion = instalador.OpenSession(id);
        using (var entrada = File.OpenRead(apk))
        using (var salida = sesion.OpenWrite("syspulse.apk", 0, entrada.Length))
        {
            entrada.CopyTo(salida);
            sesion.Fsync(salida);
        }

        var resultado = new Intent(contexto, typeof(ResultadoInstalacionReceiver)).SetAction(AccionResultado);
        // Debe ser mutable: Android agrega el estado de la instalación al intent (antes de Android 12 lo es por defecto).
        var banderas = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent
            : PendingIntentFlags.UpdateCurrent;
        var pendiente = PendingIntent.GetBroadcast(contexto, id, resultado, banderas);
        sesion.Commit(pendiente!.IntentSender!);
    }

    /// <summary>Notificación fija (no se puede descartar) mientras haya una actualización obligatoria sin instalar.</summary>
    internal static void NotificarConfirmacion(Context contexto, Intent confirmar)
    {
        ConfirmacionPendiente = confirmar;
        if (contexto.GetSystemService(Context.NotificationService) is not NotificationManager gestor) return;
        using (var canal = new NotificationChannel(Canal, "Actualización obligatoria de SysPulse", NotificationImportance.High)
        {
            Description = "Hay una versión nueva del agente que debe instalarse",
        })
        {
            gestor.CreateNotificationChannel(canal);
        }
        var accion = PendingIntent.GetActivity(contexto, IdNotificacion, new Intent(confirmar).AddFlags(ActivityFlags.NewTask),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notificacion = new Notification.Builder(contexto, Canal)
            .SetContentTitle("Actualización obligatoria de SysPulse")
            .SetContentText("Toca para instalar la versión nueva. El monitoreo necesita esta actualización.")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(accion)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .Build();
        gestor.Notify(IdNotificacion, notificacion);
    }

    internal static void QuitarNotificacion(Context contexto)
    {
        ConfirmacionPendiente = null;
        (contexto.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(IdNotificacion);
    }

    internal static void RegistrarResultado(string mensaje)
    {
        if (_log is { } log) LogResultado(log, mensaje);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Actualización {Version} descargada y verificada: instalación enviada a Android")]
    private static partial void LogListo(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preparar la actualización {Version}: {Mensaje}")]
    private static partial void LogFallo(ILogger logger, string version, string mensaje);

    [LoggerMessage(Level = LogLevel.Information, Message = "Instalación de la actualización: {Mensaje}")]
    private static partial void LogResultado(ILogger logger, string mensaje);
}

/// <summary>Recibe de Android el resultado de cada sesión de instalación.</summary>
[BroadcastReceiver(Exported = false)]
[Register("dev.syspulse.agent.ResultadoInstalacionReceiver")]
public sealed class ResultadoInstalacionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var estado = intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        switch ((PackageInstallStatus)estado)
        {
            case PackageInstallStatus.PendingUserAction:
                // Android pide confirmación (primera actualización o sin permiso aún): notificación fija hasta instalar.
                var confirmar = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent
#pragma warning disable CA1422 // Antes de Android 13 esta es la forma disponible.
                    : intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422
                if (confirmar is not null) ActualizadorAndroid.NotificarConfirmacion(context, confirmar);
                ActualizadorAndroid.RegistrarResultado("Android pide confirmación al usuario");
                break;
            case PackageInstallStatus.Success:
                ActualizadorAndroid.QuitarNotificacion(context);
                ActualizadorAndroid.RegistrarResultado("instalada");
                break;
            default:
                ActualizadorAndroid.RegistrarResultado($"falló ({intent.GetStringExtra(PackageInstaller.ExtraStatusMessage)}); se reintenta en 30 minutos");
                break;
        }
    }
}
