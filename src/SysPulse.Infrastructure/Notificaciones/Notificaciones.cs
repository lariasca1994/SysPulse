using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Domain;
using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Persistencia;
using SysPulse.Infrastructure.Telegram;

namespace SysPulse.Infrastructure.Notificaciones;

/// <summary>
/// Un aviso a enviar (sección 7, "Notificaciones externas"). Los destinatarios no se fijan aquí: se resuelven al
/// enviar leyendo los usuarios (rol, Telegram verificado), así un usuario nuevo recibe avisos desde su verificación.
/// </summary>
/// <param name="Motivo">apertura | advertencia | escalamiento | jerarquico | p1 | asignacion | cierre.</param>
/// <param name="Clave">Clave de enfriamiento (clave_dedup del evento o id del incidente + motivo).</param>
/// <param name="Severidad">advertencia | excepcion | informativo. Correo solo para excepción o P1.</param>
/// <param name="Niveles">Roles técnicos destinatarios (N1, N2, N3).</param>
/// <param name="Usuarios">Usuarios adicionales (asignado, quienes ya recibieron avisos del registro).</param>
/// <param name="CuentasFijas">Las cuentas fijas reciben excepciones y advertencias sin importar el nivel.</param>
public sealed record Aviso(
    string Motivo,
    string Clave,
    string? RegistroId,
    string Severidad,
    bool EsP1,
    IReadOnlyList<string> Niveles,
    bool Gestores,
    bool Admins,
    IReadOnlyList<ObjectId> Usuarios,
    bool CuentasFijas,
    string TextoTelegram,
    ContenidoCorreo Correo,
    TimeSpan? Enfriamiento = null);

public sealed record ContenidoCorreo(string Asunto, string Titulo, string Tono, IReadOnlyList<(string Etiqueta, string Valor)> Filas, string? Enlace, string? TextoEnlace);

/// <summary>Cola en memoria: encolar no bloquea la petición que origina el aviso.</summary>
public sealed class ColaNotificaciones
{
    private readonly Channel<Aviso> _canal = Channel.CreateBounded<Aviso>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Encolar(Aviso aviso) => _canal.Writer.TryWrite(aviso);

    public ChannelReader<Aviso> Lector => _canal.Reader;
}

public sealed record OpcionesNotificaciones(IReadOnlyList<long> ChatsFijos);

