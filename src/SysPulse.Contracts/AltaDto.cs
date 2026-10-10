namespace SysPulse.Contracts;

/// <summary>
/// Alta de un equipo con un código de un solo uso (<c>POST /api/agentes/alta</c>). El admin genera el código en la web;
/// el instalador de Windows o la app de Android lo canjean y reciben lo necesario para empezar a reportar.
/// </summary>
/// <param name="Codigo">Código tal como lo escribe la persona (ej. "K7QM-4XPA"; mayúsculas, guion y espacios opcionales).</param>
/// <param name="TipoHost">"windows" o "android".</param>
public sealed record SolicitudAlta(string Codigo, string TipoHost);

/// <summary>Configuración que recibe el equipo al canjear el código. Se guarda cifrada en el equipo (DPAPI o Android Keystore).</summary>
public sealed record RespuestaAlta(
    string MongoUri,
    string ApiUrl,
    string AgentApiKey,
    string? TelegramBotToken,
    IReadOnlyList<string> TelegramChatIds);
