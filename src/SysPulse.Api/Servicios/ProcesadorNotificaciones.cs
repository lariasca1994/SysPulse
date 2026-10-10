using SysPulse.Infrastructure.Notificaciones;

namespace SysPulse.Api.Servicios;

/// <summary>
/// Consume la cola en memoria de avisos y los envía. No consulta ninguna base periódicamente: solo espera avisos
/// (si Render duerme, el proceso se detiene y no hay nada que procesar).
/// </summary>
public sealed partial class ProcesadorNotificaciones(ColaNotificaciones cola, IServiceScopeFactory scopes, ILogger<ProcesadorNotificaciones> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var aviso in cola.Lector.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DespachadorNotificaciones>().EnviarAsync(aviso, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(ex, aviso.Motivo);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo procesar el aviso {Motivo}")]
    private partial void LogFallo(Exception ex, string motivo);
}
