using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace SysPulse.Infrastructure.Persistencia;

/// <summary>
/// Acceso a la base de SysPulse en Atlas. Los nombres de campo se guardan en snake_case (mismo estilo que los agentes).
/// </summary>
public sealed class ContextoMongo
{
    private static readonly Lock Bloqueo = new();
    private static bool _convencionesRegistradas;

    public IMongoDatabase Db { get; }
    public IMongoCollection<UsuarioDoc> Usuarios => Db.GetCollection<UsuarioDoc>("usuarios");
    public IMongoCollection<VerificacionDoc> Verificaciones => Db.GetCollection<VerificacionDoc>("verificaciones");
    public IMongoCollection<AuditoriaDoc> Auditoria => Db.GetCollection<AuditoriaDoc>("auditoria");
    public IMongoCollection<IncidenteDoc> Incidentes => Db.GetCollection<IncidenteDoc>("incidentes");
    public IMongoCollection<EventoDoc> Eventos => Db.GetCollection<EventoDoc>("eventos");
    public IMongoCollection<NotificacionDoc> Notificaciones => Db.GetCollection<NotificacionDoc>("notificaciones");
    public IMongoCollection<ContadorDoc> Contadores => Db.GetCollection<ContadorDoc>("contadores");
    public IMongoCollection<SalaDoc> Salas => Db.GetCollection<SalaDoc>("salas");
    public IMongoCollection<MensajeDoc> Mensajes => Db.GetCollection<MensajeDoc>("mensajes");
    public IMongoCollection<LecturaDoc> Lecturas => Db.GetCollection<LecturaDoc>("salas_lectura");
    public IMongoCollection<SolicitudRolDoc> SolicitudesRol => Db.GetCollection<SolicitudRolDoc>("solicitudes_rol");
    public IMongoCollection<AltaEquipoDoc> Altas => Db.GetCollection<AltaEquipoDoc>("altas_equipos");
    public IMongoCollection<HostAliasDoc> HostsAlias => Db.GetCollection<HostAliasDoc>("hosts_alias");

    /// <summary>Siguiente número de una secuencia (atómico).</summary>
    public async Task<long> SiguienteAsync(string secuencia, CancellationToken ct)
    {
        var doc = await Contadores.FindOneAndUpdateAsync(
            Builders<ContadorDoc>.Filter.Eq(c => c.Id, secuencia),
            Builders<ContadorDoc>.Update.Inc(c => c.Valor, 1),
            new FindOneAndUpdateOptions<ContadorDoc> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct).ConfigureAwait(false);
        return doc.Valor;
    }

    /// <summary>Sube el contador al menos hasta <paramref name="minimo"/> (nunca lo baja). Repara un contador perdido o reiniciado.</summary>
    public Task AsegurarMinimoAsync(string secuencia, long minimo, CancellationToken ct) =>
        Contadores.UpdateOneAsync(Builders<ContadorDoc>.Filter.Eq(c => c.Id, secuencia), Builders<ContadorDoc>.Update.Max(c => c.Valor, minimo),
            new UpdateOptions { IsUpsert = true }, ct);

    public ContextoMongo(string cadenaConexion)
    {
        RegistrarConvenciones();
        var url = MongoUrl.Create(cadenaConexion);
        var ajustes = MongoClientSettings.FromUrl(url);
        ajustes.ApplicationName = "SysPulse.Api";
        Db = new MongoClient(ajustes).GetDatabase(url.DatabaseName ?? "syspulse");
    }

