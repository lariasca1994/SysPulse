using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Seguridad;
using SysPulse.Infrastructure.Telegram;

namespace SysPulse.Api.Endpoints;

/// <summary>
/// Webhook del bot (producción). Telegram envía el secreto configurado en la cabecera
/// X-Telegram-Bot-Api-Secret-Token; sin él (o distinto) la petición se rechaza.
/// </summary>
public static class TelegramEndpoints
{
    public static void MapTelegram(this IEndpointRouteBuilder app, string? secreto)
    {
        app.MapPost("/api/telegram/webhook", async (HttpRequest req, ActualizacionTelegram actualizacion, ServicioCuentas cuentas, CancellationToken ct) =>
        {
            var recibido = req.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (string.IsNullOrEmpty(secreto) || !Cifrado.IgualesTiempoConstante(recibido, secreto)) return Results.Unauthorized();
            await cuentas.ProcesarTelegramAsync(actualizacion, ct);
            return Results.Ok();
        });
    }
}
