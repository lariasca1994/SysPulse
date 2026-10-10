using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.Agent.Core.Envio;
using SysPulse.Domain;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

public sealed record UmbralVista(double Advertencia, double Excepcion);

public sealed record AlertaVista(string Metrica, double Valor, double Umbral, string Severidad);

/// <summary>
/// Estado de un host para la web. <see cref="Nombre"/> es genérico (nunca el modelo ni el nombre del equipo).
/// <see cref="Conexion"/>: en-linea | retrasado | sin-reporte.
/// <see cref="Salud"/>: ok | advertencia | excepcion | desconocida (sin lecturas recientes).
/// Las claves de <see cref="Lecturas"/> y <see cref="Umbrales"/> usan '_' (cpu_total, disco_C_pct…), igual que Atlas.
/// </summary>
public sealed record HostVista(
    string Id,
    string Nombre,
    string Tipo,
    string? VersionAgente,
    DateTime? UltimoArranque,
    DateTime? UltimoReporte,
    string Conexion,
    string Salud,
    IReadOnlyDictionary<string, double> Lecturas,
    IReadOnlyDictionary<string, UmbralVista> Umbrales,
    IReadOnlyList<AlertaVista> Alertas);

/// <summary>Equipo para la administración: nombre genérico, tipo, versión y si está retirado.</summary>
public sealed record EquipoVista(string Id, string Nombre, string Clase, string Tipo, string? VersionAgente, DateTime? UltimoReporte, string Conexion, bool Retirado, DateTime? RetiradoEn);

/// <summary>Punto de una serie: [ts en ms Unix, promedio, máximo].</summary>
public sealed record SerieVista(string? Unidad, IReadOnlyList<double[]> Puntos);

public sealed record HistorialVista(DateTime Desde, DateTime Hasta, IReadOnlyDictionary<string, SerieVista> Series);

/// <summary>
/// Lectura de los datos que los agentes escriben directo en Atlas (hosts, estado_actual, metricas_5m).
/// La salud se calcula con los mismos umbrales que usan los agentes: los por defecto más la configuración central.
/// Es una foto del valor actual (sin la histéresis del agente), por eso puede adelantarse un par de minutos a la alerta.
/// </summary>
public sealed partial class ServicioHosts(ContextoMongo mongo, TimeProvider reloj)
{
    /// <summary>El agente publica estado_actual cada minuto; con margen para un ciclo lento o un corte breve.</summary>
    public static readonly TimeSpan LimiteEnLinea = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan LimiteRetrasado = TimeSpan.FromMinutes(15);

    private const int MaxPuntos = 360;
    private const int MaxMetricas = 16;
    private static readonly TimeSpan CacheConfiguracion = TimeSpan.FromMinutes(1);

    private readonly IMongoCollection<BsonDocument> _hosts = mongo.Db.GetCollection<BsonDocument>("hosts");
    private readonly IMongoCollection<BsonDocument> _estado = mongo.Db.GetCollection<BsonDocument>("estado_actual");
    private readonly IMongoCollection<BsonDocument> _metricas = mongo.Db.GetCollection<BsonDocument>("metricas_5m");
    private readonly IMongoCollection<BsonDocument> _configuracion = mongo.Db.GetCollection<BsonDocument>(ConfiguracionCentral.Coleccion);

    private ConfiguracionCentral? _config;
    private DateTimeOffset _configLeida = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<HostVista>> ListarAsync(CancellationToken ct)
    {
        var hosts = await _hosts.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct).ConfigureAwait(false);
        var estados = (await _estado.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(e => e["_id"].ToString()!, StringComparer.Ordinal);
        var config = await ConfiguracionAsync(ct).ConfigureAwait(false);

        var vistas = hosts.Select(h => Construir(h, estados.GetValueOrDefault(h["_id"].ToString()!), config)).ToList();
        var alias = await AliasAsync(vistas, ct).ConfigureAwait(false);
        return [.. vistas
            .Where(v => !alias.TryGetValue(v.Id, out var a) || !a.Retirado)
            .Select(v => v with { Nombre = alias.TryGetValue(v.Id, out var a) ? a.Nombre : NuevoNombre(v) })
            .OrderBy(h => OrdenSalud(h.Salud)).ThenBy(h => h.Nombre, StringComparer.CurrentCulture)];
    }

