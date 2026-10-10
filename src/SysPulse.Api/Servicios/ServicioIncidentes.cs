using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Contracts;
using SysPulse.Domain;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Notificaciones;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

public sealed record PersonaVista(string Id, string Nombre, string Rol);

public sealed record SlaVista(double Porcentaje, DateTime Vence, bool Cumplido, bool Detenido, double MinutosConsumidos);

public sealed record EntradaHistorial(DateTime Ts, string Tipo, string? Usuario, string? De, string? A, string? Texto);

public sealed record IncidenteVista(
    string Id,
    string Codigo,
    string Titulo,
    string? Descripcion,
    string Origen,
    string? HostId,
    string? HostNombre,
    string? Metrica,
    double? UltimoValor,
    bool MetricaRecuperada,
    string Impacto,
    string Urgencia,
    string Prioridad,
    string Estado,
    string Nivel,
    PersonaVista? Asignado,
    DateTime Creado,
    DateTime? PrimeraRespuesta,
    DateTime? Resuelto,
    DateTime? Cerrado,
    SlaVista SlaRespuesta,
    SlaVista SlaResolucion,
    string? CodigoCierre,
    string? Resolucion,
    IReadOnlyList<string> Acciones,
    IReadOnlyList<EntradaHistorial>? Historial);

/// <summary>Datos opcionales de una acción (motivo, prioridad, persona asignada, cierre).</summary>
public sealed record DatosAccion(string? Texto, string? Prioridad, string? UsuarioId, string? CodigoCierre);

public sealed record NuevoIncidente(string? Titulo, string? Descripcion, string? Impacto, string? Urgencia, string? HostId);

