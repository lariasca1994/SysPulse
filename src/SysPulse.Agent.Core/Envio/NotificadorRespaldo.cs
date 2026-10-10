using System.Globalization;
using System.Net.Http.Json;
using SysPulse.Contracts;

namespace SysPulse.Agent.Core.Envio;

/// <summary>
/// Respaldo directo a Telegram desde el agente mientras la API no está disponible (sección 7, "Notificaciones").
/// Va a las cuentas fijas, porque el agente no conoce los destinatarios por nivel. Los destinatarios y el token
/// se actualizan en caliente desde la configuración central.
/// Solo excepciones (abrir, escalar, cerrar): la deduplicación del evaluador evita repetir avisos.
/// </summary>
public sealed class NotificadorRespaldo(HttpClient http, string? token, IReadOnlyList<string> chatIds)
{
    private string? _token = token;
    private IReadOnlyList<string> _chatIds = chatIds;

    public bool Configurado => !string.IsNullOrWhiteSpace(_token) && _chatIds.Count > 0;
    public int Destinatarios => _chatIds.Count;

    public void Actualizar(string? token, IReadOnlyList<string> chatIds)
    {
        _token = token;
        _chatIds = chatIds;
    }

    /// <summary><paramref name="equipo"/> es el nombre genérico ("Equipo portátil", "Celular"), nunca el modelo.</summary>
    public static string Formatear(EventoDto evento, string equipo)
    {
        ArgumentNullException.ThrowIfNull(evento);
        var (icono, accion) = evento.Tipo switch
        {
            "abierto" => ("🔴", "Excepción abierta"),
            "escalado" => ("🔴", "Escalada a excepción"),
            _ => ("✅", "Resuelta"),
        };
        var valor = evento.Valor.ToString("0.#", CultureInfo.InvariantCulture);
        var umbral = evento.Umbral.ToString("0.#", CultureInfo.InvariantCulture);
        var hora = evento.Ts.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var detalle = evento.Tipo == "cerrado"
            ? $"Valor actual: {evento.Metrica} = {valor} (la alerta se cierra por debajo de {umbral})"
            : $"Valor: {evento.Metrica} = {valor} (umbral de excepción: {umbral})";
        return $"{icono} SysPulse · {accion}\nEquipo: {equipo}\n{detalle}\nHora: {hora}\n(Enviado directamente por el agente: la API todavía no está disponible.)";
    }

    public static bool DebeAvisar(EventoDto evento) => evento?.Severidad == "excepcion";

    public async Task<int> EnviarAsync(string texto, CancellationToken ct)
    {
        var token = _token;
        var chats = _chatIds;
        if (string.IsNullOrWhiteSpace(token) || chats.Count == 0) return 0;
        var enviados = 0;
        foreach (var chatId in chats)
        {
            using var respuesta = await http.PostAsJsonAsync(
                new Uri($"https://api.telegram.org/bot{token}/sendMessage"),
                new { chat_id = chatId, text = texto },
                ct).ConfigureAwait(false);
            if (respuesta.IsSuccessStatusCode) enviados++;
        }
        return enviados;
    }
}
