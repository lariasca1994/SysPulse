using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SysPulse.Infrastructure.Persistencia;

/// <summary>
/// Incidente (secciones 9 y 11). Estado, nivel y prioridad se guardan como texto ("EnProgreso", "N2", "P3").
/// El historial es de solo agregar: estados, escalamientos, prioridad, asignaciones y comentarios.
/// </summary>
public sealed class IncidenteDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>"evento" (creado desde una excepción de un agente) o "manual".</summary>
    public string Origen { get; set; } = "manual";
    public string? HostId { get; set; }
    public string? Metrica { get; set; }
    public string? ClaveDedup { get; set; }
    public ObjectId? EventoOrigenId { get; set; }
    public double? UltimoValor { get; set; }
    /// <summary>La métrica que lo originó volvió a la normalidad (el incidente sigue abierto hasta que alguien lo resuelva).</summary>
    public bool MetricaRecuperada { get; set; }

    public string Impacto { get; set; } = "Medio";
    public string Urgencia { get; set; } = "Media";
    public string Prioridad { get; set; } = "P3";
    public string Estado { get; set; } = "Nuevo";
    public string Nivel { get; set; } = "N1";
    public ObjectId? AsignadoId { get; set; }
    public ObjectId? CreadoPor { get; set; }

    public DateTime Creado { get; set; }
    /// <summary>Inicio del conteo del SLA; se reinicia al cambiar la prioridad (sección 11).</summary>
    public DateTime InicioSla { get; set; }
    public DateTime? PrimeraRespuesta { get; set; }
    public DateTime? Resuelto { get; set; }
    public DateTime? Cerrado { get; set; }
    public List<EsperaDoc> Esperas { get; set; } = [];
    /// <summary>Avisos jerárquicos ya enviados ("respuesta-75", "resolucion-100"…): no se repiten.</summary>
    public List<string> AvisosSla { get; set; } = [];
    /// <summary>Usuarios que ya recibieron avisos de este incidente: reciben también el de cierre.</summary>
    public List<ObjectId> Notificados { get; set; } = [];

    public string? CodigoCierre { get; set; }
    public string? Resolucion { get; set; }
    public List<HistorialDoc> Historial { get; set; } = [];
    public DateTime Actualizado { get; set; }
}

public sealed class EsperaDoc
{
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
}

/// <summary>Entrada del historial. Tipo: estado | escalamiento | prioridad | asignacion | comentario | sistema | sla.</summary>
public sealed class HistorialDoc
{
    public DateTime Ts { get; set; }
    public string Tipo { get; set; } = "sistema";
    public ObjectId? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? De { get; set; }
    public string? A { get; set; }
    public string? Texto { get; set; }
}

/// <summary>Evento recibido de un agente (cambio de una alerta: abierta, escalada o cerrada).</summary>
public sealed class EventoDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Host { get; set; } = "";
    public string TipoHost { get; set; } = "";
    /// <summary>abierto | escalado | cerrado.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>advertencia | excepcion.</summary>
    public string Severidad { get; set; } = "";
    public string Metrica { get; set; } = "";
    public double Valor { get; set; }
    public double Umbral { get; set; }
    public string ClaveDedup { get; set; } = "";
    public DateTime Ts { get; set; }
    public DateTime Recibido { get; set; }
    public ObjectId? IncidenteId { get; set; }
    /// <summary>El agente ya avisó por su respaldo de Telegram (la API no respondió a tiempo).</summary>
    public bool RespaldoEnviado { get; set; }
}

/// <summary>Registro de cada aviso enviado (enfriamiento y trazabilidad). TTL de 30 días.</summary>
public sealed class NotificacionDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Clave { get; set; } = "";
    public string? RegistroId { get; set; }
    /// <summary>apertura | escalamiento | jerarquico | p1 | cierre | asignacion | advertencia.</summary>
    public string Motivo { get; set; } = "";
    public string Canal { get; set; } = "";
    /// <summary>Id del usuario o "fija" para las cuentas fijas (nunca se guarda el chat_id ni el correo).</summary>
    public string Destino { get; set; } = "";
    public bool Enviado { get; set; }
    public DateTime Ts { get; set; }
}

/// <summary>Contadores para códigos legibles (INC-0001).</summary>
public sealed class ContadorDoc
{
    [BsonId] public string Id { get; set; } = "";
    public long Valor { get; set; }
}