    /// <summary>Un solo host (no retirado), con el mismo nombre que en la lista.</summary>
    public async Task<HostVista?> ObtenerAsync(string id, CancellationToken ct) =>
        (await ListarAsync(ct).ConfigureAwait(false)).FirstOrDefault(h => h.Id == id);

    /// <summary>Todos los equipos, incluidos los retirados (pantalla de administración).</summary>
    public async Task<IReadOnlyList<EquipoVista>> EquiposAsync(CancellationToken ct)
    {
        var hosts = await _hosts.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct).ConfigureAwait(false);
        var estados = (await _estado.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(e => e["_id"].ToString()!, StringComparer.Ordinal);
        var config = await ConfiguracionAsync(ct).ConfigureAwait(false);
        var vistas = hosts.Select(h => Construir(h, estados.GetValueOrDefault(h["_id"].ToString()!), config)).ToList();
        var alias = await AliasAsync(vistas, ct).ConfigureAwait(false);
        return [.. vistas
            .Select(v => alias.TryGetValue(v.Id, out var a)
                ? new EquipoVista(v.Id, a.Nombre, a.Clase, v.Tipo, v.VersionAgente, v.UltimoReporte, v.Conexion, a.Retirado, a.RetiradoEn)
                : new EquipoVista(v.Id, NuevoNombre(v), Clase(v), v.Tipo, v.VersionAgente, v.UltimoReporte, v.Conexion, false, null))
            .OrderBy(e => e.Retirado).ThenBy(e => e.Nombre, StringComparer.CurrentCulture)];
    }

    /// <summary>El admin puede dar un nombre propio (genérico: sin marcas, modelos ni nombres de personas).</summary>
    public async Task<string?> RenombrarAsync(string id, string? nombre, CancellationToken ct)
    {
        var limpio = nombre?.Trim() ?? "";
        if (!NombreValido().IsMatch(limpio)) return "Usa entre 3 y 40 letras, números, espacios o guiones.";
        var r = await mongo.HostsAlias.UpdateOneAsync(a => a.Id == id, Builders<HostAliasDoc>.Update.Set(a => a.Nombre, limpio), cancellationToken: ct).ConfigureAwait(false);
        return r.MatchedCount == 0 ? "El equipo no existe o aún no ha reportado." : null;
    }

    /// <summary>Retirar oculta el equipo de la operación diaria sin borrar su historial (se puede restaurar).</summary>
    public async Task<string?> RetirarAsync(string id, bool retirar, CancellationToken ct)
    {
        var r = await mongo.HostsAlias.UpdateOneAsync(a => a.Id == id, Builders<HostAliasDoc>.Update
            .Set(a => a.Retirado, retirar).Set(a => a.RetiradoEn, retirar ? reloj.GetUtcNow().UtcDateTime : null), cancellationToken: ct).ConfigureAwait(false);
        return r.MatchedCount == 0 ? "El equipo no existe o aún no ha reportado." : null;
    }

