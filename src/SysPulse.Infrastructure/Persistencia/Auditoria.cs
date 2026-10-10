using MongoDB.Bson;

namespace SysPulse.Infrastructure.Persistencia;

/// <summary>Registro de auditoría de solo inserción (sección 12, "Protección contra daños").</summary>
public sealed class Auditoria(ContextoMongo contexto)
{
    public Task RegistrarAsync(string accion, ObjectId? usuarioId, string? ip, string? detalle = null, string? entidad = null, string? entidadId = null, CancellationToken ct = default) =>
        contexto.Auditoria.InsertOneAsync(new AuditoriaDoc
        {
            Accion = accion,
            UsuarioId = usuarioId,
            Ip = ip,
            Detalle = detalle,
            Entidad = entidad,
            EntidadId = entidadId,
            Ts = DateTime.UtcNow,
        }, cancellationToken: ct);
}
