using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SysPulse.Infrastructure.Correo;

public sealed record OpcionesCorreo(string? ApiKey, string? Remitente, string NombreRemitente, bool EnviarDeVerdad);

/// <summary>
/// Correo transaccional por la API HTTP de Brevo (Render gratuito bloquea SMTP).
/// Mientras <see cref="OpcionesCorreo.EnviarDeVerdad"/> sea falso (desarrollo) solo se registra en el log:
/// las pruebas de correo van al final para no consumir la cuota de 300/día.
/// </summary>
public sealed partial class ServicioCorreo(HttpClient http, OpcionesCorreo opciones, ILogger<ServicioCorreo> logger)
{
    public bool EnviaDeVerdad => opciones.EnviarDeVerdad && !string.IsNullOrWhiteSpace(opciones.ApiKey) && !string.IsNullOrWhiteSpace(opciones.Remitente);

    public async Task<bool> EnviarAsync(string para, string asunto, string html, string texto, CancellationToken ct)
    {
        if (!EnviaDeVerdad)
        {
            LogSimulado(para, asunto);
            return true;
        }

        using var solicitud = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.brevo.com/v3/smtp/email"))
        {
            Content = JsonContent.Create(new
            {
                sender = new { email = opciones.Remitente, name = opciones.NombreRemitente },
                to = new[] { new { email = para } },
                subject = asunto,
                htmlContent = html,
                textContent = texto,
            }),
        };
        solicitud.Headers.Add("api-key", opciones.ApiKey);
        using var respuesta = await http.SendAsync(solicitud, ct).ConfigureAwait(false);
        if (!respuesta.IsSuccessStatusCode) LogFallo((int)respuesta.StatusCode, asunto);
        return respuesta.IsSuccessStatusCode;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Correo SIMULADO (modo desarrollo, no se usa la cuota de Brevo): para {Para}, asunto \"{Asunto}\"")]
    private partial void LogSimulado(string para, string asunto);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Brevo rechazó el correo \"{Asunto}\" (HTTP {Estado})")]
    private partial void LogFallo(int estado, string asunto);
}