    /// <summary>
    /// La web nunca recibe fabricante, modelo ni nombre del equipo. La primera vez que un equipo reporta lecturas se le
    /// asigna un nombre genérico con número fijo por tipo ("Equipo portátil 2"): agregar equipos no renombra a los demás.
    /// </summary>
    private async Task<Dictionary<string, HostAliasDoc>> AliasAsync(List<HostVista> vistas, CancellationToken ct)
    {
        var alias = (await mongo.HostsAlias.Find(FilterDefinition<HostAliasDoc>.Empty).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        // Sin lecturas todavía no se sabe si es portátil o escritorio: se espera a su primer reporte.
        foreach (var v in vistas.Where(v => !alias.ContainsKey(v.Id) && v.Lecturas.Count > 0).OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            var clase = Clase(v);
            var numero = alias.Values.Where(a => a.Clase == clase).Select(a => a.Numero).DefaultIfEmpty(0).Max() + 1;
            var doc = new HostAliasDoc { Id = v.Id, Clase = clase, Numero = numero, Nombre = $"{clase} {numero}", Creado = reloj.GetUtcNow().UtcDateTime };
            try
            {
                await mongo.HostsAlias.InsertOneAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                alias[v.Id] = doc;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Otra consulta simultánea ya lo asignó.
                alias[v.Id] = await mongo.HostsAlias.Find(a => a.Id == v.Id).FirstAsync(ct).ConfigureAwait(false);
            }
        }
        return alias;
    }

    private static string NuevoNombre(HostVista v) => v.Tipo == "android" ? "Celular nuevo" : "Equipo nuevo";

    [GeneratedRegex(@"^[\p{L}\p{N} \-]{3,40}$")]
    private static partial Regex NombreValido();

    private static string Clase(HostVista h) =>
        h.Tipo == "android" ? "Celular" : h.Lecturas.ContainsKey("bateria_pct") ? "Equipo portátil" : "Equipo de escritorio";

    /// <summary>
    /// Historial de agregados de 5 min. Las claves se validan (solo letras, dígitos y '_') porque se usan en la proyección.
    /// Si hay más de <see cref="MaxPuntos"/> ventanas, se agrupan: promedio de promedios y máximo de máximos.
    /// </summary>
    public async Task<HistorialVista> HistorialAsync(string id, int horas, IEnumerable<string> claves, CancellationToken ct)
    {
        var lista = claves.Where(c => ClaveValida().IsMatch(c)).Distinct(StringComparer.Ordinal).Take(MaxMetricas).ToList();
        var hasta = reloj.GetUtcNow().UtcDateTime;
        var desde = hasta.AddHours(-Math.Clamp(horas, 1, 24 * 7));

        var proyeccion = new BsonDocument("ts", 1);
        foreach (var c in lista) proyeccion[$"metricas.{c}"] = 1;
        var docs = await _metricas
            .Find(Builders<BsonDocument>.Filter.Eq("host", id) & Builders<BsonDocument>.Filter.Gte("ts", desde))
            .Project(proyeccion)
            .Sort(Builders<BsonDocument>.Sort.Ascending("ts"))
            .ToListAsync(ct).ConfigureAwait(false);

        var series = new Dictionary<string, SerieVista>(StringComparer.Ordinal);
        foreach (var clave in lista)
        {
            string? unidad = null;
            var puntos = new List<double[]>(docs.Count);
            foreach (var d in docs)
            {
                if (!d.TryGetValue("metricas", out var m) || !m.IsBsonDocument || !m.AsBsonDocument.TryGetValue(clave, out var r) || !r.IsBsonDocument) continue;
                var resumen = r.AsBsonDocument;
                unidad ??= resumen.TryGetValue("u", out var u) && u.IsString ? u.AsString : null;
                var ts = new DateTimeOffset(d["ts"].ToUniversalTime()).ToUnixTimeMilliseconds();
                puntos.Add([ts, Numero(resumen, "avg"), Numero(resumen, "max")]);
            }
            series[clave] = new SerieVista(unidad, Reducir(puntos));
        }
        return new HistorialVista(desde, hasta, series);
    }

    private HostVista Construir(BsonDocument host, BsonDocument? estado, ConfiguracionCentral config)
    {
        var tipo = Texto(host, "tipo") ?? "windows";
        var lecturas = new Dictionary<string, double>(StringComparer.Ordinal);
        if (estado is not null && estado.TryGetValue("lecturas", out var l) && l.IsBsonDocument)
        {
            foreach (var e in l.AsBsonDocument.Where(e => e.Value.IsNumeric)) lecturas[e.Name] = e.Value.ToDouble();
        }

        DateTime? reporte = estado is not null && estado.TryGetValue("ts", out var ts) && ts.IsValidDateTime ? ts.ToUniversalTime() : null;
        var edad = reporte is null ? TimeSpan.MaxValue : reloj.GetUtcNow().UtcDateTime - reporte.Value;
        var conexion = edad <= LimiteEnLinea ? "en-linea" : edad <= LimiteRetrasado ? "retrasado" : "sin-reporte";

        var umbrales = UmbralesEfectivos(tipo, lecturas.Keys, config);
        var alertas = new List<AlertaVista>();
        foreach (var (clave, u) in umbrales)
        {
            if (!lecturas.TryGetValue(clave, out var valor)) continue;
            if (valor >= u.Excepcion) alertas.Add(new AlertaVista(clave, valor, u.Excepcion, Nombre(Severidad.Excepcion)));
            else if (valor >= u.Advertencia) alertas.Add(new AlertaVista(clave, valor, u.Advertencia, Nombre(Severidad.Advertencia)));
        }
        alertas.Sort((a, b) => string.CompareOrdinal(b.Severidad, a.Severidad) is var s and not 0 ? s : string.CompareOrdinal(a.Metrica, b.Metrica));

        // Con el reporte vencido los valores ya no describen el equipo: la salud pasa a "desconocida".
        var salud = conexion == "sin-reporte" || lecturas.Count == 0 ? "desconocida"
            : alertas.Any(a => a.Severidad == "excepcion") ? "excepcion"
            : alertas.Count > 0 ? "advertencia" : "ok";

        DateTime? arranque = host.TryGetValue("ultimo_arranque", out var a) && a.IsValidDateTime ? a.ToUniversalTime() : null;
        return new HostVista(
            // El nombre real (modelo) se descarta aquí; Anonimizar asigna el genérico.
            host["_id"].ToString()!, "", tipo, Texto(host, "version_agente"),
            arranque, reporte, conexion, salud, lecturas, umbrales, alertas);
    }

    private static Dictionary<string, UmbralVista> UmbralesEfectivos(string tipo, IEnumerable<string> claves, ConfiguracionCentral config)
    {
        var base_ = (tipo == "android" ? UmbralesPorDefecto.Android : UmbralesPorDefecto.Windows).ToList();
        // Un umbral por unidad de disco presente (disco_C_pct → disco.C.pct), como hace el agente de Windows.
        base_.AddRange(claves.Where(c => DiscoPct().IsMatch(c)).Select(c => UmbralesPorDefecto.Disco(c.Replace('_', '.'))));
        return config.Combinar(base_, tipo).ToDictionary(
            u => PublicadorAtlas.ClaveCampo(u.Metrica), u => new UmbralVista(u.Advertencia, u.Excepcion), StringComparer.Ordinal);
    }

    private async Task<ConfiguracionCentral> ConfiguracionAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        if (_config is not null && ahora - _configLeida < CacheConfiguracion) return _config;
        var doc = await _configuracion.Find(Builders<BsonDocument>.Filter.Eq("_id", ConfiguracionCentral.Documento))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        _config = doc is null ? new ConfiguracionCentral() : PublicadorAtlas.DesdeBson(doc);
        _configLeida = ahora;
        return _config;
    }

    private static List<double[]> Reducir(List<double[]> puntos)
    {
        if (puntos.Count <= MaxPuntos) return puntos;
        var tamano = (int)Math.Ceiling(puntos.Count / (double)MaxPuntos);
        return [.. puntos.Chunk(tamano).Select(g => new[] { g[0][0], g.Average(p => p[1]), g.Max(p => p[2]) })];
    }

    private static int OrdenSalud(string salud) => salud switch { "excepcion" => 0, "advertencia" => 1, "desconocida" => 2, _ => 3 };

    private static string Nombre(Severidad s) => s == Severidad.Excepcion ? "excepcion" : "advertencia";

    private static string? Texto(BsonDocument d, string campo) => d.TryGetValue(campo, out var v) && v.IsString ? v.AsString : null;

    private static double Numero(BsonDocument d, string campo) => d.TryGetValue(campo, out var v) && v.IsNumeric ? v.ToDouble() : 0;

    [GeneratedRegex("^[A-Za-z0-9_]{1,80}$")]
    private static partial Regex ClaveValida();

    [GeneratedRegex("^disco_[A-Za-z]_pct$")]
    private static partial Regex DiscoPct();
}
