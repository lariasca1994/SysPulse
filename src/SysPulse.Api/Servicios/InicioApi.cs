using MongoDB.Driver;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

/// <summary>
/// Al arrancar: índices de Mongo y admin semilla (si no existe ninguno).
/// Si MongoDB no responde, la API arranca igual (las peticiones responden 503 de
/// mantenimiento, ver BaseNoDisponible) y la preparación se reintenta cada 30 s en
/// segundo plano, en vez de tumbar el proceso.
/// </summary>
public sealed partial class InicioApi(ContextoMongo db, IServiceScopeFactory scopes, IConfiguration config, ILogger<InicioApi> logger)
    : IHostedService, IDisposable
{
    private static readonly TimeSpan EsperaReintento = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _parar = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Con la base arriba, igual que siempre: listo antes de atender peticiones.
        if (await IntentarPrepararAsync(cancellationToken)) return;
        _ = ReintentarAsync(_parar.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _parar.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose() => _parar.Dispose();

    private async Task ReintentarAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(EsperaReintento, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await IntentarPrepararAsync(ct))
            {
                LogBaseRecuperada();
                return;
            }
        }
    }

    private async Task<bool> IntentarPrepararAsync(CancellationToken ct)
    {
        try
        {
            await db.AsegurarIndicesAsync(ct);
            using var scope = scopes.CreateScope();
            var cuentas = scope.ServiceProvider.GetRequiredService<ServicioCuentas>();
            if (await cuentas.SembrarAdminAsync(config["ADMIN_EMAIL"], config["ADMIN_TEMP_PASSWORD"], ct))
            {
                LogAdminCreado();
            }
            return true;
        }
        catch (Exception e) when (e is TimeoutException or MongoConnectionException)
        {
            LogBaseNoDisponible(e.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin semilla creado: en el primer inicio de sesión debe cambiar la contraseña y configurar el MFA")]
    private partial void LogAdminCreado();

    [LoggerMessage(Level = LogLevel.Warning, Message = "MongoDB no disponible al arrancar ({Tipo}): la API sigue en modo mantenimiento y reintenta cada 30 s")]
    private partial void LogBaseNoDisponible(string tipo);

    [LoggerMessage(Level = LogLevel.Information, Message = "MongoDB disponible: índices y admin semilla listos")]
    private partial void LogBaseRecuperada();
}
