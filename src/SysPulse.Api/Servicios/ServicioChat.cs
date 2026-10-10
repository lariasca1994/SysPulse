using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Api.Hubs;
using SysPulse.Infrastructure.Notificaciones;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

public sealed record MensajeVista(string Id, string SalaId, string Tipo, string? AutorId, string? AutorNombre, string? AutorRol, string Texto, DateTime Ts, bool Retirado);

public sealed record SalaVista(
    string Id, string Tipo, string Titulo, string? IncidenteId, string? Codigo, string? Prioridad, string? Estado,
    bool SoloLectura, DateTime UltimaActividad, int NoLeidos, MensajeVista? Ultimo);

public sealed record PersonaConectada(string Id, string Nombre, string Rol);

/// <summary>
/// Salas de chat (sección 7, mesa de servicio ITIL): #operaciones y una sala por incidente. Los cambios del
/// incidente se registran como mensajes de sistema, así la conversación y la trazabilidad quedan juntas.
/// Los mensajes se difunden a todos los conectados (equipo pequeño): cada cliente decide qué mostrar.
/// </summary>
public sealed class ServicioChat(ContextoMongo mongo, IHubContext<ChatHub> hub, ColaNotificaciones cola, SysPulse.Infrastructure.Cuentas.OpcionesCuentas opcionesCuentas, TimeProvider reloj) : IPresencia
{
    public const string Operaciones = "operaciones";
    public const int MaxTexto = 2000;
    public static readonly TimeSpan PlazoRetirar = TimeSpan.FromMinutes(15);
    /// <summary>Salas de incidentes cerrados que se siguen listando.</summary>
    private static readonly TimeSpan VisibleTrasCierre = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, (PersonaConectada Persona, int Conexiones)> _presentes = new(StringComparer.Ordinal);

    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    public static string SalaDeIncidente(ObjectId incidenteId) => $"inc-{incidenteId}";

    // ───────────── Presencia ─────────────

    public IReadOnlyList<PersonaConectada> Conectar(PersonaConectada p)
    {
        _presentes.AddOrUpdate(p.Id, (p, 1), (_, v) => (p, v.Conexiones + 1));
        return Presentes();
    }

    public IReadOnlyList<PersonaConectada> Desconectar(string usuarioId)
    {
        if (_presentes.TryGetValue(usuarioId, out var v))
        {
            if (v.Conexiones <= 1) _presentes.TryRemove(usuarioId, out _);
            else _presentes[usuarioId] = (v.Persona, v.Conexiones - 1);
        }
        return Presentes();
    }

    public bool Conectado(string usuarioId) => _presentes.ContainsKey(usuarioId);

    public IReadOnlyList<PersonaConectada> Presentes() => [.. _presentes.Values.Select(v => v.Persona).OrderBy(p => p.Nombre, StringComparer.CurrentCulture)];

    // ───────────── Salas ─────────────

