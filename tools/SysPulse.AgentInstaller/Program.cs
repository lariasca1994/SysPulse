// Instalador del agente de Windows (sección 13 de la especificación).

using DnsClient;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.AgentInstaller;

const string Uso = """
    SysPulse.AgentInstaller

    Uso (install, update y uninstall requieren PowerShell como administrador):
      install   [--desde <carpeta>] [--env <ruta .env>] [--destino <carpeta>] [--datos <carpeta>]
                Instala el servicio SysPulseAgent. --desde: carpeta con SysPulse.Agent.exe publicado
                (por defecto la subcarpeta "agent" junto al instalador). --env: archivo con los secretos
                (por defecto el .env más cercano); se cifran con DPAPI y no se muestran.
                --destino / --datos: instalar fuera de C: (ej. --destino D:\SysPulse\Agent --datos D:\SysPulse\Datos).
                Por defecto: C:\Program Files\SysPulse\Agent y C:\ProgramData\SysPulse.
      update    [--desde <carpeta>]   Reemplaza los binarios y reinicia el servicio.
      secretos  [--env <ruta .env>]   Vuelve a cifrar los secretos (tras cambiar el .env) y reinicia el servicio.
      uninstall [--purgar]            Elimina el servicio y los binarios (--purgar borra también datos y secretos).
      estado                          Muestra si el servicio está instalado y corriendo.
      config-central [--env <ruta>]   Publica en Atlas la configuración que comparten todos los agentes
                                      (cuentas fijas y token de Telegram). Los agentes la toman solos en
                                      menos de 5 minutos, sin reinstalar. No requiere administrador.
      auto-actualizar [--solo-comprobar]
                Lo ejecuta la tarea programada (SYSTEM) cada hora: si hay una versión publicada más nueva,
                la descarga de GitHub Releases, verifica la huella SHA-256 y actualiza el servicio.
                --solo-comprobar: descarga y verifica, sin instalar.
      publicar-version (--windows <zip> | --android <apk> --codigo <n>) --version <x.y.z> --url <url> [--env <ruta>]
                Anuncia en la configuración central una versión ya subida a GitHub Releases. Calcula la huella
                del archivo local (debe ser idéntico al subido). Los equipos se actualizan solos.
      mongo-estandar                  Lee MONGO_URI (mongodb+srv://) del .env, resuelve SRV/TXT y guarda
                                      MONGO_URI_ANDROID (mongodb://) en el mismo .env, sin mostrarla.
    """;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var comando = args.Length > 0 ? args[0] : "";
