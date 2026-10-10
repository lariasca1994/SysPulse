using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Agent.Core.Agregacion;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Envio;

/// <summary>Agregado de una ventana de 5 min, tal como se encola en SQLite y se publica en Atlas.</summary>
public sealed record DocumentoAgregado(string Host, string TipoHost, DateTimeOffset Ventana, Dictionary<string, Resumen> Metricas);

/// <summary>
/// Escribe DIRECTO en Atlas (regla de oro, sección 3: el agente nunca envía métricas a Render).
/// Colecciones: metricas_5m (TTL 90 días), estado_actual (1 documento por host) y hosts.
/// Los nombres de métrica se guardan con '_' en vez de '.' para que sean campos consultables.
/// </summary>
public sealed class PublicadorAtlas
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(90);

    private readonly IMongoCollection<BsonDocument> _metricas;
    private readonly IMongoCollection<BsonDocument> _estado;
    private readonly IMongoCollection<BsonDocument> _hosts;
    private bool _indicesListos;

    public PublicadorAtlas(string cadenaConexion)
    {
        var url = MongoUrl.Create(cadenaConexion);
        var ajustes = MongoClientSettings.FromUrl(url);
        ajustes.ServerSelectionTimeout = TimeSpan.FromSeconds(20);
        ajustes.ApplicationName = "SysPulse.Agent";
        var db = new MongoClient(ajustes).GetDatabase(url.DatabaseName ?? "syspulse");
        _metricas = db.GetCollection<BsonDocument>("metricas_5m");
        _estado = db.GetCollection<BsonDocument>("estado_actual");
        _hosts = db.GetCollection<BsonDocument>("hosts");
    }

    public static string ClaveCampo(string metrica) => metrica.Replace('.', '_');

    public async Task AsegurarIndicesAsync(CancellationToken ct)
    {
        if (_indicesListos) return;
        await _metricas.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("host").Ascending("ts"), new CreateIndexOptions { Name = "host_ts", Unique = true }),
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("ts"), new CreateIndexOptions { Name = "ttl_90d", ExpireAfter = Ttl }),
        ], ct).ConfigureAwait(false);
        _indicesListos = true;
    }

    /// <summary>Registra el host sin nombre ni modelo (privacidad): la plataforma le asigna un nombre genérico.</summary>
    public async Task RegistrarHostAsync(string host, string tipo, string version, CancellationToken ct)
    {
        var doc = new BsonDocument
        {
            { "_id", host }, { "tipo", tipo }, { "version_agente", version }, { "ultimo_arranque", DateTime.UtcNow },
        };
        await _hosts.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", host), doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    /// <summary>Inserta los agregados pendientes. Ventanas ya existentes (reintentos) se ignoran sin error.</summary>
    public async Task PublicarAgregadosAsync(IEnumerable<string> jsonAgregados, CancellationToken ct)
    {
        var docs = jsonAgregados.Select(j => JsonSerializer.Deserialize<DocumentoAgregado>(j)!).Select(ABson).ToList();
        if (docs.Count == 0) return;
        try
        {
            await _metricas.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }, ct).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            // Lote reenviado tras un corte: las ventanas ya estaban en Atlas.
        }
    }

    public async Task ActualizarEstadoAsync(string host, string tipo, IEnumerable<Lectura> lecturas, CancellationToken ct)
    {
        var valores = new BsonDocument();
        foreach (var l in lecturas) valores[ClaveCampo(l.Metrica)] = l.Valor;
        var doc = new BsonDocument { { "_id", host }, { "tipo_host", tipo }, { "ts", DateTime.UtcNow }, { "lecturas", valores } };
        await _estado.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", host), doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Incidentes abiertos con sus marcas de tiempo, para que el agente de Windows calcule el SLA sin despertar a la API
    /// (sección 11). Solo lee los campos necesarios.
    /// </summary>
    public async Task<IReadOnlyList<(TiemposIncidente Tiempos, IReadOnlyList<string> Avisos)>> LeerIncidentesAbiertosAsync(CancellationToken ct)
    {
        var abiertos = new BsonArray { "Nuevo", "Asignado", "EnProgreso", "EnEspera", "Reabierto" };
        var proyeccion = new BsonDocument { { "prioridad", 1 }, { "inicio_sla", 1 }, { "primera_respuesta", 1 }, { "resuelto", 1 }, { "esperas", 1 }, { "avisos_sla", 1 } };
        var docs = await _metricas.Database.GetCollection<BsonDocument>("incidentes")
            .Find(new BsonDocument("estado", new BsonDocument("$in", abiertos)))
            .Project(proyeccion).ToListAsync(ct).ConfigureAwait(false);

        static DateTimeOffset? Fecha(BsonDocument d, string campo) =>
            d.TryGetValue(campo, out var v) && v.IsValidDateTime ? new DateTimeOffset(v.ToUniversalTime(), TimeSpan.Zero) : null;

        var resultado = new List<(TiemposIncidente, IReadOnlyList<string>)>();
        foreach (var d in docs)
        {
            if (Fecha(d, "inicio_sla") is not { } inicio || !d.TryGetValue("prioridad", out var p) || !Enum.TryParse<Prioridad>(p.ToString(), out var prioridad)) continue;
            var esperas = d.TryGetValue("esperas", out var e) && e.IsBsonArray
                ? e.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument)
                    .Where(x => Fecha(x, "inicio") is not null)
                    .Select(x => new PeriodoEspera(Fecha(x, "inicio")!.Value, Fecha(x, "fin"))).ToList()
                : [];
            var avisos = d.TryGetValue("avisos_sla", out var a) && a.IsBsonArray ? a.AsBsonArray.Select(x => x.ToString()!).ToList() : [];
            resultado.Add((new TiemposIncidente(inicio, prioridad, Fecha(d, "primera_respuesta"), Fecha(d, "resuelto"), esperas), avisos));
        }
        return resultado;
    }

    /// <summary>Lee la configuración central (colección "configuracion", documento "agentes"). Null si aún no existe.</summary>
    public async Task<Configuracion.ConfiguracionCentral?> LeerConfiguracionAsync(CancellationToken ct)
    {
        var doc = await _metricas.Database.GetCollection<BsonDocument>(Configuracion.ConfiguracionCentral.Coleccion)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", Configuracion.ConfiguracionCentral.Documento))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : DesdeBson(doc);
    }

    /// <summary>Crea o reemplaza la configuración central (la usa el instalador; luego la editará el admin desde la web).</summary>
    public async Task GuardarConfiguracionAsync(Configuracion.ConfiguracionCentral config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        var umbrales = new BsonArray(config.Umbrales.Select(u => new BsonDocument
        {
            { "metrica", u.Metrica }, { "advertencia", u.Advertencia }, { "excepcion", u.Excepcion }, { "recuperacion", u.Recuperacion },
            { "lecturas_disparar", u.LecturasParaDisparar }, { "lecturas_recuperar", u.LecturasParaRecuperar },
            { "tipo_host", u.TipoHost is null ? BsonNull.Value : u.TipoHost },
        }));
        var doc = new BsonDocument
        {
            { "_id", Configuracion.ConfiguracionCentral.Documento },
            { "telegram_chat_ids", new BsonArray(config.TelegramChatIds) },
            { "telegram_bot_token", config.TelegramBotToken is null ? BsonNull.Value : config.TelegramBotToken },
            { "api_url", config.ApiUrl is null ? BsonNull.Value : config.ApiUrl },
            { "agent_api_key", config.AgentApiKey is null ? BsonNull.Value : config.AgentApiKey },
            { "umbrales", umbrales },
            { "actualizacion_windows", PaqueteABson(config.ActualizacionWindows) },
            { "actualizacion_android", PaqueteABson(config.ActualizacionAndroid) },
            { "persistencia_minutos", config.PersistenciaMinutos is null ? BsonNull.Value : config.PersistenciaMinutos.Value },
            { "actualizado", DateTime.UtcNow },
        };
        await _metricas.Database.GetCollection<BsonDocument>(Configuracion.ConfiguracionCentral.Coleccion)
            .ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    /// <summary>Convierte el documento de Atlas en configuración (también lo usa la API para calcular la salud de los hosts).</summary>
    public static Configuracion.ConfiguracionCentral DesdeBson(BsonDocument d)
    {
        static string? Texto(BsonDocument doc, string campo) => doc.TryGetValue(campo, out var v) && v.IsString ? v.AsString : null;
        static double Numero(BsonDocument doc, string campo) => doc.TryGetValue(campo, out var v) && v.IsNumeric ? v.ToDouble() : 0;
        static int Entero(BsonDocument doc, string campo, int defecto) => doc.TryGetValue(campo, out var v) && v.IsNumeric ? v.ToInt32() : defecto;

        var chats = d.TryGetValue("telegram_chat_ids", out var c) && c.IsBsonArray
            ? c.AsBsonArray.Select(x => x.ToString()!).Where(x => x.Length > 0).ToList()
            : [];
        var umbrales = d.TryGetValue("umbrales", out var u) && u.IsBsonArray
            ? u.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument)
                .Where(x => Texto(x, "metrica") is not null)
                .Select(x => new Configuracion.UmbralCentral(
                    Texto(x, "metrica")!, Numero(x, "advertencia"), Numero(x, "excepcion"), Numero(x, "recuperacion"),
                    Entero(x, "lecturas_disparar", 3), Entero(x, "lecturas_recuperar", 3), Texto(x, "tipo_host")))
                .ToList()
            : [];
        var actualizado = d.TryGetValue("actualizado", out var a) && a.IsValidDateTime ? new DateTimeOffset(a.ToUniversalTime()) : DateTimeOffset.MinValue;
        return new Configuracion.ConfiguracionCentral
        {
            TelegramChatIds = chats,
            TelegramBotToken = Texto(d, "telegram_bot_token"),
            ApiUrl = Texto(d, "api_url"),
            AgentApiKey = Texto(d, "agent_api_key"),
            Umbrales = umbrales,
            ActualizacionWindows = PaqueteDesdeBson(d, "actualizacion_windows"),
            ActualizacionAndroid = PaqueteDesdeBson(d, "actualizacion_android"),
            PersistenciaMinutos = d.TryGetValue("persistencia_minutos", out var pm) && pm.IsNumeric ? pm.ToInt32() : null,
            Actualizado = actualizado,
        };
    }

    private static BsonValue PaqueteABson(Configuracion.PaqueteActualizacion? p) => p is null
        ? BsonNull.Value
        : new BsonDocument { { "version", p.Version }, { "url", p.Url }, { "sha256", p.Sha256 }, { "codigo", p.Codigo is null ? BsonNull.Value : p.Codigo.Value } };

    private static Configuracion.PaqueteActualizacion? PaqueteDesdeBson(BsonDocument d, string campo)
    {
        if (!d.TryGetValue(campo, out var v) || !v.IsBsonDocument) return null;
        var p = v.AsBsonDocument;
        string? T(string c) => p.TryGetValue(c, out var x) && x.IsString ? x.AsString : null;
        if (T("version") is not { } version || T("url") is not { } url || T("sha256") is not { } sha) return null;
        return new Configuracion.PaqueteActualizacion(version, url, sha, p.TryGetValue("codigo", out var c) && c.IsNumeric ? c.ToInt32() : null);
    }

    private static BsonDocument ABson(DocumentoAgregado a)
    {
        var metricas = new BsonDocument();
        foreach (var (nombre, r) in a.Metricas)
        {
            metricas[ClaveCampo(nombre)] = new BsonDocument
            {
                { "min", r.Min }, { "avg", r.Avg }, { "max", r.Max }, { "p95", r.P95 }, { "n", r.Muestras }, { "u", r.Unidad },
            };
        }
        return new BsonDocument { { "host", a.Host }, { "tipo_host", a.TipoHost }, { "ts", a.Ventana.UtcDateTime }, { "metricas", metricas } };
    }
}
