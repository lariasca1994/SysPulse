using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysPulse.Agent.Core.Configuracion;

/// <summary>
/// Secretos del agente de Windows cifrados con DPAPI (ámbito de máquina) en %ProgramData%\SysPulse\agent.json.
/// El instalador restringe el archivo a SYSTEM, Administradores y NT SERVICE\SysPulseAgent.
/// Solo se usa en Windows; Android guarda sus secretos en SecureStorage.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AlmacenSecretosWindows
{
    public const string NombreArchivo = "agent.json";

    /// <summary>Variables que el agente necesita (las mismas del .env).</summary>
    public static readonly IReadOnlyList<string> Claves =
        ["MONGO_URI", "API_URL", "AGENT_API_KEY", "TELEGRAM_BOT_TOKEN", "TELEGRAM_FIXED_CHAT_IDS"];

    // Entropía adicional: otro programa de la máquina no puede descifrar el archivo sin conocerla.
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("SysPulse.Agent.v1");

    private sealed record Archivo(int Version, string Datos);

    public static void Guardar(string ruta, IReadOnlyDictionary<string, string> secretos)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(secretos);
        var cifrado = ProtectedData.Protect(json, Entropia, DataProtectionScope.LocalMachine);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(new Archivo(1, Convert.ToBase64String(cifrado))), new UTF8Encoding(false));
    }

    public static Dictionary<string, string>? Leer(string ruta)
    {
        if (!File.Exists(ruta)) return null;
        var archivo = JsonSerializer.Deserialize<Archivo>(File.ReadAllText(ruta, Encoding.UTF8));
        if (archivo is null) return null;
        var json = ProtectedData.Unprotect(Convert.FromBase64String(archivo.Datos), Entropia, DataProtectionScope.LocalMachine);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    /// <summary>Carga los secretos como variables de entorno del proceso (sin sobrescribir las existentes).</summary>
    public static bool CargarEnEntorno(string ruta)
    {
        var secretos = Leer(ruta);
        if (secretos is null) return false;
        foreach (var (clave, valor) in secretos)
        {
            if (Environment.GetEnvironmentVariable(clave) is null) Environment.SetEnvironmentVariable(clave, valor);
        }
        return true;
    }
}
