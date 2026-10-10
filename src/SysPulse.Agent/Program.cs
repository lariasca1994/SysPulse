using Serilog;
using SysPulse.Agent;
using SysPulse.Agent.Collectors.Windows;
using SysPulse.Agent.Core.Buffer;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.Agent.Core.Envio;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SysPulseAgent");

var opciones = new OpcionesAgente();
builder.Configuration.GetSection("Agente").Bind(opciones);

// Privacidad: nunca el nombre del equipo ni su modelo; solo un id anónimo estable.
if (string.IsNullOrWhiteSpace(opciones.HostId)) opciones.HostId = IdentidadEquipo.Id();
// El agente de Windows vigila el SLA de los incidentes abiertos (sección 11): solo despierta a la API en los cruces.
opciones.VigilarSla = true;

// Secretos: como servicio, desde agent.json cifrado con DPAPI (lo crea el instalador);
// en desarrollo, desde el .env del repositorio. Ninguno sobrescribe variables ya definidas.
var secretosCifrados = AlmacenSecretosWindows.CargarEnEntorno(Path.Combine(opciones.CarpetaDatos, AlmacenSecretosWindows.NombreArchivo));
var archivoEnv = secretosCifrados ? null : DotEnv.CargarSiExiste(AppContext.BaseDirectory);

opciones.MongoUri ??= Environment.GetEnvironmentVariable("MONGO_URI");
opciones.ApiUrl ??= Environment.GetEnvironmentVariable("API_URL");
opciones.AgentApiKey ??= Environment.GetEnvironmentVariable("AGENT_API_KEY");
opciones.TelegramBotToken ??= Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (opciones.TelegramChatIdsRespaldo.Count == 0)
{
    opciones.TelegramChatIdsRespaldo = (Environment.GetEnvironmentVariable("TELEGRAM_FIXED_CHAT_IDS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// Registro: archivo UTF-8 con rotación (7 archivos × 10 MB) en %ProgramData%\SysPulse\logs, más consola.
builder.Services.AddSerilog(config => config
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.File(
        Path.Combine(opciones.CarpetaDatos, "logs", "agente-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 7,
        encoding: System.Text.Encoding.UTF8,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton(_ => new BufferLocal(Path.Combine(opciones.CarpetaDatos, "agente.db")));
builder.Services.AddHttpClient<NotificadorRespaldo>()
    .AddTypedClient((http, _) => new NotificadorRespaldo(http, opciones.RespaldoTelegramActivo ? opciones.TelegramBotToken : null, opciones.TelegramChatIdsRespaldo));

builder.Services.AddSingleton<ICollector, CpuCollector>();
builder.Services.AddSingleton<ICollector, MemoriaCollector>();
builder.Services.AddSingleton<ICollector, DiscoCollector>();
builder.Services.AddSingleton<ICollector, RedCollector>();
builder.Services.AddSingleton<ICollector, SistemaCollector>();
builder.Services.AddSingleton<SysPulse.Agent.Core.MotorAgente>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
Log.Information("Secretos desde {Origen}", secretosCifrados ? "agent.json (DPAPI)" : archivoEnv is null ? "variables de entorno" : ".env de desarrollo");
host.Run();
