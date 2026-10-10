using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Domain;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

public sealed record IndicadorSla(int Cumplidos, int Incumplidos, double? Porcentaje);

public sealed record PuntoDia(string Dia, int Creados, int Resueltos);

public sealed record PrioridadResumen(string Prioridad, int Total, IndicadorSla Resolucion);

public sealed record CandidatoProblema(string HostNombre, string Metrica, int Excepciones, DateTime Ultima, string? IncidenteAbiertoId);

public sealed record HostAnalitica(string Id, string Nombre, string Tipo, double Disponibilidad, double? CpuPromedio, double? CpuMaximo, double? RamPromedio, double? RamMaximo, int Ventanas);

public sealed record CambioVista(string Id, string Tipo, string Titulo, string? Version, string Estado, DateTime Creado);

public sealed record Analitica(
    int Dias,
    DateTime Desde,
    int IncidentesTotal,
    int Abiertos,
    double? MttaMinutos,
    double? MttrMinutos,
    IndicadorSla SlaRespuesta,
    IndicadorSla SlaResolucion,
    double? TasaEscalamiento,
    double? ResueltosEnN1,
    int Reaperturas,
    IReadOnlyDictionary<string, int> PorOrigen,
    IReadOnlyList<PrioridadResumen> PorPrioridad,
    IReadOnlyList<PuntoDia> PorDia,
    int EventosTotal,
    int EventosExcepcion,
    int EventosAdvertencia,
    IReadOnlyList<CandidatoProblema> CandidatosProblema,
    IReadOnlyList<HostAnalitica> Hosts,
    IReadOnlyList<CambioVista> Cambios);

/// <summary>
/// Indicadores ITIL v4 (sección 8): niveles de servicio (SLA, MTTA, MTTR), incidentes (volumen, escalamiento,
/// resolución en primer nivel, reaperturas), problemas (alertas repetidas = candidatos) y disponibilidad de los hosts.
/// Todo se calcula al consultar sobre datos ya guardados (sin procesos en segundo plano).
/// </summary>
public sealed class ServicioAnalitica(ContextoMongo mongo, ServicioHosts hosts, ServicioIncidentes incidentes, TimeProvider reloj)
{
    /// <summary>Problema reactivo (sección 6): 3 excepciones de la misma alerta en 7 días.</summary>
    private const int UmbralProblema = 3;
    private static readonly TimeSpan VentanaProblema = TimeSpan.FromDays(7);
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(5);

