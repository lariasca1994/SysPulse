// Instalador PÚBLICO del agente de Windows (sección 13). No contiene secretos ni funciones de administración:
// solo instala, actualiza o desinstala el agente en este equipo. Los datos de conexión llegan con un código
// de alta de un solo uso que genera el admin en la web (Administración → Equipos).

using SysPulse.AgentInstaller;

const string Uso = """
    Instalador del agente SysPulse (PowerShell como administrador)

      install --codigo <XXXX-XXXX> [--destino <carpeta>] [--datos <carpeta>]
                Instala el agente en este equipo. El código lo genera el admin en SysPulse
                (Administración → Equipos → Agregar equipo); sirve una vez y vence en 24 horas.
                --destino / --datos: instalar fuera de C: (ej. --destino D:\SysPulse\Agent --datos D:\SysPulse\Datos).
      update    [--desde <carpeta>]   Reemplaza el agente por el de esta carpeta y reinicia el servicio.
                                      (Normalmente no hace falta: el agente se actualiza solo cada hora.)
      secretos  --codigo <XXXX-XXXX>  Vuelve a vincular el equipo con un código nuevo y reinicia el servicio.
      uninstall [--purgar]            Elimina el servicio (--purgar borra también los datos locales).
      estado                          Muestra si el servicio está instalado y corriendo.
    """;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var comando = args.Length > 0 ? args[0] : "";
string? Opcion(string nombre)
{
    var i = Array.IndexOf(args, nombre);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (comando == "estado")
{
    Instalacion.DetectarRutasInstaladas();
    Console.WriteLine($"Servicio {Instalacion.Servicio}: {Instalacion.Estado()} · agente v{Actualizador.VersionInstalada()}");
    return 0;
}

if (comando is not ("install" or "update" or "uninstall" or "secretos" or "auto-actualizar"))
{
    Console.WriteLine(Uso);
    return comando.Length == 0 ? 0 : 1;
}

if (!Instalacion.EsAdministrador())
{
    Console.Error.WriteLine("Este comando requiere PowerShell abierto como administrador (clic derecho → Ejecutar como administrador).");
    return 1;
}

if (comando == "auto-actualizar") return await Actualizador.EjecutarAsync(args.Contains("--solo-comprobar"));

// El zip publicado trae el agente junto al instalador; en una copia de desarrollo puede estar en la subcarpeta "agent".
var origen = Path.GetFullPath(Opcion("--desde")
    ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, Instalacion.EjecutableAgente)) ? AppContext.BaseDirectory : Path.Combine(AppContext.BaseDirectory, "agent")));

if (comando == "install") Instalacion.UsarRutas(Opcion("--destino"), Opcion("--datos"));
else Instalacion.DetectarRutasInstaladas();

if (comando is "install" or "secretos")
{
    // Instalación con código de alta (lo normal) o, solo en desarrollo, con un .env local (--env).
    Dictionary<string, string>? secretos;
    if (Opcion("--codigo") is { } codigo) secretos = await Alta.CanjearAsync(Opcion("--api") ?? Alta.ApiPorDefecto, codigo);
    else if (Opcion("--env") is { } env) secretos = Instalacion.LeerEnv(env);
    else
    {
        Console.Error.WriteLine("Indica el código de alta: --codigo XXXX-XXXX (lo genera el admin en Administración → Equipos).");
        return 1;
    }
    if (secretos is null) return 1;
    return comando == "install" ? Instalacion.Instalar(origen, secretos) : Instalacion.ActualizarSecretos(secretos);
}

return comando == "update" ? Instalacion.Actualizar(origen) : Instalacion.Desinstalar(args.Contains("--purgar"));
