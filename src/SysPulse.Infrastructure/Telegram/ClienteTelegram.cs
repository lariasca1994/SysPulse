using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysPulse.Infrastructure.Telegram;

/// <summary>Actualización recibida del bot (webhook en producción, getUpdates solo en desarrollo).</summary>
public sealed record ActualizacionTelegram(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] MensajeTelegram? Mensaje);

public sealed record MensajeTelegram(
    [property: JsonPropertyName("chat")] ChatTelegram Chat,
    [property: JsonPropertyName("from")] UsuarioTelegram? De,
    [property: JsonPropertyName("text")] string? Texto,
    [property: JsonPropertyName("contact")] ContactoTelegramMsg? Contacto);

public sealed record ChatTelegram([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("type")] string Tipo);

public sealed record UsuarioTelegram([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("username")] string? Usuario);

public sealed record ContactoTelegramMsg(
    [property: JsonPropertyName("phone_number")] string Telefono,
    [property: JsonPropertyName("user_id")] long? UsuarioId);

/// <summary>Cliente mínimo de la Bot API de Telegram (gratuita).</summary>
public sealed class ClienteTelegram(HttpClient http, string? token)
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public bool Configurado => !string.IsNullOrWhiteSpace(token);

    private Uri Url(string metodo) => new($"https://api.telegram.org/bot{token}/{metodo}");

    public async Task<bool> EnviarAsync(long chatId, string texto, bool pedirContacto = false, bool quitarTeclado = false, CancellationToken ct = default)
    {
        if (!Configurado) return false;
        object? teclado = pedirContacto
            ? new { keyboard = new[] { new[] { new { text = "📱 Compartir mi número", request_contact = true } } }, one_time_keyboard = true, resize_keyboard = true }
            : quitarTeclado ? new { remove_keyboard = true } : null;
        using var respuesta = await http.PostAsJsonAsync(Url("sendMessage"), new { chat_id = chatId, text = texto, reply_markup = teclado }, Json, ct).ConfigureAwait(false);
        return respuesta.IsSuccessStatusCode;
    }

    /// <summary>Solo desarrollo: Telegram no puede llamar al webhook de localhost, así que se consulta al bot.</summary>
    public async Task<IReadOnlyList<ActualizacionTelegram>> ObtenerActualizacionesAsync(long desde, int esperaSegundos, CancellationToken ct)
    {
        if (!Configurado) return [];
        var url = new Uri($"https://api.telegram.org/bot{token}/getUpdates?offset={desde}&timeout={esperaSegundos}&allowed_updates=%5B%22message%22%5D");
        var respuesta = await http.GetFromJsonAsync<RespuestaActualizaciones>(url, ct).ConfigureAwait(false);
        return respuesta?.Resultado ?? [];
    }

    public async Task<string?> NombreBotAsync(CancellationToken ct)
    {
        if (!Configurado) return null;
        var respuesta = await http.GetFromJsonAsync<RespuestaGetMe>(Url("getMe"), ct).ConfigureAwait(false);
        return respuesta?.Resultado?.Usuario;
    }

    private sealed record RespuestaActualizaciones([property: JsonPropertyName("result")] List<ActualizacionTelegram>? Resultado);

    private sealed record RespuestaGetMe([property: JsonPropertyName("result")] UsuarioTelegram? Resultado);
}
