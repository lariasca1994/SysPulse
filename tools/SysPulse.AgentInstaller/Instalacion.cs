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
        RegistrarActualizador();

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
        RegistrarActualizador();
        Console.WriteLine($"Listo. Estado: {Estado()}.");
        return 0;
    }

    /// <summary>
    /// install sobre un agente ya instalado (por ejemplo una versión anterior a la actualización automática):
    /// reemplaza los binarios, guarda la configuración del código nuevo, registra la tarea de actualización y reinicia.
    /// </summary>
    public static int Reinstalar(string origen, IReadOnlyDictionary<string, string> secretos)
    {
        Paso($"El agente ya estaba instalado (v{Actualizador.VersionInstalada()}): se actualiza en {CarpetaPrograma}");
        Paso("Deteniendo el servicio");
        Detener();
        try
        {
            Paso("Reemplazando los binarios");
            CopiarCarpeta(origen, CarpetaPrograma);
            ProtegerCarpetaPrograma();
            CopiarInstalador();
            Paso("Guardando los secretos cifrados (DPAPI)");
            GuardarSecretos(secretos);
            ProtegerCarpetaDatos();
        }
        finally
        {
            Paso("Iniciando el servicio");
            Sc($"start {Servicio}");
        }
        RegistrarActualizador();
        Console.WriteLine();
        Console.WriteLine($"Listo. Estado: {Estado()} · agente v{Actualizador.VersionInstalada()}. Logs en {Path.Combine(CarpetaDatos, "logs")}");
        return 0;
    }

    public static bool ServicioInstalado() => ServicioExiste();

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
        Paso("Eliminando el servicio de actualización automática");
        if (Ejecutar("sc.exe", $"query {ServicioActualizador.Nombre}").Codigo == 0)
        {
            Sc($"stop {ServicioActualizador.Nombre}");
            Sc($"delete {ServicioActualizador.Nombre}");
        }
        BorrarTareaAntigua();
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
    /// Servicio "SysPulse Actualizador" (LocalSystem) que revisa cada hora si hay versión nueva. Se crea o se
    /// reconfigura en cada instalación o actualización. Reemplaza a la antigua tarea programada, que algunos
    /// antivirus bloquean (crear tareas programadas como SYSTEM es un patrón típico de malware).
    /// </summary>
    private static void RegistrarActualizador()
    {
        Paso("Configurando la actualización automática (servicio SysPulse Actualizador, cada hora)");
        var exe = Path.Combine(CarpetaPrograma, EjecutableInstalador);
        var binPath = $"\"{exe}\" servicio-actualizador";
        var nombre = ServicioActualizador.Nombre;
        var existe = Ejecutar("sc.exe", $"query {nombre}").Codigo == 0;
        var (codigo, salida) = existe
            ? Ejecutar("sc.exe", ["config", nombre, "binPath=", binPath, "start=", "delayed-auto"])
            : Ejecutar("sc.exe", ["create", nombre, "binPath=", binPath, "start=", "delayed-auto", "DisplayName=", "SysPulse Actualizador"]);
        if (codigo != 0)
        {
            Console.WriteLine("   no se pudo configurar el servicio actualizador: " + salida.Trim());
            return;
        }
        Sc($"description {nombre} \"SysPulse: instala las versiones nuevas del agente publicadas en GitHub Releases (verifica la huella SHA-256).\"");
        Sc($"failure {nombre} reset= 86400 actions= restart/60000/restart/60000/restart/300000");
        // Sin esta marca, Windows no aplica el reinicio cuando el servicio se detiene con código de error
        // (lo que hace al reemplazarse a sí mismo): quedaba detenido hasta el próximo reinicio del equipo.
        Sc($"failureflag {nombre} 1");
        PermitirInicioAlAgente();
        BorrarTareaAntigua();
        Sc($"start {nombre}");
        Console.WriteLine("   servicio listo: " + nombre);
    }

    /// <summary>Equipos instalados con la tarea programada: crean el servicio actualizador y borran la tarea.</summary>
    public static void MigrarAServicioActualizador()
    {
        if (Ejecutar("sc.exe", $"query {ServicioActualizador.Nombre}").Codigo != 0) RegistrarActualizador();
    }

    private static void BorrarTareaAntigua()
    {
        if (Ejecutar("schtasks.exe", ["/Query", "/TN", Actualizador.Tarea]).Codigo == 0)
        {
            Ejecutar("schtasks.exe", ["/Delete", "/TN", Actualizador.Tarea, "/F"]);
        }
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
        for (var i = 0; i < 45 && !Estado().Contains("STOPPED", StringComparison.OrdinalIgnoreCase) && !Estado().StartsWith('1'); i++)
        {
            Thread.Sleep(1000);
        }

        // Windows marca el servicio como detenido un momento antes de que el proceso libere sus archivos
        // (p. ej. e_sqlite3.dll): se espera a que el proceso del agente termine y, si no lo hace, se cierra.
        var agente = Path.GetFullPath(Path.Combine(CarpetaPrograma, Ejecutable));
        foreach (var proceso in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Ejecutable)))
        {
            using (proceso)
            {
                try
                {
                    if (!string.Equals(proceso.MainModule?.FileName, agente, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!proceso.WaitForExit(15000))
                    {
                        Paso("El agente no terminó a tiempo: se cierra el proceso");
                        proceso.Kill(entireProcessTree: true);
                        proceso.WaitForExit(10000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Ya terminó.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Sin acceso a ese proceso: no es el nuestro.
                }
            }
        }
    }

    /// <summary>
    /// Reinicio del servicio actualizador tras reemplazarse a sí mismo: espera a que la instancia anterior se
    /// detenga (hasta 3 minutos) y lo inicia con el ejecutable nuevo. No depende de la recuperación de Windows.
    /// </summary>
    public static async Task<int> ReiniciarActualizadorAsync()
    {
        var nombre = ServicioActualizador.Nombre;
        Sc($"failureflag {nombre} 1");
        for (var intento = 0; intento < 60; intento++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            var (_, salida) = Ejecutar("sc.exe", $"query {nombre}");
            // El estado se lee por su código numérico (1 = detenido), igual en Windows en español o en inglés.
            if (!Regex.IsMatch(salida, @"(STATE|ESTADO)\s*:\s*1\s", RegexOptions.IgnoreCase)) continue;
            return Ejecutar("sc.exe", $"start {nombre}").Codigo;
        }
        return 1;
    }

    public static void ActivarReinicioActualizador()
    {
        Sc($"failureflag {ServicioActualizador.Nombre} 1");
        PermitirInicioAlAgente();
    }

    /// <summary>
    /// La cuenta del agente (NT SERVICE\SysPulseAgent) puede consultar e iniciar el servicio actualizador, y nada más
    /// (ni detenerlo, ni cambiar su configuración): así el agente lo vuelve a iniciar si quedó detenido.
    /// </summary>
    private static void PermitirInicioAlAgente()
    {
        try
        {
            var sid = new NTAccount(Cuenta).Translate(typeof(SecurityIdentifier)).Value;
            var nombre = ServicioActualizador.Nombre;
            var (codigo, salida) = Ejecutar("sc.exe", $"sdshow {nombre}");
            var sddl = salida.Trim();
            if (codigo != 0 || !sddl.StartsWith("D:", StringComparison.Ordinal) || sddl.Contains(sid, StringComparison.OrdinalIgnoreCase)) return;
            // CC = leer configuración, LC = consultar estado, RP = iniciar, LO = interrogar. Va al final de la DACL (antes de la SACL "S:").
            var ace = $"(A;;CCLCRPLO;;;{sid})";
            var s = sddl.IndexOf("S:", StringComparison.Ordinal);
            var nuevo = s < 0 ? sddl + ace : sddl[..s] + ace + sddl[s..];
            Ejecutar("sc.exe", ["sdset", nombre, nuevo]);
        }
        catch (IdentityNotMappedException)
        {
            // El servicio del agente aún no existe: se configura en la próxima instalación o arranque del actualizador.
        }
    }

    /// <summary>Lanza "reiniciar-actualizador" con el ejecutable nuevo, como proceso aparte que sobrevive al servicio.</summary>
    public static void ProgramarReinicioActualizador(string ejecutable)
    {
        using var p = Process.Start(new ProcessStartInfo(ejecutable, "reiniciar-actualizador")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ejecutable)!,
        });
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
            if (string.Equals(Path.GetFullPath(archivo), Path.GetFullPath(objetivo), StringComparison.OrdinalIgnoreCase)) continue;
            // Windows permite renombrar un ejecutable o librería en uso (no sobrescribirlo): así el actualizador se
            // reemplaza a sí mismo y un archivo que aún esté bloqueado no detiene la actualización.
            if (File.Exists(objetivo) && string.Equals(Path.GetFullPath(objetivo), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(objetivo, objetivo + ".old", overwrite: true);
            }
            try
            {
                File.Copy(archivo, objetivo, overwrite: true);
            }
            catch (IOException) when (File.Exists(objetivo))
            {
                File.Move(objetivo, objetivo + ".old", overwrite: true);
                File.Copy(archivo, objetivo, overwrite: true);
            }
        }
    }

    private static void Paso(string texto) => Console.WriteLine("• " + texto);

    private static int Error(string texto)
    {
        Console.Error.WriteLine("Error: " + texto);
        return 1;
    }
}
