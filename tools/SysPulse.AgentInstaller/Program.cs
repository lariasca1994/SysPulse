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

// Modo servicio: lo inicia Windows (servicio SysPulseActualizador), no una persona.
if (comando == "servicio-actualizador") return await ServicioActualizador.EjecutarAsync(args);

// Lo lanza el propio servicio actualizador tras reemplazarse: espera a que se detenga y lo vuelve a iniciar.
if (comando == "reiniciar-actualizador") return await Instalacion.ReiniciarActualizadorAsync();

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

if (comando == "auto-actualizar")
{
    // Equipos que aún usan la tarea programada: pasan al servicio actualizador (la tarea la bloquean antivirus).
    if (!args.Contains("--solo-comprobar")) Instalacion.MigrarAServicioActualizador();
    return await Actualizador.EjecutarAsync(args.Contains("--solo-comprobar"));
}

// El zip publicado trae el agente junto al instalador; en una copia de desarrollo puede estar en la subcarpeta "agent".
var origen = Path.GetFullPath(Opcion("--desde")
    ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, Instalacion.EjecutableAgente)) ? AppContext.BaseDirectory : Path.Combine(AppContext.BaseDirectory, "agent")));

// Si el agente ya está instalado, install trabaja sobre esa instalación (mismas carpetas) en vez de fallar.
var yaInstalado = Instalacion.ServicioInstalado();
if (comando == "install" && !yaInstalado) Instalacion.UsarRutas(Opcion("--destino"), Opcion("--datos"));
else Instalacion.DetectarRutasInstaladas();

if (comando is "install" or "secretos")
{
    // Validar ANTES de canjear: un código es de un solo uso y no debe gastarse en una instalación que va a fallar.
    if (comando == "install" && !File.Exists(Path.Combine(origen, Instalacion.EjecutableAgente)))
    {
        Console.Error.WriteLine($"No se encontró {Instalacion.EjecutableAgente} junto al instalador. Descomprime el zip completo y ejecuta desde esa carpeta.");
        return 1;
    }
    if (comando == "secretos" && !yaInstalado)
    {
        Console.Error.WriteLine("El agente no está instalado en este equipo. Usa: install --codigo XXXX-XXXX");
        return 1;
    }

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
    if (comando == "secretos") return Instalacion.ActualizarSecretos(secretos);
    return yaInstalado ? Instalacion.Reinstalar(origen, secretos) : Instalacion.Instalar(origen, secretos);
}

try
{
    return comando == "update" ? Instalacion.Actualizar(origen) : Instalacion.Desinstalar(args.Contains("--purgar"));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"No se pudo completar: {ex.Message}");
    Console.Error.WriteLine("El agente anterior sigue funcionando. Cierra programas que usen la carpeta del agente (o reinicia) y vuelve a intentarlo.");
    return 1;
}
