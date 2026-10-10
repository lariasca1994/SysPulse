using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

/// <summary>Al arrancar: índices de Mongo y admin semilla (si no existe ninguno).</summary>
public sealed partial class InicioApi(ContextoMongo db, IServiceScopeFactory scopes, IConfiguration config, ILogger<InicioApi> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await db.AsegurarIndicesAsync(cancellationToken);
        using var scope = scopes.CreateScope();
        var cuentas = scope.ServiceProvider.GetRequiredService<ServicioCuentas>();
        if (await cuentas.SembrarAdminAsync(config["ADMIN_EMAIL"], config["ADMIN_TEMP_PASSWORD"], cancellationToken))
        {
            LogAdminCreado();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin semilla creado: en el primer inicio de sesión debe cambiar la contraseña y configurar el MFA")]
    private partial void LogAdminCreado();
}
