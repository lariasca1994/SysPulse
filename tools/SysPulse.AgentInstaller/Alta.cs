using System.Net.Http.Json;
using SysPulse.Contracts;

namespace SysPulse.AgentInstaller;

/// <summary>
/// Canje del código de alta: el instalador no lleva secretos; los recibe de la API con un código de un solo uso
/// (24 h) que genera el admin en la web, y se guardan cifrados con DPAPI en el equipo.
/// </summary>
internal static class Alta
{
    /// <summary>URL pública de la API (no es secreta). Se puede cambiar con --api.</summary>
    public const string ApiPorDefecto = "https://syspulse-api-7dj0.onrender.com";

    public static async Task<Dictionary<string, string>?> CanjearAsync(string api, string codigo)
    {
        // Render gratuito puede tardar ~1 minuto en despertar.
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        Console.WriteLine("• Validando el código de alta con SysPulse (puede tardar hasta un minuto si el servidor estaba dormido)…");
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.PostAsJsonAsync(new Uri($"{api.TrimEnd('/')}/api/agentes/alta"), new SolicitudAlta(codigo, "windows"));
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: no se pudo conectar con SysPulse ({ex.Message}). Revisa la conexión a internet.");
            return null;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("Error: SysPulse no respondió a tiempo. Inténtalo de nuevo en un minuto.");
            return null;
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                Console.Error.WriteLine((int)respuesta.StatusCode == 429
                    ? "Error: demasiados intentos. Espera un minuto."
                    : "Error: el código no es válido, ya se usó o venció. Genera uno nuevo en Administración → Equipos.");
                return null;
            }
            var r = await respuesta.Content.ReadFromJsonAsync<RespuestaAlta>();
            if (r is null) return null;
            var secretos = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MONGO_URI"] = r.MongoUri,
                ["API_URL"] = r.ApiUrl,
                ["AGENT_API_KEY"] = r.AgentApiKey,
                ["TELEGRAM_FIXED_CHAT_IDS"] = string.Join(',', r.TelegramChatIds),
            };
            if (!string.IsNullOrWhiteSpace(r.TelegramBotToken)) secretos["TELEGRAM_BOT_TOKEN"] = r.TelegramBotToken;
            Console.WriteLine("   código aceptado (los datos recibidos no se muestran)");
            return secretos;
        }
    }
}
