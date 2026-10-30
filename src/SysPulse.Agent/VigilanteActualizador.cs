using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace SysPulse.Agent;

/// <summary>
/// Actualización forzada: cada 10 minutos el agente revisa que el servicio "SysPulse Actualizador" esté corriendo y, si
/// está detenido (p. ej. tras actualizarse a sí mismo en una versión anterior), lo vuelve a iniciar. El instalador le da
/// a la cuenta del agente permiso solo para consultar e iniciar ese servicio; nada más.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class VigilanteActualizador(ILogger<VigilanteActualizador> log) : BackgroundService
{
    private const string Servicio = "SysPulseActualizador";
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primera revisión a los 2 minutos: no compite con el arranque del equipo.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            Revisar();
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private void Revisar()
    {
        try
        {
            using var sc = new ServiceController(Servicio);
            if (sc.Status != ServiceControllerStatus.Stopped) return;
            sc.Start();
            LogIniciado(log);
        }
        catch (InvalidOperationException ex)
        {
            // No instalado, o sin permiso (equipo instalado antes de esta versión): lo arregla el próximo reinicio.
            LogSinAcceso(log, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Win32Exception ex)
        {
            LogSinAcceso(log, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "El servicio SysPulse Actualizador estaba detenido: se volvió a iniciar")]
    private static partial void LogIniciado(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No se pudo revisar el servicio SysPulse Actualizador: {Motivo}")]
    private static partial void LogSinAcceso(ILogger logger, string motivo);
}
