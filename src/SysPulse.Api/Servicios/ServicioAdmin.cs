using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Configuracion;
using SysPulse.Agent.Core.Envio;
using SysPulse.Domain;
using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Notificaciones;
using SysPulse.Infrastructure.Persistencia;
using SysPulse.Infrastructure.Telegram;

namespace SysPulse.Api.Servicios;

public sealed record UsuarioAdminVista(string Id, string Nombre, string Email, string Rol, string NivelTecnico, bool Activo,
    bool TelegramVerificado, bool MfaActivo, DateTime Creado, DateTime? UltimaConexion);

public sealed record SolicitudVista(string Id, string UsuarioId, string UsuarioNombre, string NivelActual, string Motivo, string Estado,
    string? ResueltoPorNombre, string? Comentario, DateTime Creado, DateTime? Resuelto);

public sealed record UmbralVistaAdmin(string Metrica, string Nombre, string? TipoHost, double Advertencia, double Excepcion, double Recuperacion, bool Personalizado,
    double DefectoAdvertencia, double DefectoExcepcion, double DefectoRecuperacion);

public sealed record ConfiguracionAlertasVista(int PersistenciaMinutos, IReadOnlyList<UmbralVistaAdmin> Umbrales);

public sealed record UmbralEntrada(string? Metrica, string? TipoHost, double Advertencia, double Excepcion, double Recuperacion);

public sealed record ConfiguracionAlertasEntrada(int PersistenciaMinutos, IReadOnlyList<UmbralEntrada>? Umbrales);

