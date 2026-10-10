using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SysPulse.Infrastructure.Persistencia;

/// <summary>Usuario (sección 9). Los campos sensibles se guardan cifrados o como hash; nunca en claro.</summary>
public sealed class UsuarioDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public bool EmailVerificado { get; set; }
    public string Rol { get; set; } = "N1";
    /// <summary>Nivel técnico de origen: si el admin revoca el rol gestor, el usuario vuelve a este nivel.</summary>
    public string NivelTecnico { get; set; } = "N1";
    public string PasswordHash { get; set; } = "";
    public bool DebeCambiarPassword { get; set; }
    public TelegramDoc Telegram { get; set; } = new();
    public MfaDoc Mfa { get; set; } = new();
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime Creado { get; set; }
    public DateTime? UltimaConexion { get; set; }
    /// <summary>Cambia al cambiar contraseña, rol o MFA: invalida los JWT emitidos antes.</summary>
    public string SelloSeguridad { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class TelegramDoc
{
    public string? Usuario { get; set; }
    public string? TelefonoCifrado { get; set; }
    public string? TelefonoMascara { get; set; }
    public long? ChatId { get; set; }
    public bool Verificado { get; set; }
    public DateTime? VerificadoEn { get; set; }
}

public sealed class MfaDoc
{
    public string? SecretoCifrado { get; set; }
    public bool Activo { get; set; }
    public List<string> CodigosRecuperacionHash { get; set; } = [];
    /// <summary>Último paso TOTP aceptado: impide reutilizar el mismo código.</summary>
    public long UltimoPaso { get; set; }
}

public enum TipoVerificacion { Correo, Telegram, Restablecer }

/// <summary>Token de un solo uso (correo o Telegram). TTL automático en Mongo.</summary>
public sealed class VerificacionDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId UsuarioId { get; set; }
    [BsonRepresentation(BsonType.String)] public TipoVerificacion Tipo { get; set; }
    public string TokenHash { get; set; } = "";
    /// <summary>Telegram: chat_id que inició el bot con este token (antes de confirmar identidad).</summary>
    public long? ChatId { get; set; }
    public bool IdentidadConfirmada { get; set; }
    public string? CodigoHash { get; set; }
    public int Intentos { get; set; }
    public int CodigosEnviados { get; set; }
    public DateTime Expira { get; set; }
    public DateTime Creado { get; set; }
}

/// <summary>Auditoría inmutable (solo inserción).</summary>
public sealed class AuditoriaDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId? UsuarioId { get; set; }
    public string Accion { get; set; } = "";
    public string? Entidad { get; set; }
    public string? EntidadId { get; set; }
    public string? Detalle { get; set; }
    public string? Ip { get; set; }
    public DateTime Ts { get; set; }
}
