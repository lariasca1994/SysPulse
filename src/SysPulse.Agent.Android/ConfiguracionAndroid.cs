using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Android;

/// <summary>
/// Configuración del agente Android. Los secretos llegan una vez en un archivo temporal (adb push a la carpeta
/// de la app), se validan, se guardan cifrados en SecureStorage (Android Keystore) y el archivo se borra.
/// Formato: líneas CLAVE=valor (o una sola línea mongodb://… por compatibilidad con la prueba técnica).
/// </summary>
public static class ConfiguracionAndroid
{
    public static readonly string[] ArchivosImportables = ["configuracion.txt", "prueba.txt"];
    public static readonly string[] Claves = ["MONGO_URI_ANDROID", "API_URL", "AGENT_API_KEY", "TELEGRAM_BOT_TOKEN", "TELEGRAM_FIXED_CHAT_IDS"];

    private const string PreferenciaMonitoreoActivo = "monitoreo_activo";

    public static string CarpetaImportacion =>
        global::Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;

    /// <summary>Si existe un archivo de configuración, lo importa y lo borra. Devuelve un mensaje para la pantalla o null.</summary>
    public static async Task<string?> ImportarSiExisteAsync()
    {
        foreach (var nombre in ArchivosImportables)
        {
            var ruta = Path.Combine(CarpetaImportacion, nombre);
            if (!File.Exists(ruta)) continue;

            var lineas = File.ReadAllLines(ruta).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            File.Delete(ruta);

            var valores = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var linea in lineas)
            {
                if (linea.StartsWith(CadenaMongo.PrefijoEstandar, StringComparison.Ordinal) || linea.StartsWith(CadenaMongo.PrefijoSrv, StringComparison.Ordinal))
                {
                    valores["MONGO_URI_ANDROID"] = linea;
                    continue;
                }
                var igual = linea.IndexOf('=', StringComparison.Ordinal);
                if (igual <= 0) continue;
                var clave = linea[..igual].Trim();
                if (Claves.Contains(clave)) valores[clave] = linea[(igual + 1)..].Trim().Trim('"');
            }

            if (valores.TryGetValue("MONGO_URI_ANDROID", out var mongo) && CadenaMongo.Validar(mongo, esAndroid: true) is { } error)
            {
                return $"Configuración rechazada: {error}";
            }

            foreach (var (clave, valor) in valores) await SecureStorage.Default.SetAsync(clave, valor);
            return $"Configuración importada ({valores.Count} valores) y archivo borrado.";
        }
        return null;
    }

    public static async Task<bool> EstaConfiguradoAsync() => await SecureStorage.Default.GetAsync("MONGO_URI_ANDROID") is not null;

    public static async Task<OpcionesAgente> CargarOpcionesAsync()
    {
        var chats = await SecureStorage.Default.GetAsync("TELEGRAM_FIXED_CHAT_IDS") ?? "";
        return new OpcionesAgente
        {
            HostId = IdHost(),
            TipoHost = "android",
            MongoUri = await SecureStorage.Default.GetAsync("MONGO_URI_ANDROID"),
            ApiUrl = await SecureStorage.Default.GetAsync("API_URL"),
            AgentApiKey = await SecureStorage.Default.GetAsync("AGENT_API_KEY"),
            TelegramBotToken = await SecureStorage.Default.GetAsync("TELEGRAM_BOT_TOKEN"),
            TelegramChatIdsRespaldo = chats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            CarpetaDatos = FileSystem.AppDataDirectory,
            Umbrales = UmbralesPorDefecto.Android,
        };
    }

    /// <summary>Recordar si el usuario dejó el monitoreo encendido (para reanudarlo al reiniciar el teléfono).</summary>
    public static bool MonitoreoActivo
    {
        get => Preferences.Default.Get(PreferenciaMonitoreoActivo, false);
        set => Preferences.Default.Set(PreferenciaMonitoreoActivo, value);
    }

    /// <summary>Id anónimo: "and-" + hash del ANDROID_ID (único por app y dispositivo). Nunca el nombre del teléfono.</summary>
    private static string IdHost()
    {
        var androidId = global::Android.Provider.Settings.Secure.GetString(
            global::Android.App.Application.Context.ContentResolver, global::Android.Provider.Settings.Secure.AndroidId) ?? DeviceInfo.Current.Model;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("SysPulse|" + androidId));
        return "and-" + Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }
}
