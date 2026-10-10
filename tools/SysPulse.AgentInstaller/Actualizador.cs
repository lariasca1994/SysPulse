using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using SysPulse.Agent.Core.Actualizaciones;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.Agent.Core.Envio;

namespace SysPulse.AgentInstaller;

/// <summary>
/// Actualización automática del agente de Windows. La ejecuta cada hora el servicio "SysPulse Actualizador"
/// (LocalSystem; el agente corre sin privilegios y no puede reemplazarse a sí mismo):
/// 1. lee en la configuración central la versión publicada (URL de GitHub Releases + huella SHA-256);
/// 2. si es más nueva que la instalada, la descarga a la carpeta temporal de SYSTEM y verifica origen y huella;
/// 3. detiene el servicio, reemplaza los binarios (incluido este instalador) y lo vuelve a iniciar.
/// Si algo falla, la versión instalada sigue funcionando y se reintenta en la próxima hora.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Actualizador
{
    public const string Tarea = @"SysPulse\Actualizador";
    private const long MaxLog = 1024 * 1024;

    public static async Task<int> EjecutarAsync(bool soloComprobar)
    {
        Instalacion.DetectarRutasInstaladas();
        var log = Path.Combine(Instalacion.CarpetaDatos, "logs", "actualizador.log");
        try
        {
            LimpiarRestos();
            if (!AlmacenSecretosWindows.CargarEnEntorno(Path.Combine(Instalacion.CarpetaDatos, AlmacenSecretosWindows.NombreArchivo))
                || Environment.GetEnvironmentVariable("MONGO_URI") is not { Length: > 0 } mongo)
            {
                return Registrar(log, "No hay secretos del agente (MONGO_URI): ¿está instalado?", 1);
            }

            var config = await new PublicadorAtlas(mongo).LeerConfiguracionAsync(CancellationToken.None);
            var paquete = config?.ActualizacionWindows;
            var instalada = VersionInstalada();
            if (paquete is null) return Registrar(log, $"Sin versión publicada (instalada {instalada}).", 0, detalle: soloComprobar);
            if (!VerificadorPaquete.EsMasNueva(paquete.Version, instalada))
                return Registrar(log, $"Al día: instalada {instalada}, publicada {paquete.Version}.", 0, detalle: soloComprobar);

            Registrar(log, $"Nueva versión {paquete.Version} (instalada {instalada}): descargando.", 0);
            var trabajo = Path.Combine(Path.GetTempPath(), "SysPulse-actualizacion");
            if (Directory.Exists(trabajo)) Directory.Delete(trabajo, recursive: true);
            var zip = Path.Combine(trabajo, "paquete.zip");
            await VerificadorPaquete.DescargarAsync(paquete, zip, CancellationToken.None);

            var carpeta = Path.Combine(trabajo, "agente");
            ZipFile.ExtractToDirectory(zip, carpeta);
            var exe = Path.Combine(carpeta, Instalacion.EjecutableAgente);
            if (!File.Exists(exe)) return Registrar(log, "El paquete no contiene el agente: se descarta.", 1);
            var versionPaquete = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
            if (VerificadorPaquete.EsMasNueva(paquete.Version, versionPaquete) || VerificadorPaquete.EsMasNueva(versionPaquete, paquete.Version))
                return Registrar(log, $"El paquete dice {versionPaquete} pero se publicó como {paquete.Version}: se descarta.", 1);

            if (soloComprobar) return Registrar(log, $"Paquete {paquete.Version} descargado y verificado (modo comprobación: no se instala).", 0);

            var codigo = Instalacion.Actualizar(carpeta);
            Directory.Delete(trabajo, recursive: true);
            var estado = Instalacion.Estado();
            return Registrar(log, codigo == 0 ? $"Actualizado a {paquete.Version}. Servicio: {estado}." : $"La actualización falló (código {codigo}). Servicio: {estado}.", codigo);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException or InvalidDataException or MongoDB.Driver.MongoException or TimeoutException)
        {
            return Registrar(log, $"Error: {ex.GetType().Name}: {ex.Message} (se reintenta en la próxima hora).", 1);
        }
    }

    public static string VersionInstalada()
    {
        var exe = Path.Combine(Instalacion.CarpetaPrograma, Instalacion.EjecutableAgente);
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "0.0.0" : "0.0.0";
    }

    /// <summary>El instalador en uso se renombra a .old al reemplazarse; se borra en la siguiente ejecución.</summary>
    private static void LimpiarRestos()
    {
        if (!Directory.Exists(Instalacion.CarpetaPrograma)) return;
        foreach (var viejo in Directory.EnumerateFiles(Instalacion.CarpetaPrograma, "*.old"))
        {
            try
            {
                File.Delete(viejo);
            }
            catch (IOException)
            {
                // Aún en uso: se intenta en la próxima ejecución.
            }
            catch (UnauthorizedAccessException)
            {
                // Ídem.
            }
        }
    }

    /// <summary>Escribe en el log (con un tope de 1 MB) y en la consola. Las comprobaciones sin cambios solo van a consola.</summary>
    private static int Registrar(string log, string mensaje, int codigo, bool detalle = true)
    {
        var linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {mensaje}";
        Console.WriteLine(linea);
        if (!detalle && codigo == 0) return codigo;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            if (File.Exists(log) && new FileInfo(log).Length > MaxLog) File.Delete(log);
            File.AppendAllText(log, linea + Environment.NewLine, System.Text.Encoding.UTF8);
        }
        catch (IOException)
        {
            // Sin log no se detiene la actualización.
        }
        catch (UnauthorizedAccessException)
        {
            // Ídem (ej. ejecutado sin permisos sobre la carpeta de datos).
        }
        return codigo;
    }
}
