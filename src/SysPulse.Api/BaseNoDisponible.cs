using Microsoft.AspNetCore.Diagnostics;
using MongoDB.Driver;

namespace SysPulse.Api;

/// <summary>
/// MongoDB Atlas apagado o inalcanzable: responde 503 con marca de mantenimiento
/// (el panel muestra su pantalla de mantenimiento) en vez de un 500 genérico.
/// Cualquier otra excepción sigue al manejador por defecto (ProblemDetails 500).
/// </summary>
public sealed partial class BaseNoDisponible(ILogger<BaseNoDisponible> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!EsCaidaDeBase(exception)) return false;

        LogNoDisponible(log, exception.GetType().Name);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "300";
        await httpContext.Response.WriteAsJsonAsync(
            new { title = "Servicio en mantenimiento", detail = "Servicio en mantenimiento", mantenimiento = true },
            cancellationToken);
        return true;
    }

    private static bool EsCaidaDeBase(Exception? excepcion) => excepcion switch
    {
        null => false,
        MongoConnectionException => true,
        // El driver lanza TimeoutException cuando no logra seleccionar un servidor (Atlas caído).
        TimeoutException t when t.Message.Contains("selecting a server", StringComparison.OrdinalIgnoreCase) => true,
        _ => EsCaidaDeBase(excepcion.InnerException),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "MongoDB no disponible: {Tipo}")]
    private static partial void LogNoDisponible(ILogger logger, string tipo);
}
