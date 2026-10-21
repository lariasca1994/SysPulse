namespace SysPulse.Contracts;

/// <summary>
/// Alta de un equipo con un código de un solo uso (<c>POST /api/agentes/alta</c>). Un técnico N2 o N3 o el admin genera
/// el código en la web; el instalador de Windows o la app de Android lo canjean y reciben lo necesario para reportar.
/// </summary>
/// <param name="Codigo">Código tal como lo escribe la persona (ej. "K7QM-4XPA"; mayúsculas, guion y espacios opcionales).</param>
/// <param name="TipoHost">"windows" o "android".</param>
public sealed record SolicitudAlta(string Codigo, string TipoHost);

/// <summary>
/// Configuración que recibe el equipo al canjear el código. Se guarda cifrada en el equipo (DPAPI o Android Keystore).
/// <paramref name="MongoUri"/> es del usuario de base exclusivo de los agentes (solo sus colecciones): el equipo nunca
/// recibe la cadena de la API ni el token de Telegram.
/// </summary>
public sealed record RespuestaAlta(
    string MongoUri,
    string ApiUrl,
    string AgentApiKey);