    public async Task AsegurarOperacionesAsync(CancellationToken ct)
    {
        await mongo.Salas.UpdateOneAsync(s => s.Id == Operaciones,
            Builders<SalaDoc>.Update
                .SetOnInsert(s => s.Tipo, "general").SetOnInsert(s => s.Titulo, "operaciones")
                .SetOnInsert(s => s.Creado, Ahora).SetOnInsert(s => s.UltimaActividad, Ahora),
            new UpdateOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    public async Task CrearSalaIncidenteAsync(IncidenteDoc inc, CancellationToken ct)
    {
        await mongo.Salas.UpdateOneAsync(s => s.Id == SalaDeIncidente(inc.Id),
            Builders<SalaDoc>.Update
                .SetOnInsert(s => s.Tipo, "incidente").SetOnInsert(s => s.Titulo, inc.Titulo).SetOnInsert(s => s.IncidenteId, inc.Id.ToString())
                .SetOnInsert(s => s.Creado, Ahora).Set(s => s.UltimaActividad, Ahora),
            new UpdateOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    public Task SoloLecturaAsync(ObjectId incidenteId, bool soloLectura, CancellationToken ct) =>
        mongo.Salas.UpdateOneAsync(s => s.Id == SalaDeIncidente(incidenteId), Builders<SalaDoc>.Update.Set(s => s.SoloLectura, soloLectura), cancellationToken: ct);

    public async Task<IReadOnlyList<SalaVista>> ListarAsync(ObjectId usuarioId, CancellationToken ct)
    {
        await AsegurarOperacionesAsync(ct).ConfigureAwait(false);
        var limite = Ahora - VisibleTrasCierre;
        var salas = await mongo.Salas.Find(s => s.Tipo == "general" || !s.SoloLectura || s.UltimaActividad >= limite)
            .SortByDescending(s => s.UltimaActividad).Limit(100).ToListAsync(ct).ConfigureAwait(false);

        var ids = salas.Where(s => s.IncidenteId is not null).Select(s => ObjectId.Parse(s.IncidenteId!)).ToList();
        var incidentes = ids.Count == 0 ? [] : (await mongo.Incidentes.Find(i => ids.Contains(i.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(i => i.Id.ToString());
        var lecturas = (await mongo.Lecturas.Find(l => l.UsuarioId == usuarioId).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(l => l.SalaId, l => l.UltimoLeido, StringComparer.Ordinal);

        var vistas = new List<SalaVista>(salas.Count);
        foreach (var s in salas)
        {
            var ultimo = await mongo.Mensajes.Find(m => m.SalaId == s.Id).SortByDescending(m => m.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var desde = lecturas.GetValueOrDefault(s.Id, ObjectId.Empty);
            var noLeidos = (int)await mongo.Mensajes.CountDocumentsAsync(m => m.SalaId == s.Id && m.Id > desde && m.AutorId != usuarioId, new CountOptions { Limit = 99 }, ct).ConfigureAwait(false);
            var inc = s.IncidenteId is null ? null : incidentes.GetValueOrDefault(s.IncidenteId);
            vistas.Add(new SalaVista(s.Id, s.Tipo, inc?.Titulo ?? s.Titulo, s.IncidenteId, inc?.Codigo, inc?.Prioridad, inc?.Estado,
                s.SoloLectura, s.UltimaActividad, noLeidos, ultimo is null ? null : Vista(ultimo)));
        }
        // #operaciones siempre primero.
        return [.. vistas.OrderBy(v => v.Tipo == "general" ? 0 : 1).ThenByDescending(v => v.UltimaActividad)];
    }

    public async Task<IReadOnlyList<MensajeVista>> MensajesAsync(string salaId, string? antes, CancellationToken ct)
    {
        var filtro = Builders<MensajeDoc>.Filter.Eq(m => m.SalaId, salaId);
        if (ObjectId.TryParse(antes, out var tope)) filtro &= Builders<MensajeDoc>.Filter.Lt(m => m.Id, tope);
        var docs = await mongo.Mensajes.Find(filtro).SortByDescending(m => m.Id).Limit(50).ToListAsync(ct).ConfigureAwait(false);
        docs.Reverse();
        return [.. docs.Select(Vista)];
    }

    // ───────────── Mensajes ─────────────

    public async Task<MensajeVista> EnviarAsync(string salaId, PersonaConectada autor, string? texto, CancellationToken ct)
    {
        var limpio = texto?.Trim() ?? "";
        if (limpio.Length == 0) throw new HubException("El mensaje está vacío.");
        if (limpio.Length > MaxTexto) throw new HubException($"El mensaje supera {MaxTexto} caracteres.");
        var sala = await mongo.Salas.Find(s => s.Id == salaId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? (salaId == Operaciones ? await CrearYLeerOperacionesAsync(ct).ConfigureAwait(false) : null)
            ?? throw new HubException("La sala no existe.");
        if (sala.SoloLectura) throw new HubException("El incidente está cerrado: la sala es de solo lectura.");

        var m = new MensajeDoc
        {
            SalaId = salaId, Tipo = "humano", AutorId = ObjectId.Parse(autor.Id), AutorNombre = autor.Nombre, AutorRol = autor.Rol, Texto = limpio, Ts = Ahora,
        };
        await GuardarYDifundirAsync(m, ct).ConfigureAwait(false);
        await AvisarPorCorreoAsync(sala, autor, limpio, ct).ConfigureAwait(false);
        return Vista(m);
    }

    /// <summary>Mensaje del sistema (registro ITIL del incidente). Nunca lanza: el chat no debe frenar la acción que lo origina.</summary>
    public async Task SistemaAsync(string salaId, string texto, CancellationToken ct)
    {
        try
        {
            if (salaId == Operaciones) await AsegurarOperacionesAsync(ct).ConfigureAwait(false);
            await GuardarYDifundirAsync(new MensajeDoc { SalaId = salaId, Tipo = "sistema", Texto = texto, Ts = Ahora }, ct).ConfigureAwait(false);
        }
        catch (MongoException)
        {
            // El incidente ya quedó registrado; el mensaje de sala es complementario.
        }
    }

    /// <summary>Enfriamiento del aviso por correo: como máximo uno por sala y persona cada 30 minutos.</summary>
    public static readonly TimeSpan EnfriamientoCorreoChat = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Aviso administrativo por correo de un mensaje nuevo, solo a quienes no están conectados (los conectados ya lo ven
    /// en vivo) y con enfriamiento por sala y persona, para no llenar el correo ni agotar la cuota diaria.
    /// </summary>
    private async Task AvisarPorCorreoAsync(SalaDoc sala, PersonaConectada autor, string texto, CancellationToken ct)
    {
        var conectados = _presentes.Keys.ToHashSet(StringComparer.Ordinal);
        var autorId = ObjectId.Parse(autor.Id);
        var destinatarios = await mongo.Usuarios.Find(u => u.Activo && u.EmailVerificado && u.Id != autorId).ToListAsync(ct).ConfigureAwait(false);
        var nombreSala = sala.Tipo == "general" ? "#operaciones" : sala.Titulo;
        var extracto = texto.Length > 300 ? texto[..300] + "…" : texto;
        foreach (var u in destinatarios.Where(u => !conectados.Contains(u.Id.ToString())))
        {
            var contenido = new ContenidoCorreo($"[SysPulse] Mensaje nuevo en {nombreSala}", $"Mensaje nuevo en {nombreSala}", "info",
                [("De", autor.Nombre), ("Mensaje", extracto)], $"{opcionesCuentas.UrlWeb}/salas/{sala.Id}", "Abrir la sala");
            cola.Encolar(Aviso.SoloCorreo("chat", $"chat:{sala.Id}:{u.Id}", contenido, [u.Id], enfriamiento: EnfriamientoCorreoChat));
        }
    }

    public async Task<MensajeVista> RetirarAsync(string mensajeId, string autorId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(mensajeId, out var id)) throw new HubException("Mensaje no válido.");
        var limite = Ahora - PlazoRetirar;
        var m = await mongo.Mensajes.FindOneAndUpdateAsync<MensajeDoc>(
            x => x.Id == id && x.AutorId == ObjectId.Parse(autorId) && x.Tipo == "humano" && x.Ts >= limite && !x.Retirado,
            Builders<MensajeDoc>.Update.Set(x => x.Retirado, true).Set(x => x.Texto, ""),
            new FindOneAndUpdateOptions<MensajeDoc> { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false)
            ?? throw new HubException("Solo puedes retirar tus propios mensajes, dentro de los primeros 15 minutos.");
        var vista = Vista(m);
        await hub.Clients.All.SendAsync("mensajeActualizado", vista, ct).ConfigureAwait(false);
        return vista;
    }

    public async Task MarcarLeidoAsync(string salaId, ObjectId usuarioId, string mensajeId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(mensajeId, out var id)) return;
        // Solo avanza: una pestaña vieja no "desmarca" lo ya leído.
        try
        {
            await mongo.Lecturas.UpdateOneAsync(
                l => l.Id == $"{salaId}|{usuarioId}" && l.UltimoLeido < id,
                Builders<LecturaDoc>.Update.Set(l => l.UltimoLeido, id).SetOnInsert(l => l.SalaId, salaId).SetOnInsert(l => l.UsuarioId, usuarioId),
                new UpdateOptions { IsUpsert = true }, ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Ya había una lectura más reciente: no hay nada que actualizar.
        }
    }

    private async Task<SalaDoc?> CrearYLeerOperacionesAsync(CancellationToken ct)
    {
        await AsegurarOperacionesAsync(ct).ConfigureAwait(false);
        return await mongo.Salas.Find(s => s.Id == Operaciones).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task GuardarYDifundirAsync(MensajeDoc m, CancellationToken ct)
    {
        await mongo.Mensajes.InsertOneAsync(m, cancellationToken: ct).ConfigureAwait(false);
        await mongo.Salas.UpdateOneAsync(s => s.Id == m.SalaId, Builders<SalaDoc>.Update.Set(s => s.UltimaActividad, m.Ts), cancellationToken: ct).ConfigureAwait(false);
        await hub.Clients.All.SendAsync("mensaje", Vista(m), ct).ConfigureAwait(false);
    }

    private static MensajeVista Vista(MensajeDoc m) =>
        new(m.Id.ToString(), m.SalaId, m.Tipo, m.AutorId?.ToString(), m.AutorNombre, m.AutorRol, m.Retirado ? "" : m.Texto, m.Ts, m.Retirado);
}
