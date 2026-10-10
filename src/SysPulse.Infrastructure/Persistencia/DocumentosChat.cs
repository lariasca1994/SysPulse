using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SysPulse.Infrastructure.Persistencia;

/// <summary>
/// Sala de chat (sección 7). Id legible: "operaciones" (canal general) o "inc-{id del incidente}".
/// La sala de un incidente se crea con el incidente y queda de solo lectura cuando este se cierra.
/// </summary>
public sealed class SalaDoc
{
    [BsonId] public string Id { get; set; } = "";
    /// <summary>general | incidente.</summary>
    public string Tipo { get; set; } = "general";
    public string Titulo { get; set; } = "";
    public string? IncidenteId { get; set; }
    public bool SoloLectura { get; set; }
    public DateTime Creado { get; set; }
    public DateTime UltimaActividad { get; set; }
}

/// <summary>Mensaje. Los del sistema registran el ciclo del incidente; nunca se borran físicamente.</summary>
public sealed class MensajeDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string SalaId { get; set; } = "";
    /// <summary>humano | sistema.</summary>
    public string Tipo { get; set; } = "humano";
    public ObjectId? AutorId { get; set; }
    public string? AutorNombre { get; set; }
    public string? AutorRol { get; set; }
    public string Texto { get; set; } = "";
    public DateTime Ts { get; set; }
    /// <summary>Retirado por su autor (dentro de 15 min): se muestra "mensaje retirado" y el texto se vacía.</summary>
    public bool Retirado { get; set; }
}

/// <summary>Último mensaje leído por cada persona en cada sala (para los no leídos).</summary>
public sealed class LecturaDoc
{
    [BsonId] public string Id { get; set; } = "";
    public string SalaId { get; set; } = "";
    public ObjectId UsuarioId { get; set; }
    public ObjectId UltimoLeido { get; set; }
}

/// <summary>
/// Solicitud del rol gestor (sección 12): la pide un técnico N1/N2/N3 y solo el admin la aprueba o rechaza.
/// Estado: pendiente | aprobada | rechazada | revocada. Nunca se borra.
/// </summary>
public sealed class SolicitudRolDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = "";
    public string NivelActual { get; set; } = "N1";
    public string Motivo { get; set; } = "";
    public string Estado { get; set; } = "pendiente";
    public ObjectId? ResueltoPor { get; set; }
    public string? ResueltoPorNombre { get; set; }
    public string? Comentario { get; set; }
    public DateTime Creado { get; set; }
    public DateTime? Resuelto { get; set; }
}
