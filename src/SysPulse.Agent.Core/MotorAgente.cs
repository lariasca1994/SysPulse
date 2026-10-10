using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysPulse.Agent.Core.Agregacion;
using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Buffer;
using SysPulse.Agent.Core.Checks;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.Agent.Core.Envio;
using SysPulse.Contracts;
using SysPulse.Domain;

namespace SysPulse.Agent.Core;

/// <summary>Estado observable del motor (la app Android lo muestra en pantalla).</summary>
public sealed record EstadoMotor(DateTimeOffset? UltimoCiclo, int LecturasUltimoCiclo, DateTimeOffset? UltimoEnvioAtlas, string? UltimoError, string Buffer);

/// <summary>
/// Ciclo común de los agentes Windows y Android (sección 3):
/// cada lectura → SQLite + alertas + estado_actual; cada ventana (5 min) → chequeos de red + agregado en cola;
/// cada intervalo de lotes (30 min) → insertMany a Atlas. Sin internet, todo queda en SQLite y se envía al volver.
/// Cada host solo aporta sus colectores y umbrales.
/// </summary>
public sealed partial class MotorAgente(
    IEnumerable<ICollector> collectors,
    OpcionesAgente opciones,
    BufferLocal buffer,
    NotificadorRespaldo respaldo,
    ILogger<MotorAgente> logger)
{
    private PublicadorAtlas? _atlas;
    private EvaluadorAlertas _evaluador = null!;
    private DateTimeOffset _ventanaActual;
    private DateTimeOffset _ultimoEnvio = DateTimeOffset.MinValue;
    private DateTimeOffset _ultimaPurga = DateTimeOffset.UtcNow;
    private DateTimeOffset _ultimaConsultaConfig = DateTimeOffset.MinValue;
    private DateTimeOffset _configAplicada = DateTimeOffset.MinValue;
    private IReadOnlyList<Umbral> _umbralesBase = [];
    private string? _apiUrl;
    private string? _apiKey;
    private bool _conBateria;
    private DateTimeOffset _ultimaRevisionSla = DateTimeOffset.MinValue;

    public EstadoMotor Estado { get; private set; } = new(null, 0, null, null, "");

    /// <summary>Se dispara al aplicar una configuración central nueva (Android la usa para buscar actualizaciones).</summary>
    public event Action<ConfiguracionCentral>? ConfiguracionAplicada;

    private string RutaCopiaConfig => Path.Combine(opciones.CarpetaDatos, "config-central.json");

    /// <summary>Corre hasta que se cancele. <paramref name="umbralesExtra"/>: umbrales que dependen del equipo (ej. una por unidad de disco).</summary>
    public async Task EjecutarAsync(string version, IEnumerable<Umbral> umbralesExtra, CancellationToken ct)
    {
        _umbralesBase = [.. opciones.Umbrales.Concat(umbralesExtra)];
        _evaluador = new EvaluadorAlertas(opciones.HostId, _umbralesBase, opciones.PersistenciaAlertas);
        _ventanaActual = Agregador.InicioVentana(DateTimeOffset.UtcNow, opciones.VentanaAgregado);
        _apiUrl = opciones.ApiUrl;
        _apiKey = opciones.AgentApiKey;

        // Última configuración central conocida: permite arrancar con ella aunque no haya internet.
        if (ConfiguracionCentral.LeerCopia(RutaCopiaConfig) is { } copia) AplicarConfiguracion(copia, "copia local");

        if (!string.IsNullOrWhiteSpace(opciones.MongoUri))
        {
            _atlas = new PublicadorAtlas(opciones.MongoUri);
            await IntentarAsync("registrar host en Atlas", async () =>
            {
                await _atlas.AsegurarIndicesAsync(ct).ConfigureAwait(false);
                await _atlas.RegistrarHostAsync(opciones.HostId, opciones.TipoHost, version, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await ConsultarConfiguracionAsync(ct).ConfigureAwait(false);
        }
        else
        {
            LogSinAtlas();
        }

        LogInicio(opciones.HostId, opciones.TipoHost, version, opciones.IntervaloLectura, opciones.VentanaAgregado, opciones.IntervaloEnvioLotes, respaldo.Configurado);

        using var timer = new PeriodicTimer(opciones.IntervaloLectura);
        do
        {
            await CicloAsync(ct).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task CicloAsync(CancellationToken ct)
    {
        var lecturas = new List<Lectura>();
        foreach (var collector in collectors.Where(c => c.Soportado))
        {
            try
            {
                lecturas.AddRange(await collector.LeerAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFalloColector(ex, collector.Nombre);
            }
        }

        // Cambio de ventana: chequeos de red y agregado de la ventana que terminó.
        var ventana = Agregador.InicioVentana(DateTimeOffset.UtcNow, opciones.VentanaAgregado);
        if (ventana > _ventanaActual)
        {
            lecturas.AddRange(await ChequeosRed.EjecutarAsync(opciones.DestinosTcp, opciones.DestinosPing, opciones.NombresDns, ct).ConfigureAwait(false));
            buffer.GuardarLecturas(lecturas);
            CerrarVentana(_ventanaActual);
            _ventanaActual = ventana;
        }
        else
        {
            buffer.GuardarLecturas(lecturas);
        }

        _conBateria |= lecturas.Any(l => l.Metrica == "bateria.pct");
        await ProcesarAlertasAsync(_evaluador.Evaluar(lecturas), ct).ConfigureAwait(false);
        await EntregarEventosAsync(ct).ConfigureAwait(false);
        if (opciones.VigilarSla && DateTimeOffset.UtcNow - _ultimaRevisionSla >= TimeSpan.FromMinutes(5)) await VigilarSlaAsync(ct).ConfigureAwait(false);

        DateTimeOffset? enviado = null;
        if (_atlas is not null)
        {
            await IntentarAsync("actualizar estado_actual", () => _atlas.ActualizarEstadoAsync(opciones.HostId, opciones.TipoHost, lecturas, ct)).ConfigureAwait(false);

            if (DateTimeOffset.UtcNow - _ultimoEnvio >= opciones.IntervaloEnvioLotes)
            {
                var pendientes = buffer.AgregadosPendientes();
                if (pendientes.Count > 0 &&
                    await IntentarAsync("enviar lote a Atlas", () => _atlas.PublicarAgregadosAsync(pendientes.Select(p => p.Json), ct)).ConfigureAwait(false))
                {
                    buffer.MarcarAgregadosEnviados(pendientes.Select(p => p.Id));
                    enviado = DateTimeOffset.UtcNow;
                    LogLoteEnviado(pendientes.Count);
                }
                _ultimoEnvio = DateTimeOffset.UtcNow;
            }
        }

        if (DateTimeOffset.UtcNow - _ultimaConsultaConfig >= opciones.IntervaloConfiguracion)
        {
            await ConsultarConfiguracionAsync(ct).ConfigureAwait(false);
        }

        if (DateTimeOffset.UtcNow - _ultimaPurga >= TimeSpan.FromHours(6))
        {
            buffer.Purgar(opciones.RetencionCrudo);
            _ultimaPurga = DateTimeOffset.UtcNow;
        }

        var estadoBuffer = buffer.Resumen();
        Estado = Estado with { UltimoCiclo = DateTimeOffset.UtcNow, LecturasUltimoCiclo = lecturas.Count, UltimoEnvioAtlas = enviado ?? Estado.UltimoEnvioAtlas, Buffer = estadoBuffer };
        LogCiclo(lecturas.Count, estadoBuffer);
    }

    private async Task ConsultarConfiguracionAsync(CancellationToken ct)
    {
        _ultimaConsultaConfig = DateTimeOffset.UtcNow;
        if (_atlas is null) return;
        ConfiguracionCentral? config = null;
        await IntentarAsync("leer la configuración central", async () => config = await _atlas.LeerConfiguracionAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
        if (config is null || config.Actualizado <= _configAplicada) return;

        AplicarConfiguracion(config, "Atlas");
        await IntentarAsync("guardar la copia local de la configuración", () =>
        {
            config.GuardarCopia(RutaCopiaConfig);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    /// <summary>Aplica en caliente: cuentas y token de Telegram, y umbrales (las alertas abiertas se conservan).</summary>
    private void AplicarConfiguracion(ConfiguracionCentral config, string origen)
    {
        var token = opciones.RespaldoTelegramActivo ? config.TelegramBotToken ?? opciones.TelegramBotToken : null;
        var chats = config.TelegramChatIds.Count > 0 ? config.TelegramChatIds : opciones.TelegramChatIdsRespaldo;
        respaldo.Actualizar(token, chats);
        // La URL y la clave locales (si existen) tienen prioridad; si no, se usan las de la configuración central.
        _apiUrl = string.IsNullOrWhiteSpace(opciones.ApiUrl) ? config.ApiUrl : opciones.ApiUrl;
        _apiKey = string.IsNullOrWhiteSpace(opciones.AgentApiKey) ? config.AgentApiKey : opciones.AgentApiKey;

        var umbrales = config.Combinar(_umbralesBase, opciones.TipoHost);
        _evaluador.ActualizarUmbrales(umbrales);
        _evaluador.Persistencia = config.PersistenciaMinutos is > 0 and <= 120 ? TimeSpan.FromMinutes(config.PersistenciaMinutos.Value) : opciones.PersistenciaAlertas;
        _configAplicada = config.Actualizado;
        LogConfiguracion(origen, chats.Count, config.Umbrales.Count, config.Actualizado);
        ConfiguracionAplicada?.Invoke(config);
    }

    private void CerrarVentana(DateTimeOffset inicio)
    {
        var lecturas = buffer.LeerLecturas(inicio, inicio + opciones.VentanaAgregado);
        if (lecturas.Count == 0) return;
        var documento = new DocumentoAgregado(opciones.HostId, opciones.TipoHost, inicio, Agregador.Resumir(lecturas).ToDictionary(StringComparer.Ordinal));
        buffer.EncolarAgregado(inicio, documento);
        LogVentana(inicio, documento.Metricas.Count);
    }

    private async Task ProcesarAlertasAsync(IReadOnlyList<CambioAlerta> cambios, CancellationToken ct)
    {
        foreach (var cambio in cambios)
        {
            LogAlerta(cambio.Tipo, cambio.Severidad, cambio.Metrica, cambio.Valor);
            buffer.EncolarEvento(new EventoDto(
                opciones.HostId, opciones.TipoHost, cambio.Tipo.ToString().ToLowerInvariant(), TextoSeveridad(cambio.Severidad),
                cambio.Metrica, cambio.Valor, cambio.Umbral, cambio.ClaveDedup, cambio.Ts));
        }
    }

    private static string TextoSeveridad(Severidad s) => s switch
    {
        Domain.Severidad.Excepcion => "excepcion",
        Domain.Severidad.Advertencia => "advertencia",
        _ => "informativo",
    };

    /// <summary>
    /// Entrega los eventos pendientes a la API (sección 3). Si la API no responde (90 s la primera vez, por el arranque
    /// en frío de Render), las excepciones salen por el respaldo de Telegram a las cuentas fijas y el evento se reintenta
    /// en los ciclos siguientes, marcado para que la API no repita el aviso a esas cuentas. Sin API configurada,
    /// solo queda el respaldo. Un evento de más de 24 h ya no se entrega.
    /// </summary>
    private async Task EntregarEventosAsync(CancellationToken ct)
    {
        var url = _apiUrl;
        var clave = _apiKey;
        var hayApi = !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(clave);

        foreach (var (id, json, intentos) in buffer.EventosPendientes())
        {
            EventoDto? evento;
            try
            {
                evento = JsonSerializer.Deserialize<EventoDto>(json);
            }
            catch (JsonException)
            {
                evento = null;
            }
            if (evento is null || DateTimeOffset.UtcNow - evento.Ts > TimeSpan.FromHours(24))
            {
                buffer.MarcarEventoEntregado(id);
                continue;
            }

            if (hayApi && await ClienteApi.EnviarEventoAsync(url!, clave!, evento, intentos == 0 ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(20), ct).ConfigureAwait(false))
            {
                buffer.MarcarEventoEntregado(id);
                LogEventoEntregado(evento.Metrica, evento.Tipo);
                continue;
            }

            buffer.SumarIntentoEvento(id);
            if (!evento.RespaldoEnviado && NotificadorRespaldo.DebeAvisar(evento))
            {
                var enviados = 0;
                await IntentarAsync("aviso de respaldo por Telegram", async () =>
                    enviados = await respaldo.EnviarAsync(NotificadorRespaldo.Formatear(evento, NombreGenerico), ct).ConfigureAwait(false)).ConfigureAwait(false);
                LogRespaldo(enviados);
                if (enviados > 0) buffer.ActualizarEvento(id, JsonSerializer.Serialize(evento with { RespaldoEnviado = true }));
            }

            if (!hayApi)
            {
                buffer.MarcarEventoEntregado(id);
                continue;
            }
            // La API no responde: el resto se reintenta en el próximo ciclo (sin insistir evento por evento).
            break;
        }
    }

    /// <summary>
    /// Agente de Windows: calcula el SLA de los incidentes abiertos leyendo Atlas y solo despierta a la API cuando
    /// hay un aviso del 75 % o 100 % pendiente (sección 11). La API vuelve a calcular y evita duplicados.
    /// </summary>
    private async Task VigilarSlaAsync(CancellationToken ct)
    {
        _ultimaRevisionSla = DateTimeOffset.UtcNow;
        if (_atlas is null || string.IsNullOrWhiteSpace(_apiUrl) || string.IsNullOrWhiteSpace(_apiKey)) return;
        IReadOnlyList<(TiemposIncidente Tiempos, IReadOnlyList<string> Avisos)> abiertos = [];
        if (!await IntentarAsync("leer incidentes abiertos", async () => abiertos = await _atlas.LeerIncidentesAbiertosAsync(ct).ConfigureAwait(false)).ConfigureAwait(false)) return;

        var ahora = DateTimeOffset.UtcNow;
        var pendientes = abiertos.Count(i => SlaDeIncidente.AvisosPendientes(
            SlaDeIncidente.Calcular(i.Tiempos, ahora), true, i.Tiempos.PrimeraRespuesta is not null, i.Avisos).Count > 0);
        if (pendientes == 0) return;

        var ok = await ClienteApi.AvisarSlaAsync(_apiUrl!, _apiKey!, TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
        LogSla(pendientes, ok);
    }

    /// <summary>Nombre genérico para el respaldo de Telegram: nunca el nombre del equipo ni su modelo.</summary>
    private string NombreGenerico => opciones.TipoHost == "android" ? "Celular" : _conBateria ? "Equipo portátil" : "Equipo de escritorio";

    private async Task<bool> IntentarAsync(string accion, Func<Task> trabajo)
    {
        try
        {
            await trabajo().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Estado = Estado with { UltimoError = $"{accion}: {ex.Message}" };
            LogFalloAccion(accion, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SysPulse Agent {Host} ({Tipo}) v{Version}: lectura cada {Lectura}, ventana {Ventana}, lotes cada {Lotes}, respaldo Telegram {Respaldo}")]
    private partial void LogInicio(string host, string tipo, string version, TimeSpan lectura, TimeSpan ventana, TimeSpan lotes, bool respaldo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuración central aplicada desde {Origen}: {Cuentas} cuenta(s) de Telegram, {Umbrales} umbral(es) personalizados (versión {Actualizado:yyyy-MM-dd HH:mm:ss})")]
    private partial void LogConfiguracion(string origen, int cuentas, int umbrales, DateTimeOffset actualizado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MONGO_URI no configurada: las métricas solo se guardan en SQLite")]
    private partial void LogSinAtlas();

    [LoggerMessage(Level = LogLevel.Information, Message = "Ciclo: {Lecturas} lecturas | {Buffer}")]
    private partial void LogCiclo(int lecturas, string buffer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ventana {Inicio:HH:mm} cerrada: {Metricas} métricas agregadas")]
    private partial void LogVentana(DateTimeOffset inicio, int metricas);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lote enviado a Atlas: {Cantidad} ventanas")]
    private partial void LogLoteEnviado(int cantidad);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerta {Tipo} ({Severidad}): {Metrica} = {Valor:0.##}")]
    private partial void LogAlerta(TipoCambio tipo, Severidad severidad, string metrica, double valor);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso de respaldo enviado a {Enviados} cuenta(s) de Telegram")]
    private partial void LogRespaldo(int enviados);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evento entregado a la API: {Metrica} ({Tipo})")]
    private partial void LogEventoEntregado(string metrica, string tipo);

    [LoggerMessage(Level = LogLevel.Information, Message = "SLA: {Pendientes} incidente(s) con aviso pendiente; API avisada: {Ok}")]
    private partial void LogSla(int pendientes, bool ok);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el colector {Collector}")]
    private partial void LogFalloColector(Exception ex, string collector);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo {Accion}: {Tipo}: {Mensaje} (se reintenta en el próximo ciclo)")]
    private partial void LogFalloAccion(string accion, string tipo, string mensaje);
}