    public async Task AsegurarIndicesAsync(CancellationToken ct)
    {
        await Usuarios.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<UsuarioDoc>(Builders<UsuarioDoc>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true, Name = "email_unico" }),
            new CreateIndexModel<UsuarioDoc>(Builders<UsuarioDoc>.IndexKeys.Ascending(u => u.Rol), new CreateIndexOptions { Name = "rol" }),
            new CreateIndexModel<UsuarioDoc>(
                Builders<UsuarioDoc>.IndexKeys.Ascending(u => u.Telegram.ChatId),
                new CreateIndexOptions<UsuarioDoc>
                {
                    Name = "telegram_chat_unico",
                    Unique = true,
                    PartialFilterExpression = Builders<UsuarioDoc>.Filter.Type(u => u.Telegram.ChatId, MongoDB.Bson.BsonType.Int64),
                }),
        ], ct).ConfigureAwait(false);

        await Verificaciones.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<VerificacionDoc>(Builders<VerificacionDoc>.IndexKeys.Ascending(v => v.TokenHash), new CreateIndexOptions { Name = "token" }),
            new CreateIndexModel<VerificacionDoc>(Builders<VerificacionDoc>.IndexKeys.Ascending(v => v.Expira), new CreateIndexOptions { Name = "ttl", ExpireAfter = TimeSpan.Zero }),
        ], ct).ConfigureAwait(false);

        await Auditoria.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditoriaDoc>(Builders<AuditoriaDoc>.IndexKeys.Descending(a => a.Ts), new CreateIndexOptions { Name = "ts" }), cancellationToken: ct).ConfigureAwait(false);
        // Los eventos de auditoría se depuran a los 15 días, igual que en todo el portafolio.
        await Auditoria.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditoriaDoc>(Builders<AuditoriaDoc>.IndexKeys.Ascending(a => a.Ts), new CreateIndexOptions { Name = "ttl_15d", ExpireAfter = TimeSpan.FromDays(15) }), cancellationToken: ct).ConfigureAwait(false);

        await Incidentes.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<IncidenteDoc>(Builders<IncidenteDoc>.IndexKeys.Ascending(i => i.Codigo), new CreateIndexOptions { Unique = true, Name = "codigo" }),
            new CreateIndexModel<IncidenteDoc>(Builders<IncidenteDoc>.IndexKeys.Ascending(i => i.Estado).Descending(i => i.Creado), new CreateIndexOptions { Name = "estado_creado" }),
            new CreateIndexModel<IncidenteDoc>(Builders<IncidenteDoc>.IndexKeys.Ascending(i => i.ClaveDedup).Ascending(i => i.Estado), new CreateIndexOptions { Name = "clave_estado" }),
        ], ct).ConfigureAwait(false);

        await Eventos.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<EventoDoc>(Builders<EventoDoc>.IndexKeys.Ascending(e => e.ClaveDedup).Descending(e => e.Ts), new CreateIndexOptions { Name = "clave_ts" }),
            // Los eventos crudos se conservan 90 días, igual que las métricas.
            new CreateIndexModel<EventoDoc>(Builders<EventoDoc>.IndexKeys.Ascending(e => e.Recibido), new CreateIndexOptions { Name = "ttl_90d", ExpireAfter = TimeSpan.FromDays(90) }),
        ], ct).ConfigureAwait(false);

        await Mensajes.Indexes.CreateOneAsync(
            new CreateIndexModel<MensajeDoc>(Builders<MensajeDoc>.IndexKeys.Ascending(m => m.SalaId).Descending(m => m.Id), new CreateIndexOptions { Name = "sala_id" }),
            cancellationToken: ct).ConfigureAwait(false);
        await Salas.Indexes.CreateOneAsync(
            new CreateIndexModel<SalaDoc>(Builders<SalaDoc>.IndexKeys.Descending(s => s.UltimaActividad), new CreateIndexOptions { Name = "actividad" }),
            cancellationToken: ct).ConfigureAwait(false);

        await Notificaciones.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<NotificacionDoc>(Builders<NotificacionDoc>.IndexKeys.Ascending(n => n.Clave).Descending(n => n.Ts), new CreateIndexOptions { Name = "clave_ts" }),
            new CreateIndexModel<NotificacionDoc>(Builders<NotificacionDoc>.IndexKeys.Ascending(n => n.Ts), new CreateIndexOptions { Name = "ttl_30d", ExpireAfter = TimeSpan.FromDays(30) }),
        ], ct).ConfigureAwait(false);
    }

    private static void RegistrarConvenciones()
    {
        lock (Bloqueo)
        {
            if (_convencionesRegistradas) return;
            ConventionRegistry.Register(
                "SysPulse",
                new ConventionPack { new SnakeCaseConvention(), new IgnoreExtraElementsConvention(true) },
                t => t.Namespace?.StartsWith("SysPulse", StringComparison.Ordinal) == true);
            _convencionesRegistradas = true;
        }
    }

    /// <summary>EmailVerificado → email_verificado.</summary>
    private sealed class SnakeCaseConvention : ConventionBase, IMemberMapConvention
    {
        public void Apply(MongoDB.Bson.Serialization.BsonMemberMap memberMap)
        {
            var nombre = memberMap.MemberName;
            if (nombre == "Id") return;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < nombre.Length; i++)
            {
                if (char.IsUpper(nombre[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(nombre[i]));
            }
            memberMap.SetElementName(sb.ToString());
        }
    }
}