/// <summary>
/// Funciones exclusivas del admin (sección 12): cuentas (desactivar/reactivar, nunca borrar), solicitudes del rol
/// gestor y configuración de alertas que comparten todos los agentes. Todo queda en la auditoría.
/// </summary>
public sealed partial class ServicioAdmin(
    ContextoMongo mongo,
    PublicadorAtlas atlas,
    Auditoria auditoria,
    ClienteTelegram telegram,
    ServicioCorreo correo,
    OpcionesCuentas opciones,
    TimeProvider reloj)
{
    public const int PersistenciaPorDefecto = 8;
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    // ───────────── Cuentas ─────────────

    public async Task<IReadOnlyList<UsuarioAdminVista>> UsuariosAsync(CancellationToken ct) =>
        [.. (await mongo.Usuarios.Find(FilterDefinition<UsuarioDoc>.Empty).SortBy(u => u.Nombre).ToListAsync(ct).ConfigureAwait(false))
            .Select(u => new UsuarioAdminVista(u.Id.ToString(), u.Nombre, u.Email, u.Rol, u.NivelTecnico, u.Activo,
                u.Telegram.Verificado, u.Mfa.Activo, u.Creado, u.UltimaConexion))];

    /// <summary>Desactivar cierra sus sesiones al instante (nuevo sello). Los registros de la persona se conservan.</summary>
    public async Task<string?> CambiarActivoAsync(ObjectId adminId, string usuarioId, bool activo, CancellationToken ct)
    {
        if (!ObjectId.TryParse(usuarioId, out var id)) return "Usuario no válido.";
        if (id == adminId && !activo) return "No puedes desactivar tu propia cuenta.";
        var r = await mongo.Usuarios.UpdateOneAsync(u => u.Id == id, Builders<UsuarioDoc>.Update
            .Set(u => u.Activo, activo).Set(u => u.SelloSeguridad, Guid.NewGuid().ToString("N")), cancellationToken: ct).ConfigureAwait(false);
        if (r.MatchedCount == 0) return "El usuario no existe.";
        await auditoria.RegistrarAsync(activo ? "admin.cuenta_reactivada" : "admin.cuenta_desactivada", adminId, null, null, "usuario", usuarioId, ct).ConfigureAwait(false);
        return null;
    }

    // ───────────── Rol gestor ─────────────

    public async Task<SolicitudVista?> MiSolicitudAsync(ObjectId usuarioId, CancellationToken ct)
    {
        var s = await mongo.SolicitudesRol.Find(x => x.UsuarioId == usuarioId).SortByDescending(x => x.Creado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return s is null ? null : Vista(s);
    }

    public async Task<string?> SolicitarGestorAsync(UsuarioDoc usuario, string? motivo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (!Roles.SeleccionablesEnRegistro.Contains(usuario.Rol)) return "Solo los técnicos N1, N2 o N3 pueden solicitar el rol gestor.";
        var texto = motivo?.Trim() ?? "";
        if (texto.Length is < 20 or > 1000) return "Explica el motivo (entre 20 y 1000 caracteres).";
        if (await mongo.SolicitudesRol.Find(x => x.UsuarioId == usuario.Id && x.Estado == "pendiente").AnyAsync(ct).ConfigureAwait(false))
            return "Ya tienes una solicitud pendiente.";

        await mongo.SolicitudesRol.InsertOneAsync(new SolicitudRolDoc
        {
            UsuarioId = usuario.Id, UsuarioNombre = usuario.Nombre, NivelActual = usuario.Rol, Motivo = texto, Creado = Ahora,
        }, cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("cuenta.gestor_solicitado", usuario.Id, null, texto, ct: ct).ConfigureAwait(false);

        // Aviso al admin por Telegram (si lo tiene verificado): tiene una solicitud por revisar.
        foreach (var admin in await mongo.Usuarios.Find(u => u.Rol == Roles.Admin && u.Activo).ToListAsync(ct).ConfigureAwait(false))
        {
            await AvisarAsync(admin, $"📝 SysPulse: {usuario.Nombre} ({usuario.Rol}) solicitó el rol gestor.\nRevísala en {opciones.UrlWeb}/admin", null, ct).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<IReadOnlyList<SolicitudVista>> SolicitudesAsync(CancellationToken ct) =>
        [.. (await mongo.SolicitudesRol.Find(FilterDefinition<SolicitudRolDoc>.Empty).SortByDescending(x => x.Creado).Limit(100).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(x => x.Estado == "pendiente" ? 0 : 1).Select(Vista)];

    /// <summary>Aprobar convierte a la persona en gestor (conserva su nivel técnico para una revocación) y cierra sus sesiones.</summary>
    public async Task<string?> ResolverSolicitudAsync(ObjectId adminId, string adminNombre, string solicitudId, bool aprobar, string? comentario, CancellationToken ct)
    {
        if (!ObjectId.TryParse(solicitudId, out var id)) return "Solicitud no válida.";
        if (!aprobar && string.IsNullOrWhiteSpace(comentario)) return "Explica el motivo del rechazo.";
        var s = await mongo.SolicitudesRol.FindOneAndUpdateAsync<SolicitudRolDoc>(
            x => x.Id == id && x.Estado == "pendiente",
            Builders<SolicitudRolDoc>.Update.Set(x => x.Estado, aprobar ? "aprobada" : "rechazada")
                .Set(x => x.ResueltoPor, adminId).Set(x => x.ResueltoPorNombre, adminNombre)
                .Set(x => x.Comentario, comentario?.Trim()).Set(x => x.Resuelto, Ahora),
            cancellationToken: ct).ConfigureAwait(false);
        if (s is null) return "La solicitud no existe o ya fue resuelta.";

        var usuario = await mongo.Usuarios.Find(u => u.Id == s.UsuarioId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (aprobar && usuario is not null)
        {
            await mongo.Usuarios.UpdateOneAsync(u => u.Id == s.UsuarioId, Builders<UsuarioDoc>.Update
                .Set(u => u.Rol, Roles.Gestor).Set(u => u.SelloSeguridad, Guid.NewGuid().ToString("N")), cancellationToken: ct).ConfigureAwait(false);
        }
        await auditoria.RegistrarAsync(aprobar ? "admin.gestor_aprobado" : "admin.gestor_rechazado", adminId, null, comentario, "usuario", s.UsuarioId.ToString(), ct).ConfigureAwait(false);

        if (usuario is not null)
        {
            var mensaje = aprobar
                ? $"✅ SysPulse: tu solicitud de rol gestor fue aprobada por {adminNombre}. Vuelve a iniciar sesión para usar tus nuevos permisos."
                : $"SysPulse: tu solicitud de rol gestor fue rechazada por {adminNombre}. Motivo: {comentario}";
            await AvisarAsync(usuario, mensaje, aprobar ? "Tu solicitud de rol gestor fue aprobada" : "Tu solicitud de rol gestor fue rechazada", ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>Revocar devuelve a la persona a su nivel técnico N1/N2/N3 original.</summary>
    public async Task<string?> RevocarGestorAsync(ObjectId adminId, string usuarioId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(usuarioId, out var id)) return "Usuario no válido.";
        var u = await mongo.Usuarios.Find(x => x.Id == id && x.Rol == Roles.Gestor).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (u is null) return "La persona no tiene el rol gestor.";
        var nivel = Roles.SeleccionablesEnRegistro.Contains(u.NivelTecnico) ? u.NivelTecnico : Roles.N1;
        await mongo.Usuarios.UpdateOneAsync(x => x.Id == id, Builders<UsuarioDoc>.Update
            .Set(x => x.Rol, nivel).Set(x => x.SelloSeguridad, Guid.NewGuid().ToString("N")), cancellationToken: ct).ConfigureAwait(false);
        await mongo.SolicitudesRol.UpdateManyAsync(x => x.UsuarioId == id && x.Estado == "aprobada",
            Builders<SolicitudRolDoc>.Update.Set(x => x.Estado, "revocada"), cancellationToken: ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("admin.gestor_revocado", adminId, null, $"vuelve a {nivel}", "usuario", usuarioId, ct).ConfigureAwait(false);
        await AvisarAsync(u, $"SysPulse: el administrador revocó tu rol gestor. Vuelves a tu nivel {nivel}.", "Cambio de rol en SysPulse", ct).ConfigureAwait(false);
        return null;
    }

    // ───────────── Configuración de alertas (central, para todos los agentes) ─────────────

    public async Task<ConfiguracionAlertasVista> ConfiguracionAlertasAsync(CancellationToken ct)
    {
        var config = await atlas.LeerConfiguracionAsync(ct).ConfigureAwait(false) ?? new ConfiguracionCentral();
        var lista = new List<UmbralVistaAdmin>();
        void Agregar(IEnumerable<Umbral> defectos, string tipo)
        {
            foreach (var d in defectos)
            {
                var c = config.Umbrales.FirstOrDefault(u => u.Metrica == d.Metrica && (u.TipoHost is null || u.TipoHost == tipo));
                lista.Add(c is null
                    ? new UmbralVistaAdmin(d.Metrica, Etiquetas.Nombre(d.Metrica), tipo, d.Advertencia, d.Excepcion, d.Recuperacion, false, d.Advertencia, d.Excepcion, d.Recuperacion)
                    : new UmbralVistaAdmin(d.Metrica, Etiquetas.Nombre(d.Metrica), tipo, c.Advertencia, c.Excepcion, c.Recuperacion, true, d.Advertencia, d.Excepcion, d.Recuperacion));
            }
        }
        Agregar(UmbralesPorDefecto.Windows.Append(UmbralesPorDefecto.Disco("disco.C.pct")), "windows");
        Agregar(UmbralesPorDefecto.Android, "android");
        return new ConfiguracionAlertasVista(config.PersistenciaMinutos ?? PersistenciaPorDefecto, lista);
    }

    public async Task<string?> GuardarConfiguracionAlertasAsync(ObjectId adminId, ConfiguracionAlertasEntrada entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.PersistenciaMinutos is < 1 or > 60) return "La persistencia debe estar entre 1 y 60 minutos.";
        var umbrales = new List<UmbralCentral>();
        foreach (var u in entrada.Umbrales ?? [])
        {
            if (u.Metrica is null || !MetricaValida().IsMatch(u.Metrica)) return $"Métrica no válida: {u.Metrica}.";
            if (u.TipoHost is not (null or "windows" or "android")) return "Tipo de equipo no válido.";
            if (!double.IsFinite(u.Advertencia) || !double.IsFinite(u.Excepcion) || !double.IsFinite(u.Recuperacion) || u.Recuperacion < 0)
                return $"{Etiquetas.Nombre(u.Metrica)}: valores no válidos.";
            if (!(u.Recuperacion < u.Advertencia && u.Advertencia <= u.Excepcion))
                return $"{Etiquetas.Nombre(u.Metrica)}: debe cumplirse recuperación < advertencia ≤ excepción.";
            umbrales.Add(new UmbralCentral(u.Metrica, u.Advertencia, u.Excepcion, u.Recuperacion, TipoHost: u.TipoHost));
        }

        var actual = await atlas.LeerConfiguracionAsync(ct).ConfigureAwait(false) ?? new ConfiguracionCentral();
        await atlas.GuardarConfiguracionAsync(actual with { Umbrales = umbrales, PersistenciaMinutos = entrada.PersistenciaMinutos }, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync("admin.alertas_configuradas", adminId, null,
            $"persistencia {entrada.PersistenciaMinutos} min, {umbrales.Count} umbral(es) personalizados", ct: ct).ConfigureAwait(false);
        return null;
    }

    // ───────────── Apoyo ─────────────

    private async Task AvisarAsync(UsuarioDoc u, string texto, string? asuntoCorreo, CancellationToken ct)
    {
        try
        {
            if (u.Telegram is { Verificado: true, ChatId: { } chat }) await telegram.EnviarAsync(chat, texto, ct: ct).ConfigureAwait(false);
            if (asuntoCorreo is not null && u.EmailVerificado)
            {
                var (asunto, html, plano) = PlantillasCorreo.Aviso(new ContenidoCorreo($"[SysPulse] {asuntoCorreo}", asuntoCorreo, "info", [("Detalle", texto)], $"{opciones.UrlWeb}/perfil", "Abrir mi cuenta"));
                await correo.EnviarAsync(u.Email, asunto, html, plano, ct).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            // El cambio ya se aplicó; el aviso es complementario.
        }
    }

    private static SolicitudVista Vista(SolicitudRolDoc s) => new(s.Id.ToString(), s.UsuarioId.ToString(), s.UsuarioNombre, s.NivelActual, s.Motivo,
        s.Estado, s.ResueltoPorNombre, s.Comentario, s.Creado, s.Resuelto);

    [GeneratedRegex(@"^([a-z]+(\.[a-z0-9_]+)+|disco\.[A-Z]\.pct)$")]
    private static partial Regex MetricaValida();
}
