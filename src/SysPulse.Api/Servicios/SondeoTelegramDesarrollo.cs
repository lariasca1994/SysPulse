using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Telegram;

namespace SysPulse.Api.Servicios;

/// <summary>
/// SOLO EN DESARROLLO: Telegram no puede llamar al webhook de localhost, así que se consulta al bot con
/// getUpdates (espera larga) mientras la API local está corriendo. En producción se usa el webhook y esto no existe.
/// </summary>
public sealed partial class SondeoTelegramDesarrollo(ClienteTelegram telegram, IServiceScopeFactory scopes, ILogger<SondeoTelegramDesarrollo> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!telegram.Configurado) return;
        LogInicio();
        long desde = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var actualizacion in await telegram.ObtenerActualizacionesAsync(desde, 25, stoppingToken))
                {
                    desde = actualizacion.UpdateId + 1;
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ServicioCuentas>().ProcesarTelegramAsync(actualizacion, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                LogError(ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Desarrollo: consultando al bot de Telegram (getUpdates). En producción se usa el webhook.")]
    private partial void LogInicio();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sondeo de Telegram: {Mensaje}")]
    private partial void LogError(string mensaje);
}
