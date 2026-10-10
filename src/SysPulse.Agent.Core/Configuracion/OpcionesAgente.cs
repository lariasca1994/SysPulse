using SysPulse.Agent.Core.Alertas;

namespace SysPulse.Agent.Core.Configuracion;

/// <summary>Configuración del agente. Los secretos vienen de variables de entorno (.env en desarrollo, DPAPI/SecureStorage en producción).</summary>
public sealed class OpcionesAgente
{
    /// <summary>Id anónimo del host (nunca el nombre del equipo). Cada plataforma lo asigna si viene vacío.</summary>
    public string HostId { get; set; } = "";
    public string TipoHost { get; set; } = "windows";

    public string? MongoUri { get; set; }
    public string? ApiUrl { get; set; }
    public string? AgentApiKey { get; set; }
    /// <summary>Vigilar el SLA de los incidentes abiertos en Atlas y avisar a la API los cruces del 75 % y 100 % (solo Windows).</summary>
    public bool VigilarSla { get; set; }
    public string? TelegramBotToken { get; set; }
    public IReadOnlyList<string> TelegramChatIdsRespaldo { get; set; } = [];
    public bool RespaldoTelegramActivo { get; set; } = true;

    public string CarpetaDatos { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SysPulse");

    public TimeSpan IntervaloLectura { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan VentanaAgregado { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan IntervaloEnvioLotes { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan RetencionCrudo { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Cada cuánto se relee la configuración central en Atlas (cuentas de Telegram, umbrales).</summary>
    public TimeSpan IntervaloConfiguracion { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Destinos de los chequeos de red (solo handshake TCP, nunca login).</summary>
    public IReadOnlyList<string> DestinosTcp { get; set; } = ["1.1.1.1:443", "8.8.8.8:53"];
    public IReadOnlyList<string> DestinosPing { get; set; } = ["8.8.8.8", "1.1.1.1"];
    public IReadOnlyList<string> NombresDns { get; set; } = ["www.google.com", "cloud.mongodb.com"];

    public IReadOnlyList<Umbral> Umbrales { get; set; } = UmbralesPorDefecto.Windows;
}

public static class UmbralesPorDefecto
{
    public static readonly IReadOnlyList<Umbral> Windows =
    [
        new("cpu.total", Advertencia: 85, Excepcion: 95, Recuperacion: 75),
        new("ram.pct", Advertencia: 85, Excepcion: 95, Recuperacion: 80),
        new("paginacion.pct", Advertencia: 80, Excepcion: 95, Recuperacion: 70),
        new("eventlog.errores_5m", Advertencia: 5, Excepcion: 20, Recuperacion: 2, LecturasParaDisparar: 1, LecturasParaRecuperar: 3),
        new("red.ping.perdida_pct", Advertencia: 20, Excepcion: 60, Recuperacion: 5, LecturasParaDisparar: 2, LecturasParaRecuperar: 2),
        // Solo aplica a equipos con batería (portátiles, tablets): en un PC de escritorio la métrica no existe.
        new("bateria.baja_sin_cargar", Advertencia: 1, Excepcion: 2, Recuperacion: 0.5, LecturasParaDisparar: 2, LecturasParaRecuperar: 2),
    ];

    /// <summary>
    /// Celulares Android (sección 5, "Hosts Android"). Las condiciones "menor es peor" se expresan como métricas 0/1
    /// (ej. bateria.baja_sin_cargar) para usar el mismo evaluador.
    /// </summary>
    public static readonly IReadOnlyList<Umbral> Android =
    [
        new("bateria.temp_c", Advertencia: 40, Excepcion: 45, Recuperacion: 38),
        // PowerManager: 2 = moderado, 3 = grave, 4+ = crítico/emergencia/apagado.
        new("termico.estado", Advertencia: 2, Excepcion: 3, Recuperacion: 1, LecturasParaDisparar: 2, LecturasParaRecuperar: 3),
        new("bateria.baja_sin_cargar", Advertencia: 1, Excepcion: 2, Recuperacion: 0.5, LecturasParaDisparar: 2, LecturasParaRecuperar: 2),
        new("almacenamiento.pct", Advertencia: 85, Excepcion: 95, Recuperacion: 80),
        new("ram.baja", Advertencia: 1, Excepcion: 2, Recuperacion: 0.5),
        new("red.ping.perdida_pct", Advertencia: 20, Excepcion: 60, Recuperacion: 5, LecturasParaDisparar: 2, LecturasParaRecuperar: 2),
    ];

    /// <summary>Umbral de disco por unidad (la métrica incluye la letra: disco.C.pct).</summary>
    public static Umbral Disco(string metrica) => new(metrica, Advertencia: 85, Excepcion: 95, Recuperacion: 80);
}
