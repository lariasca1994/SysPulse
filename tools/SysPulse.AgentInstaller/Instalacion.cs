using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.AgentInstaller;

/// <summary>
/// Instala el agente como servicio de Windows (sección 13):
/// - binarios en %ProgramFiles%\SysPulse\Agent; datos, logs y secretos en %ProgramData%\SysPulse;
/// - servicio SysPulseAgent, inicio automático retrasado, reinicio si falla;
/// - cuenta virtual NT SERVICE\SysPulseAgent (sin privilegios de administrador), miembro solo de
///   "Performance Monitor Users" (S-1-5-32-558) y "Event Log Readers" (S-1-5-32-573);
/// - secretos cifrados con DPAPI y archivo restringido a SYSTEM, Administradores y la cuenta del servicio.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Instalacion
{
    public const string Servicio = "SysPulseAgent";
    public const string Cuenta = @"NT SERVICE\SysPulseAgent";
    public const string EjecutableAgente = "SysPulse.Agent.exe";
    private const string Ejecutable = EjecutableAgente;
    private const string EjecutableInstalador = "SysPulse.AgentInstaller.exe";

    // Por defecto en C:; con --destino y --datos se instala en otra unidad (ej. D:\SysPulse).
    public static string CarpetaPrograma { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SysPulse", "Agent");
    public static string CarpetaDatos { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SysPulse");

    public static void UsarRutas(string? destino, string? datos)
    {
        if (!string.IsNullOrWhiteSpace(destino)) CarpetaPrograma = Path.GetFullPath(destino).TrimEnd('\\');
        if (!string.IsNullOrWhiteSpace(datos)) CarpetaDatos = Path.GetFullPath(datos).TrimEnd('\\');
    }

    /// <summary>Para update/uninstall: toma las rutas reales del servicio instalado (sc qc), estén en C: o en otra unidad.</summary>
    public static void DetectarRutasInstaladas()
    {
        var (codigo, salida) = Ejecutar("sc.exe", $"qc {Servicio}");
        if (codigo != 0) return;
        var linea = salida.Split('\n').FirstOrDefault(l => l.Contains(Ejecutable, StringComparison.OrdinalIgnoreCase));
        if (linea is null) return;
        var exe = Regex.Match(linea, @"""?([A-Za-z]:\\[^""]*?SysPulse\.Agent\.exe)""?", RegexOptions.IgnoreCase);
        if (exe.Success) CarpetaPrograma = Path.GetDirectoryName(exe.Groups[1].Value)!;
        var datos = Regex.Match(linea, @"--Agente:CarpetaDatos=""?([^""]+?)""?\s*$");
        if (datos.Success) CarpetaDatos = datos.Groups[1].Value.Trim();
    }

    public static bool EsAdministrador()
    {
        using var identidad = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Instalar(string origen, IReadOnlyDictionary<string, string> secretos)
    {
        if (!File.Exists(Path.Combine(origen, Ejecutable))) return Error($"No se encontró {Ejecutable} en {origen}.");
        if (!secretos.ContainsKey("MONGO_URI")) return Error("Faltan los datos de conexión (canjea un código de alta).");
        if (ServicioExiste()) return Error("El servicio ya existe. Use 'update' para actualizar o 'uninstall' primero.");

        Paso("Copiando el agente a " + CarpetaPrograma);
        CopiarCarpeta(origen, CarpetaPrograma);

        ProtegerCarpetaPrograma();

        Paso("Creando el servicio " + Servicio);
        var binario = Path.Combine(CarpetaPrograma, Ejecutable);
        // La carpeta de datos va como argumento del servicio: el agente la lee al arrancar (Agente:CarpetaDatos).
        var binPath = $"\"{binario}\" --Agente:CarpetaDatos=\"{CarpetaDatos}\"";
        if (Ejecutar("sc.exe", ["create", Servicio, "binPath=", binPath, "start=", "delayed-auto", "obj=", Cuenta, "DisplayName=", "SysPulse Agent"]).Codigo != 0)
        {
            return Error("No se pudo crear el servicio.");
        }
        Sc($"description {Servicio} \"SysPulse: monitoreo del equipo (métricas, red, Event Log) y envío a MongoDB Atlas.\"");
        Sc($"failure {Servicio} reset= 86400 actions= restart/60000/restart/60000/restart/300000");

        Paso("Agregando la cuenta del servicio a los grupos de lectura de rendimiento y Event Log");
        AgregarAGrupo("S-1-5-32-558");
        AgregarAGrupo("S-1-5-32-573");

        Paso("Guardando los secretos cifrados (DPAPI)");
        GuardarSecretos(secretos);
        ProtegerCarpetaDatos();

        CopiarInstalador();
        RegistrarTareaActualizacion();

        Paso("Iniciando el servicio");
        Sc($"start {Servicio}");
        Console.WriteLine();
        Console.WriteLine($"Listo. Estado: {Estado()}. Logs en {Path.Combine(CarpetaDatos, "logs")}");
        return 0;
    }

    public static int Actualizar(string origen)
    {
        if (!File.Exists(Path.Combine(origen, Ejecutable))) return Error($"No se encontró {Ejecutable} en {origen}.");
        if (!ServicioExiste()) return Error("El servicio no está instalado. Use 'install'.");

        Paso("Deteniendo el servicio");
        Detener();
        try
        {
            Paso("Reemplazando los binarios");
            CopiarCarpeta(origen, CarpetaPrograma);
            CopiarInstalador();
        }
        finally
        {
            // Pase lo que pase con la copia, el servicio vuelve a arrancar (con la versión nueva o la anterior).
            Paso("Iniciando el servicio");
            Sc($"start {Servicio}");
        }
        RegistrarTareaActualizacion();
        Console.WriteLine($"Listo. Estado: {Estado()}.");
        return 0;
    }

    /// <summary>Vuelve a cifrar los secretos desde un .env (ej. tras cambiar un token) y reinicia el servicio.</summary>
    public static int ActualizarSecretos(IReadOnlyDictionary<string, string> secretos)
    {
        if (!secretos.ContainsKey("MONGO_URI")) return Error("Faltan los datos de conexión.");
        if (!ServicioExiste()) return Error("El servicio no está instalado. Use 'install'.");

        Paso("Guardando los secretos cifrados (DPAPI) en " + CarpetaDatos);
        GuardarSecretos(secretos);
        Paso("Reiniciando el servicio");
        Detener();
        Sc($"start {Servicio}");
        Console.WriteLine($"Listo. Estado: {Estado()}.");
        return 0;
    }

    public static int Desinstalar(bool purgarDatos)
    {
        Paso("Eliminando la tarea de actualización automática");
        Ejecutar("schtasks.exe", ["/Delete", "/TN", Actualizador.Tarea, "/F"]);
        if (ServicioExiste())
        {
            Paso("Deteniendo y eliminando el servicio");
            Detener();
            Sc($"delete {Servicio}");
        }
        if (Directory.Exists(CarpetaPrograma))
        {
            Paso("Eliminando los binarios");
            Directory.Delete(CarpetaPrograma, recursive: true);
        }
        if (purgarDatos && Directory.Exists(CarpetaDatos))
        {
            Paso("Eliminando datos, logs y secretos (" + CarpetaDatos + ")");
            Directory.Delete(CarpetaDatos, recursive: true);
        }
        else if (Directory.Exists(CarpetaDatos))
        {
            Console.WriteLine($"Se conservan datos y secretos en {CarpetaDatos} (use --purgar para borrarlos).");
        }
        Console.WriteLine("Desinstalado.");
        return 0;
    }

    public static string Estado()
    {
        var (codigo, salida) = Ejecutar("sc.exe", $"query {Servicio}");
        if (codigo != 0) return "no instalado";
        var linea = salida.Split('\n').FirstOrDefault(l => l.Contains("STATE", StringComparison.OrdinalIgnoreCase) || l.Contains("ESTADO", StringComparison.OrdinalIgnoreCase));
        return linea?.Split(':').Last().Trim() ?? "desconocido";
    }

    /// <summary>Deja una copia del instalador junto al agente: es el que ejecuta la tarea de actualización.</summary>
    private static void CopiarInstalador()
    {
        var actual = Environment.ProcessPath;
        var destino = Path.Combine(CarpetaPrograma, EjecutableInstalador);
        if (actual is null || string.Equals(Path.GetFullPath(actual), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(destino) && string.Equals(Path.GetFullPath(destino), actual, StringComparison.OrdinalIgnoreCase)) return;
        File.Copy(actual, destino, overwrite: true);
    }

    /// <summary>
    /// Tarea programada que corre cada hora como SYSTEM: "SysPulse.AgentInstaller.exe auto-actualizar".
    /// Se crea (o reemplaza) en cada instalación o actualización manual; desde entonces todo es automático.
    /// </summary>
    private static void RegistrarTareaActualizacion()
    {
        Paso("Programando la actualización automática (cada hora, como SYSTEM)");
        var exe = Path.Combine(CarpetaPrograma, EjecutableInstalador);
        var (codigo, salida) = Ejecutar("schtasks.exe",
            ["/Create", "/TN", Actualizador.Tarea, "/TR", $"\"{exe}\" auto-actualizar", "/SC", "HOURLY", "/RU", "SYSTEM", "/RL", "HIGHEST", "/F"]);
        Console.WriteLine(codigo == 0 ? "   tarea creada: " + Actualizador.Tarea : "   no se pudo crear la tarea: " + salida.Trim());
    }

    /// <summary>Lee de un .env solo las claves que usa el agente (instalación de desarrollo).</summary>
    public static Dictionary<string, string> LeerEnv(string rutaEnv)
    {
        var lineas = File.Exists(rutaEnv) ? File.ReadAllLines(rutaEnv, System.Text.Encoding.UTF8) : [];
        var secretos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var clave in AlmacenSecretosWindows.Claves)
        {
            var linea = lineas.FirstOrDefault(l => l.TrimStart().StartsWith(clave + "=", StringComparison.Ordinal));
            var valor = linea?[(linea.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim().Trim('"');
            if (!string.IsNullOrEmpty(valor)) secretos[clave] = valor;
        }
        return secretos;
    }

    private static void GuardarSecretos(IReadOnlyDictionary<string, string> todos)
    {
        var secretos = todos.Where(p => AlmacenSecretosWindows.Claves.Contains(p.Key) && p.Value.Length > 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        AlmacenSecretosWindows.Guardar(Path.Combine(CarpetaDatos, AlmacenSecretosWindows.NombreArchivo), secretos);
        Console.WriteLine($"   {secretos.Count} secretos guardados: {string.Join(", ", secretos.Keys)} (valores no mostrados)");
    }

    /// <summary>Datos y secretos: SYSTEM y Administradores control total; la cuenta del servicio, modificar. Sin herencia.</summary>
    private static void ProtegerCarpetaDatos()
    {
        var carpeta = new DirectoryInfo(CarpetaDatos);
        carpeta.Create();
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags herencia = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herencia, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herencia, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new NTAccount(Cuenta), FileSystemRights.Modify, herencia, PropagationFlags.None, AccessControlType.Allow));
        carpeta.SetAccessControl(acl);
    }

    /// <summary>
    /// Binarios: solo SYSTEM y Administradores pueden escribir; los demás, leer y ejecutar.
    /// Necesario fuera de Program Files (ej. D:\), donde los usuarios suelen poder escribir y podrían
    /// reemplazar el ejecutable que corre el servicio.
    /// </summary>
    private static void ProtegerCarpetaPrograma()
    {
        var carpeta = new DirectoryInfo(CarpetaPrograma);
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags herencia = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herencia, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herencia, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, herencia, PropagationFlags.None, AccessControlType.Allow));
        carpeta.SetAccessControl(acl);
    }

    /// <summary>Usa el SID del grupo: el nombre cambia con el idioma de Windows.</summary>
    private static void AgregarAGrupo(string sid)
    {
        var grupo = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value.Split('\\').Last();
        var (codigo, salida) = Ejecutar("net.exe", $"localgroup \"{grupo}\" \"{Cuenta}\" /add");
        // 1378 = ya es miembro.
        Console.WriteLine(codigo == 0 || salida.Contains("1378", StringComparison.Ordinal) ? $"   {grupo}: ok" : $"   {grupo}: {salida.Trim()}");
    }

    private static void Detener()
    {
        Sc($"stop {Servicio}");
        for (var i = 0; i < 30 && !Estado().Contains("STOPPED", StringComparison.OrdinalIgnoreCase) && !Estado().StartsWith('1'); i++)
        {
            Thread.Sleep(1000);
        }
    }

    private static bool ServicioExiste() => Ejecutar("sc.exe", $"query {Servicio}").Codigo == 0;

    private static int Sc(string argumentos)
    {
        var (codigo, salida) = Ejecutar("sc.exe", argumentos);
        if (codigo != 0) Console.WriteLine($"   sc {argumentos.Split(' ')[0]}: {salida.Trim()}");
        return codigo;
    }

    private static (int Codigo, string Salida) Ejecutar(string programa, string argumentos) =>
        Ejecutar(new ProcessStartInfo(programa, argumentos));

    /// <summary>Con lista de argumentos: .NET escapa comillas y espacios (rutas como "D:\Mis cosas\SysPulse").</summary>
    private static (int Codigo, string Salida) Ejecutar(string programa, IEnumerable<string> argumentos)
    {
        var inicio = new ProcessStartInfo(programa);
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        return Ejecutar(inicio);
    }

    private static (int Codigo, string Salida) Ejecutar(ProcessStartInfo inicio)
    {
        inicio.RedirectStandardOutput = true;
        inicio.RedirectStandardError = true;
        inicio.UseShellExecute = false;
        inicio.CreateNoWindow = true;
        using var proceso = Process.Start(inicio)!;
        var salida = proceso.StandardOutput.ReadToEnd() + proceso.StandardError.ReadToEnd();
        proceso.WaitForExit();
        return (proceso.ExitCode, salida);
    }

    private static void CopiarCarpeta(string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var archivo in Directory.EnumerateFiles(origen, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origen, archivo);
            var objetivo = Path.Combine(destino, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(objetivo)!);
            // Windows permite renombrar un ejecutable en uso (no sobrescribirlo): así el actualizador se reemplaza a sí mismo.
            if (File.Exists(objetivo) && string.Equals(Path.GetFullPath(objetivo), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(objetivo, objetivo + ".old", overwrite: true);
            }
            File.Copy(archivo, objetivo, overwrite: true);
        }
    }

    private static void Paso(string texto) => Console.WriteLine("• " + texto);

    private static int Error(string texto)
    {
        Console.Error.WriteLine("Error: " + texto);
        return 1;
    }
}
