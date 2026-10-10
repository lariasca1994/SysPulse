using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;
using global::Android.Runtime;
using Microsoft.Extensions.Logging;
using SysPulse.Agent.Android.Collectors;
using SysPulse.Agent.Core;
using SysPulse.Agent.Core.Buffer;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Agent.Core.Envio;

namespace SysPulse.Agent.Android;

/// <summary>
/// Servicio en primer plano de tipo specialUse (sección 5): corre el motor común 24/7 con los colectores de Android.
/// Se declara a mano en AndroidManifest.xml (incluye la propiedad PROPERTY_SPECIAL_USE_FGS_SUBTYPE).
/// </summary>
[Register("dev.syspulse.agent.ServicioMonitoreo")]
public sealed partial class ServicioMonitoreo : Service
{
    public const string AccionDetener = "dev.syspulse.agent.DETENER";
    private const string Canal = "syspulse_monitoreo";
    private const int IdNotificacion = 1001;

    private static readonly HttpClient Http = new();
    private static readonly ILoggerFactory Logs = Logcat.CrearFactory();

    private CancellationTokenSource? _cancelacion;
    private PowerManager.WakeLock? _wakeLock;
    private BufferLocal? _buffer;

    /// <summary>Motor en ejecución (la pantalla lee su estado). Null si el servicio no corre.</summary>
    public static MotorAgente? Motor { get; private set; }

    public static void Iniciar(Context contexto)
    {
        var intento = new Intent(contexto, typeof(ServicioMonitoreo));
        contexto.StartForegroundService(intento);
    }

    public static void Detener(Context contexto)
    {
        var intento = new Intent(contexto, typeof(ServicioMonitoreo)).SetAction(AccionDetener);
        contexto.StartService(intento);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == AccionDetener)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CrearCanal();
        var notificacion = new Notification.Builder(this, Canal)
            .SetContentTitle("SysPulse monitoreando")
            .SetContentText("Batería, temperatura, memoria y red cada minuto")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .SetContentIntent(PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable))
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(IdNotificacion, notificacion, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(IdNotificacion, notificacion);
        }

        if (_cancelacion is null)
        {
            _cancelacion = new CancellationTokenSource();
            // Wake lock parcial: el teléfono está siempre cargando, así que el costo de batería no importa,
            // y evita que la CPU se duerma entre lecturas.
            if (GetSystemService(PowerService) is PowerManager energia)
            {
                _wakeLock = energia.NewWakeLock(WakeLockFlags.Partial, "SysPulse::Monitoreo");
                _wakeLock?.Acquire();
            }
            _ = Task.Run(() => CorrerAsync(_cancelacion.Token));
        }

        return StartCommandResult.Sticky;
    }

    private async Task CorrerAsync(CancellationToken ct)
    {
        var log = Logs.CreateLogger<ServicioMonitoreo>();
        try
        {
            var opciones = await ConfiguracionAndroid.CargarOpcionesAsync();
            _buffer = new BufferLocal(Path.Combine(opciones.CarpetaDatos, "agente.db"));
            ICollector[] colectores = [new BateriaCollector(), new SistemaCollector(), new RedMovilCollector()];
            var respaldo = new NotificadorRespaldo(Http, opciones.TelegramBotToken, opciones.TelegramChatIdsRespaldo);
            Motor = new MotorAgente(colectores, opciones, _buffer, respaldo, Logs.CreateLogger<MotorAgente>());
            // Versión nueva publicada en la configuración central: se descarga y se avisa con una notificación.
            Motor.ConfiguracionAplicada += config => ActualizadorAndroid.Revisar(this, config, log);
            await Motor.EjecutarAsync(AppInfo.Current.VersionString, [], ct);
        }
        catch (System.OperationCanceledException)
        {
            // Detenido por el usuario.
        }
#pragma warning disable CA1031 // El servicio no debe caerse: se registra y queda detenido.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogMotorDetenido(log, ex);
        }
    }

    public override void OnDestroy()
    {
        _cancelacion?.Cancel();
        _cancelacion?.Dispose();
        _cancelacion = null;
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock = null;
        Motor = null;
        _buffer?.Dispose();
        base.OnDestroy();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "El motor se detuvo por un error")]
    private static partial void LogMotorDetenido(ILogger logger, Exception ex);

    private void CrearCanal()
    {
        if (GetSystemService(NotificationService) is not NotificationManager gestor) return;
        using var canal = new NotificationChannel(Canal, "Monitoreo de SysPulse", NotificationImportance.Low)
        {
            Description = "Aviso permanente mientras SysPulse mide el teléfono",
        };
        gestor.CreateNotificationChannel(canal);
    }
}