string? Opcion(string nombre)
{
    var i = Array.IndexOf(args, nombre);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (comando == "mongo-estandar") return await MongoEstandarAsync();
if (comando == "config-central") return await ConfigCentralAsync(Opcion("--env") ?? BuscarEnv(Directory.GetCurrentDirectory()));
if (comando == "publicar-version") return await PublicarVersionAsync(Opcion("--env") ?? BuscarEnv(Directory.GetCurrentDirectory()), Opcion("--windows"), Opcion("--android"), Opcion("--version"), Opcion("--url"), Opcion("--codigo"));
if (comando == "estado")
{
    Console.WriteLine($"Servicio {Instalacion.Servicio}: {Instalacion.Estado()}");
    return 0;
}

if (comando is "install" or "update" or "uninstall" or "secretos" or "auto-actualizar")
{
    if (!Instalacion.EsAdministrador())
    {
        Console.Error.WriteLine("Este comando requiere PowerShell abierto como administrador.");
        return 1;
    }

    if (comando == "auto-actualizar") return await Actualizador.EjecutarAsync(args.Contains("--solo-comprobar"));

    var origen = Path.GetFullPath(Opcion("--desde") ?? Path.Combine(AppContext.BaseDirectory, "agent"));
    if (comando == "install") Instalacion.UsarRutas(Opcion("--destino"), Opcion("--datos"));
    else Instalacion.DetectarRutasInstaladas();
    return comando switch
    {
        "install" => Instalacion.Instalar(origen, Opcion("--env") ?? BuscarEnv(Directory.GetCurrentDirectory()) ?? ".env"),
        "update" => Instalacion.Actualizar(origen),
        "secretos" => Instalacion.ActualizarSecretos(Opcion("--env") ?? BuscarEnv(Directory.GetCurrentDirectory()) ?? ".env"),
        _ => Instalacion.Desinstalar(args.Contains("--purgar")),
    };
}

Console.WriteLine(Uso);
return comando.Length == 0 ? 0 : 1;

static async Task<int> MongoEstandarAsync()
{
    var rutaEnv = BuscarEnv(Directory.GetCurrentDirectory());
    if (rutaEnv is null)
    {
        Console.Error.WriteLine("No se encontró el archivo .env (búsquelo desde la carpeta del repositorio).");
        return 1;
    }

    var lineas = File.ReadAllLines(rutaEnv, System.Text.Encoding.UTF8).ToList();
    var srv = Valor(lineas, "MONGO_URI");
    if (srv is null || !srv.StartsWith(CadenaMongo.PrefijoSrv, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("MONGO_URI no existe en el .env o no es mongodb+srv://.");
        return 1;
    }

    var host = new Uri(srv.Replace(CadenaMongo.PrefijoSrv, "https://", StringComparison.Ordinal)).Host;
    var dns = new LookupClient();
    var respuestaSrv = await dns.QueryAsync($"_mongodb._tcp.{host}", QueryType.SRV);
    var hosts = respuestaSrv.Answers.SrvRecords()
        .Select(r => $"{r.Target.Value.TrimEnd('.')}:{r.Port}")
        .Order(StringComparer.Ordinal)
        .ToList();
    var respuestaTxt = await dns.QueryAsync(host, QueryType.TXT);
    var txt = string.Concat(respuestaTxt.Answers.TxtRecords().SelectMany(t => t.Text));

    var estandar = CadenaMongo.ConvertirSrvAEstandar(srv, hosts, txt);

    var indice = lineas.FindIndex(l => l.TrimStart().StartsWith("MONGO_URI_ANDROID=", StringComparison.Ordinal));
    var nueva = $"MONGO_URI_ANDROID={estandar}";
    if (indice >= 0) lineas[indice] = nueva;
    else lineas.Insert(lineas.FindIndex(l => l.TrimStart().StartsWith("MONGO_URI=", StringComparison.Ordinal)) + 1, nueva);
    File.WriteAllLines(rutaEnv, lineas, new System.Text.UTF8Encoding(false));

    Console.WriteLine($"MONGO_URI_ANDROID guardada en {rutaEnv} ({hosts.Count} hosts del clúster, {(string.IsNullOrEmpty(txt) ? "sin" : "con")} opciones TXT).");
    return 0;
}

static async Task<int> ConfigCentralAsync(string? rutaEnv)
{
    if (rutaEnv is null || !File.Exists(rutaEnv))
    {
        Console.Error.WriteLine("No se encontró el archivo .env.");
        return 1;
    }
    var lineas = File.ReadAllLines(rutaEnv, System.Text.Encoding.UTF8).ToList();
    var mongo = Valor(lineas, "MONGO_URI");
    if (string.IsNullOrWhiteSpace(mongo))
    {
        Console.Error.WriteLine("MONGO_URI no está en el .env.");
        return 1;
    }

    var atlas = new SysPulse.Agent.Core.Envio.PublicadorAtlas(mongo);
    var actual = await atlas.LeerConfiguracionAsync(CancellationToken.None);
    var chats = (Valor(lineas, "TELEGRAM_FIXED_CHAT_IDS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var nueva = new ConfiguracionCentral
    {
        TelegramChatIds = chats,
        TelegramBotToken = Valor(lineas, "TELEGRAM_BOT_TOKEN"),
        ApiUrl = Valor(lineas, "API_URL") is { Length: > 0 } api ? api.TrimEnd('/') : null,
        AgentApiKey = Valor(lineas, "AGENT_API_KEY") is { Length: > 0 } clave ? clave : null,
        // Los umbrales personalizados (si ya existían) se conservan: los editará el admin desde la web.
        Umbrales = actual?.Umbrales ?? [],
        ActualizacionWindows = actual?.ActualizacionWindows,
        ActualizacionAndroid = actual?.ActualizacionAndroid,
    };
    await atlas.GuardarConfiguracionAsync(nueva, CancellationToken.None);
    Console.WriteLine($"Configuración central publicada: {chats.Length} cuenta(s) de Telegram, token {(nueva.TelegramBotToken is null ? "sin cambios (no está en el .env)" : "incluido")}, API {(nueva.ApiUrl ?? "sin configurar (solo respaldo por Telegram)")}, clave de agente {(nueva.AgentApiKey is null ? "no" : "incluida")}, {nueva.Umbrales.Count} umbral(es) personalizados.");
    Console.WriteLine("Los agentes la aplicarán solos en menos de 5 minutos.");
    return 0;
}

static async Task<int> PublicarVersionAsync(string? rutaEnv, string? zipWindows, string? apkAndroid, string? version, string? url, string? codigo)
{
    var archivo = zipWindows ?? apkAndroid;
    if (archivo is null || version is null || url is null || (zipWindows is not null && apkAndroid is not null))
    {
        Console.Error.WriteLine("Uso: publicar-version (--windows <zip> | --android <apk> --codigo <n>) --version <x.y.z> --url <url>");
        return 1;
    }
    if (!File.Exists(archivo)) return Falla($"No existe {archivo}.");
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !SysPulse.Agent.Core.Actualizaciones.VerificadorPaquete.UrlPermitida(uri))
        return Falla("La URL debe ser de GitHub Releases por HTTPS (los equipos rechazan cualquier otra).");
    if (!Version.TryParse(version, out _)) return Falla("La versión debe tener el formato 1.2.0.");
    int? codigoAndroid = null;
    if (apkAndroid is not null)
    {
        if (!int.TryParse(codigo, out var c) || c <= 0) return Falla("Para Android indique --codigo (el ApplicationVersion del APK, entero creciente).");
        codigoAndroid = c;
    }
    if (rutaEnv is null || !File.Exists(rutaEnv)) return Falla("No se encontró el archivo .env.");
    var mongo = Valor([.. File.ReadAllLines(rutaEnv, System.Text.Encoding.UTF8)], "MONGO_URI");
    if (string.IsNullOrWhiteSpace(mongo)) return Falla("MONGO_URI no está en el .env.");

    var atlas = new SysPulse.Agent.Core.Envio.PublicadorAtlas(mongo);
    var actual = await atlas.LeerConfiguracionAsync(CancellationToken.None) ?? new ConfiguracionCentral();
    var paquete = new PaqueteActualizacion(version, url, SysPulse.Agent.Core.Actualizaciones.VerificadorPaquete.Huella(archivo), codigoAndroid);
    var nueva = zipWindows is not null ? actual with { ActualizacionWindows = paquete } : actual with { ActualizacionAndroid = paquete };
    await atlas.GuardarConfiguracionAsync(nueva, CancellationToken.None);
    Console.WriteLine($"Versión {version} publicada para {(zipWindows is not null ? "Windows" : "Android")}. Huella SHA-256: {paquete.Sha256}");
    Console.WriteLine(zipWindows is not null
        ? "Los equipos Windows se actualizarán solos en la próxima hora."
        : "Los celulares la descargarán y mostrarán una notificación para instalarla.");
    return 0;
}

static int Falla(string mensaje)
{
    Console.Error.WriteLine("Error: " + mensaje);
    return 1;
}

static string? Valor(List<string> lineas, string clave)
{
    var linea = lineas.FirstOrDefault(l => l.TrimStart().StartsWith(clave + "=", StringComparison.Ordinal));
    return linea?[(linea.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim().Trim('"');
}

static string? BuscarEnv(string directorio)
{
    for (var actual = new DirectoryInfo(directorio); actual is not null; actual = actual.Parent)
    {
        var candidato = Path.Combine(actual.FullName, ".env");
        if (File.Exists(candidato)) return candidato;
    }
    return null;
}