    public async Task<Analitica> CalcularAsync(int dias, CancellationToken ct)
    {
        dias = Math.Clamp(dias, 1, 90);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var desde = ahora.AddDays(-dias);
        var zona = Zona();

        var docs = await mongo.Incidentes.Find(i => i.Creado >= desde).ToListAsync(ct).ConfigureAwait(false);
        var abiertos = (int)await mongo.Incidentes.CountDocumentsAsync(
            i => i.Estado != nameof(EstadoIncidente.Resuelto) && i.Estado != nameof(EstadoIncidente.Cerrado), cancellationToken: ct).ConfigureAwait(false);

        // ── Niveles de servicio ──
        var respuesta = new List<bool>();
        var resolucion = new List<(string Prioridad, bool Cumplido)>();
        foreach (var d in docs)
        {
            var sla = incidentes.Sla(d);
            if (d.PrimeraRespuesta is not null || sla.Respuesta.Porcentaje >= 100) respuesta.Add(sla.RespuestaCumplida);
            if (d.Resuelto is not null || sla.Resolucion.Porcentaje >= 100) resolucion.Add((d.Prioridad, sla.ResolucionCumplida));
        }

        var tomados = docs.Where(d => d.PrimeraRespuesta is not null).ToList();
        var resueltos = docs.Where(d => d.Resuelto is not null).ToList();
        double? Promedio(IEnumerable<double> v) => v.Any() ? Math.Round(v.Average(), 1) : null;
        bool Escalado(IncidenteDoc d) => d.Historial.Any(h => h.Tipo == "escalamiento");

        var porPrioridad = Enum.GetNames<Prioridad>()
            .Select(p => new PrioridadResumen(p, docs.Count(d => d.Prioridad == p), Indicador(resolucion.Where(r => r.Prioridad == p).Select(r => r.Cumplido))))
            .ToList();

        var porDia = Enumerable.Range(0, dias + 1)
            .Select(i => TimeZoneInfo.ConvertTimeFromUtc(desde, zona).Date.AddDays(i))
            .Select(f => new PuntoDia(
                f.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                docs.Count(d => TimeZoneInfo.ConvertTimeFromUtc(d.Creado, zona).Date == f),
                docs.Count(d => d.Resuelto is { } r && TimeZoneInfo.ConvertTimeFromUtc(r, zona).Date == f)))
            .ToList();

        // ── Eventos y problemas ──
        var eventos = await mongo.Eventos.Find(e => e.Recibido >= desde).ToListAsync(ct).ConfigureAwait(false);
        var nombres = (await hosts.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(h => h.Id, h => h, StringComparer.Ordinal);
        var desdeProblema = ahora - VentanaProblema;
        var abiertosPorClave = (await mongo.Incidentes.Find(i => i.ClaveDedup != null && i.Estado != nameof(EstadoIncidente.Resuelto) && i.Estado != nameof(EstadoIncidente.Cerrado))
            .ToListAsync(ct).ConfigureAwait(false)).GroupBy(i => i.ClaveDedup!).ToDictionary(g => g.Key, g => g.First().Id.ToString());
        var candidatos = (await mongo.Eventos.Find(e => e.Recibido >= desdeProblema && e.Severidad == "excepcion" && e.Tipo == "abierto").ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.ClaveDedup)
            .Where(g => g.Count() >= UmbralProblema)
            .Select(g => new CandidatoProblema(
                nombres.TryGetValue(g.First().Host, out var h) ? h.Nombre : "Equipo retirado",
                Etiquetas.Nombre(g.First().Metrica), g.Count(), g.Max(e => e.Recibido), abiertosPorClave.GetValueOrDefault(g.Key)))
            .OrderByDescending(c => c.Excepciones)
            .ToList();

        // ── Disponibilidad y uso de los hosts (agregados de 5 min) ──
        var hostsAnalitica = await HostsAsync(desde, ahora, nombres, ct).ConfigureAwait(false);

        // ── Cambios (habilitación de cambios ITIL) ──
        var cambios = (await mongo.Db.GetCollection<BsonDocument>("cambios").Find(new BsonDocument("creado", new BsonDocument("$gte", desde)))
            .Sort(new BsonDocument("creado", -1)).Limit(20).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new CambioVista(c["_id"].ToString()!, Texto(c, "tipo") ?? "estandar", Texto(c, "titulo") ?? "Cambio", Texto(c, "version"),
                Texto(c, "estado") ?? "Implementado", c.TryGetValue("creado", out var f) && f.IsValidDateTime ? f.ToUniversalTime() : desde))
            .ToList();

        return new Analitica(
            dias, desde, docs.Count, abiertos,
            Promedio(tomados.Select(d => (d.PrimeraRespuesta!.Value - d.Creado).TotalMinutes)),
            Promedio(resueltos.Select(d => (d.Resuelto!.Value - d.Creado).TotalMinutes)),
            Indicador(respuesta), Indicador(resolucion.Select(r => r.Cumplido)),
            docs.Count == 0 ? null : Math.Round(100.0 * docs.Count(Escalado) / docs.Count, 1),
            resueltos.Count == 0 ? null : Math.Round(100.0 * resueltos.Count(d => d.Nivel == "N1" && !Escalado(d)) / resueltos.Count, 1),
            docs.Sum(d => d.Historial.Count(h => h.Tipo == "estado" && h.A == nameof(EstadoIncidente.Reabierto))),
            docs.GroupBy(d => d.Origen).ToDictionary(g => g.Key, g => g.Count()),
            porPrioridad, porDia,
            eventos.Count, eventos.Count(e => e.Severidad == "excepcion" && e.Tipo != "cerrado"), eventos.Count(e => e.Severidad == "advertencia" && e.Tipo != "cerrado"),
            candidatos, hostsAnalitica, cambios);
    }

    private async Task<List<HostAnalitica>> HostsAsync(DateTime desde, DateTime ahora, Dictionary<string, HostVista> nombres, CancellationToken ct)
    {
        var metricas = mongo.Db.GetCollection<BsonDocument>("metricas_5m");
        var grupos = await metricas.Aggregate()
            .Match(new BsonDocument("ts", new BsonDocument("$gte", desde)))
            .Group(new BsonDocument
            {
                { "_id", "$host" },
                { "ventanas", new BsonDocument("$sum", 1) },
                { "primera", new BsonDocument("$min", "$ts") },
                { "cpu", new BsonDocument("$avg", "$metricas.cpu_total.avg") },
                { "cpuMax", new BsonDocument("$max", "$metricas.cpu_total.max") },
                { "ram", new BsonDocument("$avg", "$metricas.ram_pct.avg") },
                { "ramMax", new BsonDocument("$max", "$metricas.ram_pct.max") },
            })
            .ToListAsync(ct).ConfigureAwait(false);

        double? N(BsonDocument g, string c) => g.TryGetValue(c, out var v) && v.IsNumeric ? Math.Round(v.ToDouble(), 1) : null;
        return [.. grupos
            .Where(g => nombres.ContainsKey(g["_id"].ToString()!))
            .Select(g =>
            {
                var id = g["_id"].ToString()!;
                // Disponibilidad desde la primera ventana del período (un host agregado después no se penaliza).
                var inicio = g["primera"].ToUniversalTime();
                var esperadas = Math.Max(1, (ahora - inicio).TotalMinutes / Ventana.TotalMinutes);
                var ventanas = g["ventanas"].ToInt32();
                return new HostAnalitica(id, nombres[id].Nombre, nombres[id].Tipo, Math.Min(100, Math.Round(100.0 * ventanas / esperadas, 1)),
                    N(g, "cpu"), N(g, "cpuMax"), N(g, "ram"), N(g, "ramMax"), ventanas);
            })
            .OrderBy(h => h.Nombre, StringComparer.CurrentCulture)];
    }

    private static IndicadorSla Indicador(IEnumerable<bool> resultados)
    {
        var lista = resultados.ToList();
        var cumplidos = lista.Count(x => x);
        return new IndicadorSla(cumplidos, lista.Count - cumplidos, lista.Count == 0 ? null : Math.Round(100.0 * cumplidos / lista.Count, 1));
    }

    private static TimeZoneInfo Zona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Environment.GetEnvironmentVariable("ZONA_HORARIA") ?? "America/Bogota");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");
        }
    }

    private static string? Texto(BsonDocument d, string campo) => d.TryGetValue(campo, out var v) && v.IsString ? v.AsString : null;
}