/// <summary>
/// Resuelve destinatarios y envía por Telegram y correo. Un fallo de un canal o destinatario no bloquea los demás.
/// Nunca guarda chat_id ni correos en el registro de notificaciones: solo el id del usuario o "fija".
/// </summary>
public sealed partial class DespachadorNotificaciones(
    ContextoMongo mongo,
    ClienteTelegram telegram,
    ServicioCorreo correo,
    OpcionesNotificaciones opciones,
    ILogger<DespachadorNotificaciones> logger)
{
    /// <summary>Telegram admite ~1 mensaje por segundo por chat; entre chats distintos basta una pausa corta.</summary>
    private static readonly TimeSpan PausaEntreMensajes = TimeSpan.FromMilliseconds(60);

    public async Task EnviarAsync(Aviso aviso, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        if (aviso.Enfriamiento is { } enfriamiento && await EnEnfriamientoAsync(aviso, enfriamiento, ct).ConfigureAwait(false))
        {
            LogEnfriamiento(aviso.Motivo, aviso.Clave);
            return;
        }

        var usuarios = await DestinatariosAsync(aviso, ct).ConfigureAwait(false);
        var registros = new List<NotificacionDoc>();
        var chatsUsuarios = new HashSet<long>();

        foreach (var u in usuarios.Where(u => u.Telegram is { Verificado: true, ChatId: not null }))
        {
            chatsUsuarios.Add(u.Telegram.ChatId!.Value);
            registros.Add(Registro(aviso, "telegram", u.Id.ToString(), await EnviarTelegramAsync(u.Telegram.ChatId.Value, aviso.TextoTelegram, ct).ConfigureAwait(false)));
        }

        if (aviso.CuentasFijas)
        {
            // Una cuenta fija que además es usuario registrado ya lo recibió arriba.
            foreach (var chat in opciones.ChatsFijos.Where(c => !chatsUsuarios.Contains(c)))
            {
                registros.Add(Registro(aviso, "telegram", "fija", await EnviarTelegramAsync(chat, aviso.TextoTelegram, ct).ConfigureAwait(false)));
            }
        }

        if (aviso.Severidad == "excepcion" || aviso.EsP1)
        {
            var (asunto, html, texto) = PlantillasCorreo.Aviso(aviso.Correo);
            foreach (var u in usuarios.Where(u => u.EmailVerificado))
            {
                var ok = await IntentarAsync(() => correo.EnviarAsync(u.Email, asunto, html, texto, ct)).ConfigureAwait(false);
                registros.Add(Registro(aviso, "correo", u.Id.ToString(), ok));
            }
        }

        if (registros.Count > 0) await mongo.Notificaciones.InsertManyAsync(registros, cancellationToken: ct).ConfigureAwait(false);

        // Quien recibió avisos de un incidente recibe también el de cierre.
        if (aviso.RegistroId is { } id && ObjectId.TryParse(id, out var incidenteId) && usuarios.Count > 0)
        {
            await mongo.Incidentes.UpdateOneAsync(
                Builders<IncidenteDoc>.Filter.Eq(i => i.Id, incidenteId),
                Builders<IncidenteDoc>.Update.AddToSetEach(i => i.Notificados, usuarios.Select(u => u.Id)),
                cancellationToken: ct).ConfigureAwait(false);
        }

        var entregados = registros.Count(r => r.Enviado);
        LogEnviado(aviso.Motivo, entregados, registros.Count);
    }

    private async Task<List<UsuarioDoc>> DestinatariosAsync(Aviso aviso, CancellationToken ct)
    {
        var roles = new List<string>(aviso.Niveles);
        if (aviso.Gestores) roles.Add(Roles.Gestor);
        if (aviso.Admins) roles.Add(Roles.Admin);

        var f = Builders<UsuarioDoc>.Filter;
        var filtro = f.Eq(u => u.Activo, true) & (f.In(u => u.Rol, roles) | f.In(u => u.Id, aviso.Usuarios));
        if (roles.Count == 0 && aviso.Usuarios.Count == 0) return [];
        return await mongo.Usuarios.Find(filtro).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> EnEnfriamientoAsync(Aviso aviso, TimeSpan enfriamiento, CancellationToken ct)
    {
        var desde = DateTime.UtcNow - enfriamiento;
        return await mongo.Notificaciones
            .Find(n => n.Clave == aviso.Clave && n.Motivo == aviso.Motivo && n.Ts >= desde)
            .AnyAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> EnviarTelegramAsync(long chat, string texto, CancellationToken ct)
    {
        var ok = await IntentarAsync(() => telegram.EnviarAsync(chat, texto, ct: ct)).ConfigureAwait(false);
        await Task.Delay(PausaEntreMensajes, ct).ConfigureAwait(false);
        return ok;
    }

    /// <summary>Un reintento corto ante fallos de red; si vuelve a fallar se registra como fallido y se sigue.</summary>
    private async Task<bool> IntentarAsync(Func<Task<bool>> envio)
    {
        for (var intento = 1; intento <= 2; intento++)
        {
            try
            {
                if (await envio().ConfigureAwait(false)) return true;
            }
            catch (HttpRequestException ex)
            {
                LogFalloCanal(ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                LogFalloCanal(ex.Message);
            }
            if (intento == 1) await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        return false;
    }

    private static NotificacionDoc Registro(Aviso a, string canal, string destino, bool enviado) => new()
    {
        Clave = a.Clave, RegistroId = a.RegistroId, Motivo = a.Motivo, Canal = canal, Destino = destino, Enviado = enviado, Ts = DateTime.UtcNow,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso {Motivo}: {Enviados}/{Total} entregas")]
    private partial void LogEnviado(string motivo, int enviados, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso {Motivo} omitido por enfriamiento ({Clave})")]
    private partial void LogEnfriamiento(string motivo, string clave);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló un envío de notificación: {Mensaje}")]
    private partial void LogFalloCanal(string mensaje);
}