/// <summary>
/// Incidentes propios (sección 11): creación desde eventos de excepción o manual, ciclo de vida con las reglas del
/// dominio, SLA calculado al consultar y avisos por nivel. Los avisos jerárquicos (75 % / 100 %) se evalúan al
/// consultar, al llegar un evento o cuando el agente de Windows avisa un cruce (sin procesos en segundo plano).
/// </summary>
public sealed class ServicioIncidentes(
    ContextoMongo mongo,
    ServicioHosts hosts,
    ColaNotificaciones cola,
    Auditoria auditoria,
    OpcionesCuentas opcionesCuentas,
    ServicioChat chat,
    TimeProvider reloj)
{
    /// <summary>Enfriamiento por clave de evento: una alerta intermitente no inunda Telegram ni el correo.</summary>
    public static readonly TimeSpan Enfriamiento = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Si la misma alerta vuelve antes de este plazo tras una resolución automática, se reabre el mismo incidente
    /// (es la misma falla que oscila); después de este plazo es un incidente nuevo, con su propio código.
    /// </summary>
    public static readonly TimeSpan VentanaReapertura = TimeSpan.FromMinutes(30);

    public const string CierreAutomatico = "Se normalizó sin intervención";

    private static readonly string[] EstadosAbiertos =
        [nameof(EstadoIncidente.Nuevo), nameof(EstadoIncidente.Asignado), nameof(EstadoIncidente.EnProgreso), nameof(EstadoIncidente.EnEspera), nameof(EstadoIncidente.Reabierto)];

    private static readonly TimeZoneInfo Zona = BuscarZona(Environment.GetEnvironmentVariable("ZONA_HORARIA") ?? "America/Bogota");

    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    // ───────────── Eventos de los agentes ─────────────

    public async Task<IncidenteDoc?> RecibirEventoAsync(EventoDto e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);
        var evento = new EventoDoc
        {
            Host = e.Host, TipoHost = e.TipoHost, Tipo = e.Tipo.ToLowerInvariant(), Severidad = e.Severidad.ToLowerInvariant(),
            Metrica = e.Metrica, Valor = e.Valor, Umbral = e.Umbral, ClaveDedup = e.ClaveDedup,
            Ts = e.Ts.UtcDateTime, Recibido = Ahora, RespaldoEnviado = e.RespaldoEnviado,
        };
        await mongo.Eventos.InsertOneAsync(evento, cancellationToken: ct).ConfigureAwait(false);

        var nombreHost = await NombreHostAsync(e.Host, ct).ConfigureAwait(false);
        var abierto = await mongo.Incidentes
            .Find(i => i.ClaveDedup == e.ClaveDedup && EstadosAbiertos.Contains(i.Estado))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        IncidenteDoc? resultado = null;
        if (evento.Severidad == "excepcion" && evento.Tipo is "abierto" or "escalado")
        {
            // Incidente anterior a la resolución automática: se normalizó hace rato y nadie lo tomó. Se resuelve
            // y la alerta nueva abre otro incidente, para que cada falla cuente por separado en las estadísticas.
            if (abierto is not null && SinAtender(abierto) && abierto.MetricaRecuperada && Ahora - abierto.Actualizado > VentanaReapertura)
            {
                await ResolverAutomaticoAsync(abierto, abierto.UltimoValor ?? evento.Valor, nombreHost, avisar: false, ct).ConfigureAwait(false);
                abierto = null;
            }

            if (abierto is null)
            {
                var desde = Ahora - VentanaReapertura;
                var reciente = await mongo.Incidentes
                    .Find(i => i.ClaveDedup == e.ClaveDedup && i.Estado == nameof(EstadoIncidente.Resuelto) && i.CodigoCierre == CierreAutomatico && i.Resuelto >= desde)
                    .SortByDescending(i => i.Resuelto).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                resultado = reciente is not null
                    ? await ReabrirAutomaticoAsync(reciente, evento, nombreHost, ct).ConfigureAwait(false)
                    : await CrearDesdeEventoAsync(evento, nombreHost, ct).ConfigureAwait(false);
            }
            else
            {
                resultado = await ActualizarDesdeEventoAsync(abierto, evento, ct).ConfigureAwait(false);
            }
        }
        else if (evento.Tipo == "cerrado" && abierto is not null)
        {
            resultado = await MarcarRecuperadoAsync(abierto, evento, nombreHost, ct).ConfigureAwait(false);
        }
        else if (evento.Severidad == "advertencia")
        {
            AvisarAdvertencia(evento, nombreHost);
        }

        if (resultado is not null)
        {
            await mongo.Eventos.UpdateOneAsync(x => x.Id == evento.Id, Builders<EventoDoc>.Update.Set(x => x.IncidenteId, resultado.Id), cancellationToken: ct).ConfigureAwait(false);
        }

        await RevisarSlaAsync(ct).ConfigureAwait(false);
        return resultado;
    }

    private async Task<IncidenteDoc> CrearDesdeEventoAsync(EventoDoc e, string nombreHost, CancellationToken ct)
    {
        var prioridad = PrioridadSugerida.ParaMetrica(e.Metrica);
        var ahora = Ahora;
        var inc = new IncidenteDoc
        {
            Codigo = await NuevoCodigoAsync(ct).ConfigureAwait(false),
            Titulo = $"{Etiquetas.Nombre(e.Metrica)} en {nombreHost}",
            Descripcion = $"Creado automáticamente: {Etiquetas.Nombre(e.Metrica)} llegó a {Etiquetas.Valor(e.Metrica, e.Valor)} (umbral de excepción {Etiquetas.Valor(e.Metrica, e.Umbral)}).",
            Origen = "evento", HostId = e.Host, Metrica = e.Metrica, ClaveDedup = e.ClaveDedup, EventoOrigenId = e.Id, UltimoValor = e.Valor,
            Impacto = nameof(Impacto.Medio), Urgencia = prioridad == Prioridad.P2 ? nameof(Urgencia.Alta) : nameof(Urgencia.Media),
            Prioridad = prioridad.ToString(), Estado = nameof(EstadoIncidente.Nuevo), Nivel = nameof(NivelEscalamiento.N1),
            Creado = ahora, InicioSla = ahora, Actualizado = ahora,
            Historial = [new HistorialDoc { Ts = ahora, Tipo = "sistema", Texto = $"Incidente creado por el monitoreo de {nombreHost}." }],
        };
        await mongo.Incidentes.InsertOneAsync(inc, cancellationToken: ct).ConfigureAwait(false);
        await RegistrarAperturaEnChatAsync(inc, $"Creado por el monitoreo de {nombreHost}: {Etiquetas.Nombre(e.Metrica)} en {Etiquetas.Valor(e.Metrica, e.Valor)} (umbral {Etiquetas.Valor(e.Metrica, e.Umbral)}).", ct).ConfigureAwait(false);

        var sla = Sla(inc);
        cola.Encolar(AvisoIncidente(inc, "apertura", nombreHost,
            $"🔴 Nuevo incidente {inc.Codigo} · {inc.Prioridad}",
            [
                ("Equipo", nombreHost),
                (Etiquetas.Nombre(e.Metrica), $"{Etiquetas.Valor(e.Metrica, e.Valor)} (umbral {Etiquetas.Valor(e.Metrica, e.Umbral)})"),
                ("Nivel", inc.Nivel),
                ("Responder antes de", Hora(sla.Respuesta.Vence)),
            ],
            niveles: [inc.Nivel], cuentasFijas: !e.RespaldoEnviado, enfriamiento: Enfriamiento, clave: e.ClaveDedup));
        return inc;
    }

    private async Task<IncidenteDoc> ActualizarDesdeEventoAsync(IncidenteDoc inc, EventoDoc e, CancellationToken ct)
    {
        var texto = inc.MetricaRecuperada
            ? $"La métrica volvió a superar el umbral: {Etiquetas.Valor(e.Metrica, e.Valor)}."
            : $"Nueva lectura en excepción: {Etiquetas.Valor(e.Metrica, e.Valor)}.";
        var u = Builders<IncidenteDoc>.Update
            .Set(i => i.UltimoValor, e.Valor).Set(i => i.MetricaRecuperada, false).Set(i => i.Actualizado, Ahora)
            .Push(i => i.Historial, new HistorialDoc { Ts = Ahora, Tipo = "sistema", Texto = texto });
        return await mongo.Incidentes.FindOneAndUpdateAsync(i => i.Id == inc.Id, u, new FindOneAndUpdateOptions<IncidenteDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
    }

    /// <summary>Nadie lo ha tomado ni asignado (nuevo, o reabierto por el monitoreo): el monitoreo puede resolverlo solo.</summary>
    private static bool SinAtender(IncidenteDoc inc) =>
        inc.Estado is nameof(EstadoIncidente.Nuevo) or nameof(EstadoIncidente.Reabierto) && inc.AsignadoId is null;

    /// <summary>
    /// ITIL (gestión de eventos): si la métrica vuelve a la normalidad y nadie trabajaba el incidente, el monitoreo
    /// lo resuelve con el código "Se normalizó sin intervención". Si alguien lo tiene, sigue abierto para que decida.
    /// </summary>
    private async Task<IncidenteDoc> ResolverAutomaticoAsync(IncidenteDoc inc, double valor, string nombreHost, bool avisar, CancellationToken ct)
    {
        var ahora = Ahora;
        var metrica = inc.Metrica ?? "";
        var texto = $"Resuelto automáticamente por el monitoreo: la métrica volvió a la normalidad ({Etiquetas.Valor(metrica, valor)}) sin intervención.";
        var u = Builders<IncidenteDoc>.Update
            .Set(i => i.Estado, nameof(EstadoIncidente.Resuelto)).Set(i => i.Resuelto, ahora)
            .Set(i => i.PrimeraRespuesta, inc.PrimeraRespuesta ?? ahora)
            .Set(i => i.CodigoCierre, CierreAutomatico).Set(i => i.Resolucion, texto)
            .Set(i => i.UltimoValor, valor).Set(i => i.MetricaRecuperada, true).Set(i => i.Actualizado, ahora)
            .Push(i => i.Historial, new HistorialDoc { Ts = ahora, Tipo = "estado", De = inc.Estado, A = nameof(EstadoIncidente.Resuelto), Texto = texto });
        var actualizado = await mongo.Incidentes.FindOneAndUpdateAsync(
            Builders<IncidenteDoc>.Filter.Eq(i => i.Id, inc.Id) & Builders<IncidenteDoc>.Filter.Eq(i => i.Estado, inc.Estado), u,
            new FindOneAndUpdateOptions<IncidenteDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
        if (actualizado is null) return inc; // Alguien lo tomó en ese mismo instante: se respeta su trabajo.

        await chat.SistemaAsync(ServicioChat.SalaDeIncidente(inc.Id), $"✅ {texto}", ct).ConfigureAwait(false);
        if (avisar)
        {
            cola.Encolar(AvisoIncidente(actualizado, "cierre", nombreHost,
                $"✅ {actualizado.Codigo} resuelto automáticamente",
                [("Equipo", nombreHost), (Etiquetas.Nombre(metrica), Etiquetas.Valor(metrica, valor)), ("Cierre", CierreAutomatico)],
                niveles: [], usuarios: [.. actualizado.Notificados], cuentasFijas: true, severidad: "informativo"));
        }
        return actualizado;
    }

    /// <summary>La misma alerta volvió poco después de resolverse sola: es la misma falla y se reabre el mismo incidente.</summary>
    private async Task<IncidenteDoc> ReabrirAutomaticoAsync(IncidenteDoc inc, EventoDoc e, string nombreHost, CancellationToken ct)
    {
        var ahora = Ahora;
        var texto = $"Reabierto por el monitoreo: la métrica volvió a superar el umbral ({Etiquetas.Valor(e.Metrica, e.Valor)}) poco después de normalizarse.";
        var u = Builders<IncidenteDoc>.Update
            .Set(i => i.Estado, nameof(EstadoIncidente.Reabierto)).Set(i => i.Resuelto, null)
            .Set(i => i.CodigoCierre, null).Set(i => i.Resolucion, null).Set(i => i.AvisosSla, [])
            .Set(i => i.UltimoValor, e.Valor).Set(i => i.MetricaRecuperada, false).Set(i => i.Actualizado, ahora)
            .Push(i => i.Historial, new HistorialDoc { Ts = ahora, Tipo = "estado", De = inc.Estado, A = nameof(EstadoIncidente.Reabierto), Texto = texto });
        var actualizado = await mongo.Incidentes.FindOneAndUpdateAsync(i => i.Id == inc.Id, u,
            new FindOneAndUpdateOptions<IncidenteDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
        await chat.SistemaAsync(ServicioChat.SalaDeIncidente(inc.Id), $"↩️ {texto}", ct).ConfigureAwait(false);
        cola.Encolar(AvisoIncidente(actualizado, "reapertura", nombreHost, $"↩️ {actualizado.Codigo} reabierto por el monitoreo",
            [("Equipo", nombreHost), (Etiquetas.Nombre(e.Metrica), $"{Etiquetas.Valor(e.Metrica, e.Valor)} (umbral {Etiquetas.Valor(e.Metrica, e.Umbral)})")],
            niveles: [actualizado.Nivel], cuentasFijas: !e.RespaldoEnviado, enfriamiento: Enfriamiento, clave: e.ClaveDedup));
        return actualizado;
    }

    private async Task<IncidenteDoc> MarcarRecuperadoAsync(IncidenteDoc inc, EventoDoc e, string nombreHost, CancellationToken ct)
    {
        if (SinAtender(inc)) return await ResolverAutomaticoAsync(inc, e.Valor, nombreHost, avisar: true, ct).ConfigureAwait(false);

        var u = Builders<IncidenteDoc>.Update
            .Set(i => i.UltimoValor, e.Valor).Set(i => i.MetricaRecuperada, true).Set(i => i.Actualizado, Ahora)
            .Push(i => i.Historial, new HistorialDoc { Ts = Ahora, Tipo = "sistema", Texto = $"La métrica volvió a la normalidad ({Etiquetas.Valor(e.Metrica, e.Valor)}). El incidente sigue abierto hasta que alguien lo resuelva." });
        var actualizado = await mongo.Incidentes.FindOneAndUpdateAsync(i => i.Id == inc.Id, u, new FindOneAndUpdateOptions<IncidenteDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
        await chat.SistemaAsync(ServicioChat.SalaDeIncidente(inc.Id), $"✅ La métrica volvió a la normalidad ({Etiquetas.Valor(e.Metrica, e.Valor)}). El incidente sigue abierto hasta que alguien lo resuelva.", ct).ConfigureAwait(false);

        cola.Encolar(AvisoIncidente(actualizado, "recuperacion", nombreHost,
            $"✅ {actualizado.Codigo}: la métrica se normalizó",
            [("Equipo", nombreHost), (Etiquetas.Nombre(e.Metrica), Etiquetas.Valor(e.Metrica, e.Valor)), ("Siguiente paso", "Revisar y resolver el incidente")],
            niveles: [], usuarios: [.. actualizado.Notificados], cuentasFijas: !e.RespaldoEnviado, severidad: "informativo"));
        return actualizado;
    }

    /// <summary>Las advertencias no abren incidente: solo avisan por Telegram (sección 7), con enfriamiento.</summary>
    private void AvisarAdvertencia(EventoDoc e, string nombreHost)
    {
        var cerrada = e.Tipo == "cerrado";
        var titulo = cerrada ? $"✅ Advertencia superada en {nombreHost}" : $"🟡 Advertencia en {nombreHost}";
        var linea = cerrada
            ? $"{Etiquetas.Nombre(e.Metrica)}: {Etiquetas.Valor(e.Metrica, e.Valor)}"
            : $"{Etiquetas.Nombre(e.Metrica)}: {Etiquetas.Valor(e.Metrica, e.Valor)} (umbral {Etiquetas.Valor(e.Metrica, e.Umbral)})";
        var texto = $"{titulo}\n{linea}\nHora: {Hora(e.Ts)}\n{opcionesCuentas.UrlWeb}/hosts/{Uri.EscapeDataString(e.Host)}";
        cola.Encolar(new Aviso(
            cerrada ? "advertencia-cierre" : "advertencia", e.ClaveDedup, null, "advertencia", false,
            [nameof(NivelEscalamiento.N1)], false, false, [], !e.RespaldoEnviado, texto,
            new ContenidoCorreo(titulo, titulo, "advertencia", [], null, null), Enfriamiento));
    }

    // ───────────── Consultas ─────────────

    public async Task<IReadOnlyList<IncidenteVista>> ListarAsync(Actor actor, bool soloAbiertos, CancellationToken ct)
    {
        await RevisarSlaAsync(ct).ConfigureAwait(false);
        var filtro = soloAbiertos
            ? Builders<IncidenteDoc>.Filter.In(i => i.Estado, EstadosAbiertos)
            : Builders<IncidenteDoc>.Filter.Empty;
        var docs = await mongo.Incidentes.Find(filtro).SortByDescending(i => i.Creado).Limit(200).ToListAsync(ct).ConfigureAwait(false);
        return await VistasAsync(docs, actor, conHistorial: false, ct).ConfigureAwait(false);
    }

    public async Task<IncidenteVista?> ObtenerAsync(string id, Actor actor, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        var doc = await mongo.Incidentes.Find(i => i.Id == oid).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : (await VistasAsync([doc], actor, conHistorial: true, ct).ConfigureAwait(false))[0];
    }

    public async Task<IReadOnlyList<PersonaVista>> TecnicosAsync(CancellationToken ct)
    {
        var roles = new[] { Roles.N1, Roles.N2, Roles.N3, Roles.Admin };
        var usuarios = await mongo.Usuarios.Find(u => u.Activo && roles.Contains(u.Rol)).SortBy(u => u.Nombre).ToListAsync(ct).ConfigureAwait(false);
        return [.. usuarios.Select(u => new PersonaVista(u.Id.ToString(), u.Nombre, u.Rol))];
    }

    // ───────────── Acciones ─────────────

    public async Task<IncidenteVista> CrearManualAsync(NuevoIncidente n, Actor actor, string nombreActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(n);
        var titulo = n.Titulo?.Trim() ?? "";
        if (titulo.Length is < 5 or > 140) throw new ValidacionException("El título debe tener entre 5 y 140 caracteres.");
        if (!Enum.TryParse<Impacto>(n.Impacto, out var impacto) || !Enum.TryParse<Urgencia>(n.Urgencia, out var urgencia))
            throw new ValidacionException("Elige impacto y urgencia.");
        var prioridad = MatrizPrioridad.Calcular(impacto, urgencia);
        if (prioridad == Prioridad.P1 && !actor.EsGestor && !actor.EsAdmin)
            throw new ValidacionException("Un P1 (impacto alto y urgencia alta) solo lo declara el gestor. Ajusta impacto o urgencia.");

        var ahora = Ahora;
        var inc = new IncidenteDoc
        {
            Codigo = await NuevoCodigoAsync(ct).ConfigureAwait(false),
            Titulo = titulo, Descripcion = string.IsNullOrWhiteSpace(n.Descripcion) ? null : n.Descripcion.Trim()[..Math.Min(n.Descripcion.Trim().Length, 4000)],
            Origen = "manual", HostId = string.IsNullOrWhiteSpace(n.HostId) ? null : n.HostId,
            Impacto = impacto.ToString(), Urgencia = urgencia.ToString(), Prioridad = prioridad.ToString(),
            Estado = nameof(EstadoIncidente.Nuevo), Nivel = nameof(NivelEscalamiento.N1),
            CreadoPor = ObjectId.Parse(actor.Id), Creado = ahora, InicioSla = ahora, Actualizado = ahora,
            Historial = [new HistorialDoc { Ts = ahora, Tipo = "sistema", UsuarioId = ObjectId.Parse(actor.Id), UsuarioNombre = nombreActor, Texto = "Registró el incidente." }],
        };
        await mongo.Incidentes.InsertOneAsync(inc, cancellationToken: ct).ConfigureAwait(false);
        await RegistrarAperturaEnChatAsync(inc, $"Registrado manualmente por {nombreActor}.", ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("incidente.crear", ObjectId.Parse(actor.Id), null, inc.Codigo, "incidente", inc.Id.ToString(), ct).ConfigureAwait(false);

        var nombreHost = inc.HostId is null ? null : await NombreHostAsync(inc.HostId, ct).ConfigureAwait(false);
        cola.Encolar(AvisoIncidente(inc, prioridad == Prioridad.P1 ? "p1" : "apertura", nombreHost,
            $"🔴 Nuevo incidente {inc.Codigo} · {inc.Prioridad}",
            [("Título", inc.Titulo), ("Registrado por", nombreActor), ("Nivel", inc.Nivel), ("Responder antes de", Hora(Sla(inc).Respuesta.Vence))],
            niveles: [inc.Nivel], cuentasFijas: true));
        return (await VistasAsync([inc], actor, true, ct).ConfigureAwait(false))[0];
    }

    public async Task<IncidenteVista> AplicarAsync(string id, AccionIncidente accion, DatosAccion datos, Actor actor, string nombreActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (!ObjectId.TryParse(id, out var oid)) throw new NoEncontradoException();
        var inc = await mongo.Incidentes.Find(i => i.Id == oid).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw new NoEncontradoException();

        Prioridad? prioridadNueva = Enum.TryParse<Prioridad>(datos.Prioridad, out var p) ? p : null;
        var error = ReglasIncidente.Validar(accion, Situacion(inc), actor, reloj.GetUtcNow(), prioridadNueva);
        if (error is not null) throw new ReglaException(error);

        var ahora = Ahora;
        var texto = datos.Texto?.Trim();
        var actorId = ObjectId.Parse(actor.Id);
        var estadoAnterior = inc.Estado;
        var destino = ReglasIncidente.Destino(accion, Enum.Parse<EstadoIncidente>(inc.Estado)).ToString();
        var u = Builders<IncidenteDoc>.Update.Set(i => i.Estado, destino).Set(i => i.Actualizado, ahora);
        var h = new HistorialDoc { Ts = ahora, UsuarioId = actorId, UsuarioNombre = nombreActor, Tipo = "estado", De = estadoAnterior, A = destino, Texto = texto };
        Aviso? aviso = null;
        var nombreHost = inc.HostId is null ? null : await NombreHostAsync(inc.HostId, ct).ConfigureAwait(false);

        switch (accion)
        {
            case AccionIncidente.Comentar:
                if (string.IsNullOrWhiteSpace(texto)) throw new ValidacionException("Escribe el comentario.");
                h.Tipo = "comentario"; h.De = h.A = null;
                break;

            case AccionIncidente.Tomar:
                u = u.Set(i => i.AsignadoId, actorId);
                if (inc.PrimeraRespuesta is null) u = u.Set(i => i.PrimeraRespuesta, ahora);
                h.Texto = "Tomó el incidente.";
                break;

            case AccionIncidente.Asignar:
            {
                if (!ObjectId.TryParse(datos.UsuarioId, out var destinoId)) throw new ValidacionException("Elige a quién asignar.");
                var persona = await mongo.Usuarios.Find(x => x.Id == destinoId && x.Activo).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw new ValidacionException("Esa persona no existe o está inactiva.");
                if (persona.Rol == Roles.Gestor) throw new ValidacionException("El gestor no resuelve técnicamente: asigna a un técnico.");
                u = u.Set(i => i.AsignadoId, destinoId);
                if (inc.PrimeraRespuesta is null) u = u.Set(i => i.PrimeraRespuesta, ahora);
                h.Tipo = "asignacion"; h.A = persona.Nombre; h.De = null;
                aviso = AvisoIncidente(inc, "asignacion", nombreHost, $"📌 Te asignaron {inc.Codigo} · {inc.Prioridad}",
                    [("Título", inc.Titulo), ("Asignado por", nombreActor)], niveles: [], usuarios: [destinoId], cuentasFijas: false, severidad: "informativo");
                break;
            }

            case AccionIncidente.Pausar:
                if (string.IsNullOrWhiteSpace(texto)) throw new ValidacionException("Indica qué se está esperando (pausa el SLA).");
                u = u.Push(i => i.Esperas, new EsperaDoc { Inicio = ahora });
                break;

            case AccionIncidente.Reanudar:
                inc.Esperas.Where(x => x.Fin is null).ToList().ForEach(x => x.Fin = ahora);
                u = u.Set(i => i.Esperas, inc.Esperas);
                break;

            case AccionIncidente.Escalar:
            {
                if (string.IsNullOrWhiteSpace(texto)) throw new ValidacionException("Explica por qué se escala.");
                var siguiente = ReglasIncidente.Siguiente(Enum.Parse<NivelEscalamiento>(inc.Nivel)).ToString();
                // Si estaba En espera, la pausa termina: el nuevo nivel retoma el SLA.
                inc.Esperas.Where(x => x.Fin is null).ToList().ForEach(x => x.Fin = ahora);
                u = u.Set(i => i.Nivel, siguiente).Set(i => i.AsignadoId, null).Set(i => i.Esperas, inc.Esperas);
                h.Tipo = "escalamiento"; h.De = inc.Nivel; h.A = siguiente;
                var previos = inc.AsignadoId is { } a ? new List<ObjectId> { a } : [];
                aviso = AvisoIncidente(inc, "escalamiento", nombreHost, $"⬆️ {inc.Codigo} escalado a {siguiente} · {inc.Prioridad}",
                    [("Título", inc.Titulo), ("Escalado por", nombreActor), ("Motivo", texto)], niveles: [siguiente], usuarios: previos, cuentasFijas: true);
                break;
            }

            case AccionIncidente.CambiarPrioridad:
                if (string.IsNullOrWhiteSpace(texto)) throw new ValidacionException("Justifica el cambio de prioridad.");
                // El SLA se recalcula desde el momento del cambio (sección 11).
                u = u.Set(i => i.Prioridad, prioridadNueva!.Value.ToString()).Set(i => i.InicioSla, ahora).Set(i => i.AvisosSla, []);
                h.Tipo = "prioridad"; h.De = inc.Prioridad; h.A = prioridadNueva.Value.ToString();
                if (prioridadNueva == Prioridad.P1)
                {
                    aviso = AvisoIncidente(inc, "p1", nombreHost, $"🚨 {inc.Codigo} declarado P1",
                        [("Título", inc.Titulo), ("Declarado por", nombreActor), ("Motivo", texto)], niveles: [inc.Nivel], cuentasFijas: true, p1: true);
                }
                break;

            case AccionIncidente.Resolver:
            {
                if (string.IsNullOrWhiteSpace(datos.CodigoCierre)) throw new ValidacionException("Elige el código de cierre.");
                if (string.IsNullOrWhiteSpace(texto) || texto.Length < 10) throw new ValidacionException("Describe la solución (mínimo 10 caracteres).");
                u = u.Set(i => i.Resuelto, ahora).Set(i => i.CodigoCierre, datos.CodigoCierre.Trim()).Set(i => i.Resolucion, texto);
                var interesados = new List<ObjectId>(inc.Notificados);
                if (inc.AsignadoId is { } a) interesados.Add(a);
                aviso = AvisoIncidente(inc, "cierre", nombreHost, $"✅ {inc.Codigo} resuelto",
                    [("Título", inc.Titulo), ("Resuelto por", nombreActor), ("Cierre", datos.CodigoCierre.Trim()), ("Solución", texto)],
                    niveles: [], usuarios: interesados, cuentasFijas: true, severidad: "informativo");
                break;
            }

            case AccionIncidente.Cerrar:
                u = u.Set(i => i.Cerrado, ahora);
                break;

            case AccionIncidente.Reabrir:
                if (string.IsNullOrWhiteSpace(texto)) throw new ValidacionException("Explica por qué se reabre.");
                u = u.Set(i => i.Resuelto, null).Set(i => i.CodigoCierre, null).Set(i => i.Resolucion, null).Set(i => i.AvisosSla, []);
                aviso = AvisoIncidente(inc, "reapertura", nombreHost, $"↩️ {inc.Codigo} reabierto",
                    [("Título", inc.Titulo), ("Reabierto por", nombreActor), ("Motivo", texto)],
                    niveles: [inc.Nivel], usuarios: inc.AsignadoId is { } r ? [r] : [], cuentasFijas: true);
                break;
        }

        u = u.Push(i => i.Historial, h);
        // Control de concurrencia: si otra persona cambió el estado entre la lectura y esta escritura, no se aplica.
        var actualizado = await mongo.Incidentes.FindOneAndUpdateAsync(
            Builders<IncidenteDoc>.Filter.Eq(i => i.Id, oid) & Builders<IncidenteDoc>.Filter.Eq(i => i.Estado, estadoAnterior),
            u, new FindOneAndUpdateOptions<IncidenteDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false)
            ?? throw new ReglaException("Otra persona actualizó este incidente al mismo tiempo. Recarga e inténtalo de nuevo.");

        if (accion != AccionIncidente.Comentar)
        {
            await auditoria.RegistrarAsync($"incidente.{accion.ToString().ToLowerInvariant()}", actorId, null,
                $"{inc.Codigo}: {estadoAnterior} → {destino}{(texto is null ? "" : $" · {texto}")}", "incidente", inc.Id.ToString(), ct).ConfigureAwait(false);
        }
        if (aviso is not null) cola.Encolar(aviso);
        await RegistrarAccionEnChatAsync(actualizado, accion, nombreActor, texto, h, ct).ConfigureAwait(false);
        return (await VistasAsync([actualizado], actor, true, ct).ConfigureAwait(false))[0];
    }

    // ───────────── SLA ─────────────

    /// <summary>
    /// Avisos jerárquicos pendientes (75 % y 100 %) de los incidentes abiertos. Cada aviso se marca de forma atómica
    /// antes de encolarlo, así dos consultas simultáneas no lo envían dos veces.
    /// </summary>
    public async Task<int> RevisarSlaAsync(CancellationToken ct)
    {
        var abiertos = await mongo.Incidentes.Find(i => EstadosAbiertos.Contains(i.Estado)).ToListAsync(ct).ConfigureAwait(false);

        // Incidentes que se normalizaron hace rato sin que nadie los tomara (p. ej. los anteriores a la resolución
        // automática): se resuelven solos y dejan de consumir SLA.
        foreach (var inc in abiertos.Where(i => SinAtender(i) && i.MetricaRecuperada && Ahora - i.Actualizado > VentanaReapertura).ToList())
        {
            var resuelto = await ResolverAutomaticoAsync(inc, inc.UltimoValor ?? 0, await NombreHostAsync(inc.HostId ?? "", ct).ConfigureAwait(false), avisar: false, ct).ConfigureAwait(false);
            if (resuelto.Estado == nameof(EstadoIncidente.Resuelto)) abiertos.Remove(inc);
        }

        var enviados = 0;
        foreach (var inc in abiertos)
        {
            var sla = Sla(inc);
            foreach (var marca in SlaDeIncidente.AvisosPendientes(sla, true, inc.PrimeraRespuesta is not null, inc.AvisosSla))
            {
                var r = await mongo.Incidentes.UpdateOneAsync(
                    Builders<IncidenteDoc>.Filter.Eq(i => i.Id, inc.Id) & Builders<IncidenteDoc>.Filter.Ne("avisos_sla", marca),
                    Builders<IncidenteDoc>.Update.AddToSet(i => i.AvisosSla, marca)
                        .Push(i => i.Historial, new HistorialDoc { Ts = Ahora, Tipo = "sla", Texto = TextoSla(marca) }),
                    cancellationToken: ct).ConfigureAwait(false);
                if (r.ModifiedCount == 0) continue;

                var nombreHost = inc.HostId is null ? null : await NombreHostAsync(inc.HostId, ct).ConfigureAwait(false);
                var vencido = marca.EndsWith("-100", StringComparison.Ordinal);
                var estadoSla = marca.StartsWith("respuesta", StringComparison.Ordinal) ? sla.Respuesta : sla.Resolucion;
                cola.Encolar(AvisoIncidente(inc, "jerarquico", nombreHost,
                    $"{(vencido ? "⛔" : "⏰")} {inc.Codigo}: {TextoSla(marca)}",
                    [("Título", inc.Titulo), ("Nivel", inc.Nivel), ("Vence", Hora(estadoSla.Vence))],
                    niveles: [], usuarios: inc.AsignadoId is { } a ? [a] : [], cuentasFijas: true, gestores: true));
                await chat.SistemaAsync(ServicioChat.SalaDeIncidente(inc.Id), $"{(vencido ? "⛔" : "⏰")} SLA: {TextoSla(marca)}.", ct).ConfigureAwait(false);
                if (vencido) await chat.SistemaAsync(ServicioChat.Operaciones, $"⛔ {inc.Codigo} ({inc.Prioridad}): {TextoSla(marca)}.", ct).ConfigureAwait(false);
                enviados++;
            }
        }
        return enviados;
    }

    private static string TextoSla(string marca) => marca switch
    {
        "respuesta-75" => "75 % del tiempo de respuesta sin que nadie lo tome",
        "respuesta-100" => "venció el tiempo de respuesta; escalamiento al gestor",
        "resolucion-75" => "75 % del tiempo de resolución consumido",
        _ => "venció el tiempo de resolución; escalamiento al gestor",
    };

    // ───────────── Registro en el chat (mesa de servicio) ─────────────

    private async Task RegistrarAperturaEnChatAsync(IncidenteDoc inc, string origen, CancellationToken ct)
    {
        await chat.CrearSalaIncidenteAsync(inc, ct).ConfigureAwait(false);
        await chat.SistemaAsync(ServicioChat.SalaDeIncidente(inc.Id),
            $"🔴 {inc.Codigo} · {inc.Prioridad} · nivel {inc.Nivel}. {origen} Responder antes de {Hora(Sla(inc).Respuesta.Vence)}.", ct).ConfigureAwait(false);
        await chat.SistemaAsync(ServicioChat.Operaciones, $"🔴 Nuevo incidente {inc.Codigo} ({inc.Prioridad}): {inc.Titulo}", ct).ConfigureAwait(false);
    }

    private async Task RegistrarAccionEnChatAsync(IncidenteDoc inc, AccionIncidente accion, string quien, string? texto, HistorialDoc h, CancellationToken ct)
    {
        var sala = ServicioChat.SalaDeIncidente(inc.Id);
        var motivo = string.IsNullOrWhiteSpace(texto) ? "" : $": {texto}";
        var mensaje = accion switch
        {
            AccionIncidente.Tomar => $"✋ {quien} tomó el incidente.",
            AccionIncidente.Asignar => $"📌 {quien} asignó el incidente a {h.A}.",
            AccionIncidente.Iniciar => $"▶️ {quien} inició el trabajo.",
            AccionIncidente.Pausar => $"⏸️ {quien} lo puso en espera{motivo}. El SLA de resolución queda en pausa.",
            AccionIncidente.Reanudar => $"▶️ {quien} reanudó el trabajo.",
            AccionIncidente.Escalar => $"⬆️ {quien} escaló el incidente de {h.De} a {h.A}{motivo}",
            AccionIncidente.CambiarPrioridad => $"🚩 {quien} cambió la prioridad de {h.De} a {h.A}{motivo}",
            AccionIncidente.Resolver => $"✅ {quien} resolvió el incidente ({inc.CodigoCierre}){motivo}",
            AccionIncidente.Cerrar => $"🔒 {quien} cerró el incidente. La sala queda de solo lectura.",
            AccionIncidente.Reabrir => $"↩️ {quien} reabrió el incidente{motivo}",
            _ => null, // Los comentarios quedan en el historial formal, no se duplican en el chat.
        };
        if (mensaje is null) return;
        await chat.SistemaAsync(sala, mensaje, ct).ConfigureAwait(false);

        // En #operaciones solo lo que el equipo completo debe saber.
        var general = accion switch
        {
            AccionIncidente.Escalar => $"⬆️ {inc.Codigo} escalado a {h.A}: {inc.Titulo}",
            AccionIncidente.CambiarPrioridad when h.A == nameof(Prioridad.P1) => $"🚨 {inc.Codigo} declarado P1: {inc.Titulo}",
            AccionIncidente.Resolver => $"✅ {inc.Codigo} resuelto por {quien}: {inc.Titulo}",
            AccionIncidente.Reabrir => $"↩️ {inc.Codigo} reabierto: {inc.Titulo}",
            _ => null,
        };
        if (general is not null) await chat.SistemaAsync(ServicioChat.Operaciones, general, ct).ConfigureAwait(false);

        if (accion == AccionIncidente.Cerrar) await chat.SoloLecturaAsync(inc.Id, true, ct).ConfigureAwait(false);
        if (accion == AccionIncidente.Reabrir) await chat.SoloLecturaAsync(inc.Id, false, ct).ConfigureAwait(false);
    }

    // ───────────── Apoyo ─────────────

    private Aviso AvisoIncidente(IncidenteDoc inc, string motivo, string? nombreHost, string titulo, IReadOnlyList<(string, string)> filas,
        IReadOnlyList<string> niveles, IReadOnlyList<ObjectId>? usuarios = null, bool cuentasFijas = true, string severidad = "excepcion",
        bool gestores = false, bool p1 = false, TimeSpan? enfriamiento = null, string? clave = null)
    {
        var esP1 = p1 || inc.Prioridad == nameof(Prioridad.P1);
        var enlace = $"{opcionesCuentas.UrlWeb}/incidentes/{inc.Id}";
        var lineas = string.Join("\n", filas.Select(f => $"{f.Item1}: {f.Item2}"));
        var texto = $"{titulo}\n{inc.Titulo}\n{lineas}\n{enlace}";
        var tono = severidad == "informativo" ? (motivo is "cierre" or "recuperacion" ? "ok" : "info") : motivo == "jerarquico" ? "advertencia" : "critico";
        return new Aviso(
            motivo, clave ?? $"{inc.Id}:{motivo}", inc.Id.ToString(), severidad, esP1 && severidad != "informativo",
            niveles, gestores || esP1, esP1, usuarios ?? [], cuentasFijas, texto,
            new ContenidoCorreo($"[SysPulse] {titulo}", titulo, tono, [("Incidente", inc.Titulo), .. filas, ("Prioridad", inc.Prioridad)], enlace, "Abrir el incidente"),
            enfriamiento);
    }

    private async Task<List<IncidenteVista>> VistasAsync(IReadOnlyList<IncidenteDoc> docs, Actor actor, bool conHistorial, CancellationToken ct)
    {
        var idsPersonas = docs.Where(d => d.AsignadoId is not null).Select(d => d.AsignadoId!.Value).Distinct().ToList();
        var personas = idsPersonas.Count == 0 ? [] : (await mongo.Usuarios.Find(u => idsPersonas.Contains(u.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(u => u.Id, u => new PersonaVista(u.Id.ToString(), u.Nombre, u.Rol));
        var nombres = (await hosts.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(h => h.Id, h => h.Nombre, StringComparer.Ordinal);
        var ahora = reloj.GetUtcNow();

        return [.. docs.Select(d =>
        {
            var sla = Sla(d);
            var situacion = Situacion(d);
            var acciones = Enum.GetValues<AccionIncidente>()
                .Where(a => a == AccionIncidente.CambiarPrioridad
                    ? ReglasIncidente.Validar(a, situacion, actor, ahora, d.Prioridad == "P3" ? Prioridad.P2 : Prioridad.P3) is null
                    : ReglasIncidente.Validar(a, situacion, actor, ahora) is null)
                .Select(a => a.ToString()).ToList();
            return new IncidenteVista(
                d.Id.ToString(), d.Codigo, d.Titulo, d.Descripcion, d.Origen, d.HostId,
                d.HostId is null ? null : nombres.GetValueOrDefault(d.HostId, "Equipo retirado"),
                d.Metrica, d.UltimoValor, d.MetricaRecuperada, d.Impacto, d.Urgencia, d.Prioridad, d.Estado, d.Nivel,
                d.AsignadoId is { } a ? personas.GetValueOrDefault(a) : null,
                d.Creado, d.PrimeraRespuesta, d.Resuelto, d.Cerrado,
                Vista(sla.Respuesta, sla.RespuestaCumplida, d.PrimeraRespuesta is not null),
                Vista(sla.Resolucion, sla.ResolucionCumplida, d.Resuelto is not null || d.Estado == nameof(EstadoIncidente.EnEspera)),
                d.CodigoCierre, d.Resolucion, acciones,
                conHistorial ? [.. d.Historial.Select(x => new EntradaHistorial(x.Ts, x.Tipo, x.UsuarioNombre, Legible(x.De), Legible(x.A), x.Texto))] : null);
        })];
    }

    private static SlaVista Vista(EstadoSla s, bool cumplido, bool detenido) =>
        new(Math.Round(s.Porcentaje, 1), s.Vence.UtcDateTime, cumplido, detenido, Math.Round(s.Consumido.TotalMinutes, 1));

    /// <summary>Estado interno (EnProgreso) a texto legible para el historial; otros valores pasan tal cual.</summary>
    private static string? Legible(string? valor) => Enum.TryParse<EstadoIncidente>(valor, out var e) ? ReglasIncidente.Nombre(e) : valor;

    private static SituacionIncidente Situacion(IncidenteDoc d) => new(
        Enum.Parse<EstadoIncidente>(d.Estado), Enum.Parse<NivelEscalamiento>(d.Nivel), Enum.Parse<Prioridad>(d.Prioridad),
        d.AsignadoId?.ToString(), d.Resuelto is { } r ? new DateTimeOffset(r, TimeSpan.Zero) : null);

    public SlaIncidente Sla(IncidenteDoc d) => SlaDeIncidente.Calcular(new TiemposIncidente(
        new DateTimeOffset(d.InicioSla, TimeSpan.Zero), Enum.Parse<Prioridad>(d.Prioridad),
        d.PrimeraRespuesta is { } pr ? new DateTimeOffset(pr, TimeSpan.Zero) : null,
        d.Resuelto is { } r ? new DateTimeOffset(r, TimeSpan.Zero) : null,
        [.. d.Esperas.Select(e => new PeriodoEspera(new DateTimeOffset(e.Inicio, TimeSpan.Zero), e.Fin is { } f ? new DateTimeOffset(f, TimeSpan.Zero) : null))]),
        reloj.GetUtcNow());

    /// <summary>
    /// Código correlativo e irrepetible (INC-0001, INC-0002…). Si el contador se perdió o quedó atrasado, se alinea con el
    /// mayor código existente antes de reintentar; el índice único sobre el código es la última barrera.
    /// </summary>
    private async Task<string> NuevoCodigoAsync(CancellationToken ct)
    {
        for (var intento = 0; ; intento++)
        {
            var codigo = $"INC-{(await mongo.SiguienteAsync("incidentes", ct).ConfigureAwait(false)).ToString("0000", CultureInfo.InvariantCulture)}";
            if (intento >= 3 || !await mongo.Incidentes.Find(i => i.Codigo == codigo).AnyAsync(ct).ConfigureAwait(false)) return codigo;
            var recientes = await mongo.Incidentes.Find(FilterDefinition<IncidenteDoc>.Empty).SortByDescending(i => i.Id).Limit(200)
                .Project(i => i.Codigo).ToListAsync(ct).ConfigureAwait(false);
            var mayor = recientes.Select(c => long.TryParse(c?.Replace("INC-", "", StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
            await mongo.AsegurarMinimoAsync("incidentes", mayor, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> NombreHostAsync(string hostId, CancellationToken ct) =>
        (await hosts.ListarAsync(ct).ConfigureAwait(false)).FirstOrDefault(h => h.Id == hostId)?.Nombre ?? "Equipo monitoreado";

    private static string Hora(DateTimeOffset ts) => Hora(ts.UtcDateTime);

    private static string Hora(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zona);
        return local.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo BuscarZona(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");
        }
    }
}

public class ReglaException(string mensaje) : Exception(mensaje);

public sealed class ValidacionException(string mensaje) : ReglaException(mensaje);

public sealed class NoEncontradoException() : Exception("El incidente no existe.");
