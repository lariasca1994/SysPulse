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
    public static readonly string[] Claves = ["MONGO_URI_ANDROID", "API_URL", "AGENT_API_KEY"];

    /// <summary>Claves que guardaban versiones anteriores y ya no deben quedar en el teléfono.</summary>
    private static readonly string[] ClavesRetiradas = ["TELEGRAM_BOT_TOKEN", "TELEGRAM_FIXED_CHAT_IDS"];

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

    /// <summary>URL pública de la API (no es secreta). La app no lleva ningún secreto: llegan al canjear el código.</summary>
    public const string ApiPorDefecto = "https://syspulse-api-7dj0.onrender.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>
    /// Vincula el celular con un código de alta de un solo uso (lo genera un técnico N2 o N3 o el admin en la web).
    /// Lo recibido se guarda cifrado en SecureStorage (Android Keystore). Devuelve null si salió bien o el error.
    /// </summary>
    public static async Task<string?> VincularAsync(string codigo)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await Http.PostAsync(new Uri($"{ApiPorDefecto}/api/agentes/alta"),
                System.Net.Http.Json.JsonContent.Create(new SysPulse.Contracts.SolicitudAlta(codigo, "android")));
        }
        catch (HttpRequestException)
        {
            return "Sin conexión con SysPulse. Revisa el Wi-Fi o los datos e inténtalo de nuevo.";
        }
        catch (TaskCanceledException)
        {
            return "SysPulse no respondió a tiempo (puede estar despertando). Inténtalo en un minuto.";
        }

        using (respuesta)
        {
            if ((int)respuesta.StatusCode == 429) return "Demasiados intentos. Espera un minuto.";
            if ((int)respuesta.StatusCode == 503) return "El alta de equipos no está disponible en este momento. Avisa al administrador.";
            if (!respuesta.IsSuccessStatusCode) return "El código no es válido, ya se usó o venció. Pide uno nuevo a un técnico N2 o N3.";
            var r = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<SysPulse.Contracts.RespuestaAlta>(respuesta.Content);
            if (r is null) return "Respuesta inesperada de SysPulse.";
            if (CadenaMongo.Validar(r.MongoUri, esAndroid: true) is { } error) return $"Configuración rechazada: {error}";

            await SecureStorage.Default.SetAsync("MONGO_URI_ANDROID", r.MongoUri);
            await SecureStorage.Default.SetAsync("API_URL", r.ApiUrl);
            await SecureStorage.Default.SetAsync("AGENT_API_KEY", r.AgentApiKey);
            return null;
        }
    }

    public static async Task<OpcionesAgente> CargarOpcionesAsync()
    {
        foreach (var clave in ClavesRetiradas) SecureStorage.Default.Remove(clave);
        return new OpcionesAgente
        {
            HostId = IdHost(),
            TipoHost = "android",
            MongoUri = await SecureStorage.Default.GetAsync("MONGO_URI_ANDROID"),
            ApiUrl = await SecureStorage.Default.GetAsync("API_URL"),
            AgentApiKey = await SecureStorage.Default.GetAsync("AGENT_API_KEY"),
            // Envío por HTTPS (pila de red de Java) en lugar de conectarse directo a Atlas: las conexiones TLS
            // nativas de .NET en Android pueden cerrar la app al cambiar o perder la red.
            IngestaUrl = IngestaUrl,
            IntervaloEstado = TimeSpan.FromMinutes(2),
            CarpetaDatos = FileSystem.AppDataDirectory,
            Umbrales = UmbralesPorDefecto.Android,
        };
    }

    /// <summary>Ruta de ingesta de los agentes (dirección pública de la web de SysPulse; no es un secreto).</summary>
    public const string IngestaUrl = "https://syspulse.vercel.app/";

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
