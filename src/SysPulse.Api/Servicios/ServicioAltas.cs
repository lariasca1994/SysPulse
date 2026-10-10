using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Contracts;
using SysPulse.Infrastructure.Notificaciones;
using SysPulse.Infrastructure.Persistencia;
using SysPulse.Infrastructure.Seguridad;

namespace SysPulse.Api.Servicios;

/// <summary>Datos que recibe un equipo al darse de alta. Pueden ser credenciales de base exclusivas para agentes.</summary>
public sealed record OpcionesAlta(string? MongoWindows, string? MongoAndroid, string ApiPublica, string? AgentApiKey, string? TelegramBotToken, IReadOnlyList<string> TelegramChatIds);

public sealed record CodigoAltaVista(string Pista, string CreadoPor, DateTime Creado, DateTime Expira, string Estado, string? TipoUsado, DateTime? UsadoEn);

/// <summary>
/// Altas de equipos con código de un solo uso (ITIL: gestión de activos y configuración). El instalador y la app no llevan
/// secretos: el admin genera un código (24 h), el equipo lo canjea una vez y recibe su configuración por HTTPS.
/// Del código solo se guarda su huella: ni la base ni los registros permiten reconstruirlo.
/// </summary>
public sealed class ServicioAltas(ContextoMongo mongo, OpcionesAlta opciones, Auditoria auditoria, ColaNotificaciones cola, SysPulse.Infrastructure.Cuentas.OpcionesCuentas opcionesCuentas, ServicioChat chat, TimeProvider reloj)
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);
    // Sin caracteres que se confunden al leerlos o dictarlos (0/O, 1/I/L).
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    public async Task<(string Codigo, DateTime Expira)> CrearAsync(ObjectId adminId, string adminNombre, CancellationToken ct)
    {
        var crudo = string.Concat(Enumerable.Range(0, 8).Select(_ => Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)]));
        var codigo = $"{crudo[..4]}-{crudo[4..]}";
        var expira = Ahora + Vigencia;
        await mongo.Altas.InsertOneAsync(new AltaEquipoDoc
        {
            CodigoHash = Cifrado.Hash(crudo), Pista = crudo[4..], CreadoPor = adminId, CreadoPorNombre = adminNombre, Creado = Ahora, Expira = expira,
        }, cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("admin.codigo_alta_creado", adminId, null, $"termina en {crudo[4..]}", ct: ct).ConfigureAwait(false);
        return (codigo, expira);
    }

    public async Task<IReadOnlyList<CodigoAltaVista>> CodigosAsync(CancellationToken ct)
    {
        var ahora = Ahora;
        return [.. (await mongo.Altas.Find(FilterDefinition<AltaEquipoDoc>.Empty).SortByDescending(a => a.Creado).Limit(20).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => new CodigoAltaVista($"····-{a.Pista}", a.CreadoPorNombre, a.Creado, a.Expira,
                a.Usado ? "usado" : a.Expira < ahora ? "vencido" : "pendiente", a.TipoUsado, a.UsadoEn))];
    }

    /// <summary>Canje atómico: un código solo sirve una vez, aunque dos equipos lo envíen al mismo tiempo.</summary>
    public async Task<RespuestaAlta?> CanjearAsync(SolicitudAlta s, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        var tipo = s.TipoHost?.ToLowerInvariant();
        if (tipo is not ("windows" or "android")) return null;
        var crudo = new string((s.Codigo ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (crudo.Length != 8) return null;

        var mongoUri = tipo == "android" ? opciones.MongoAndroid : opciones.MongoWindows;
        if (string.IsNullOrWhiteSpace(mongoUri) || string.IsNullOrWhiteSpace(opciones.AgentApiKey)) return null;

        var hash = Cifrado.Hash(crudo);
        var ahora = Ahora;
        var alta = await mongo.Altas.FindOneAndUpdateAsync<AltaEquipoDoc>(
            a => a.CodigoHash == hash && !a.Usado && a.Expira > ahora,
            Builders<AltaEquipoDoc>.Update.Set(a => a.Usado, true).Set(a => a.UsadoEn, ahora).Set(a => a.TipoUsado, tipo),
            cancellationToken: ct).ConfigureAwait(false);
        if (alta is null)
        {
            await auditoria.RegistrarAsync("alta.codigo_rechazado", null, ip, tipo, ct: ct).ConfigureAwait(false);
            return null;
        }

        await auditoria.RegistrarAsync("alta.equipo_vinculado", null, ip, $"{tipo} · código ····-{alta.Pista}", ct: ct).ConfigureAwait(false);
        var plataforma = tipo == "android" ? "Celular Android" : "Equipo Windows";
        cola.Encolar(Aviso.SoloCorreo("equipo-alta", $"alta:{alta.Id}", new ContenidoCorreo(
            $"[SysPulse] Equipo vinculado: {plataforma}", "Se vinculó un equipo nuevo", "ok",
            [("Tipo", plataforma), ("Código usado", $"····-{alta.Pista}"), ("Generado por", alta.CreadoPorNombre)],
            $"{opcionesCuentas.UrlWeb}/admin", "Ver los equipos"), admins: true));
        await chat.SistemaAsync(ServicioChat.Operaciones, $"🆕 Se vinculó un equipo nuevo ({plataforma}). Aparecerá en Salud de hosts con su primer reporte.", ct).ConfigureAwait(false);
        return new RespuestaAlta(mongoUri, opciones.ApiPublica, opciones.AgentApiKey, opciones.TelegramBotToken, opciones.TelegramChatIds);
    }
}
