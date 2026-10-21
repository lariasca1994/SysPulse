using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MongoDB.Bson;
using SysPulse.Api.Servicios;
using SysPulse.Infrastructure.Cuentas;

namespace SysPulse.Api.Endpoints;

public sealed record CambioActivo(bool Activo);
public sealed record ResolucionSolicitud(bool Aprobar, string? Comentario);
public sealed record NuevaSolicitudGestor(string? Motivo);

/// <summary>Administración (solo admin) y solicitud del rol gestor (cualquier técnico con sesión).</summary>
public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        g.MapGet("/usuarios", (ServicioAdmin s, CancellationToken ct) => s.UsuariosAsync(ct));

        g.MapPost("/usuarios/{id}/activo", async (string id, CambioActivo c, ClaimsPrincipal u, ServicioAdmin s, CancellationToken ct) =>
            Resultado(await s.CambiarActivoAsync(Id(u), id, c.Activo, ct)));

        g.MapPost("/usuarios/{id}/revocar-gestor", async (string id, ClaimsPrincipal u, ServicioAdmin s, CancellationToken ct) =>
            Resultado(await s.RevocarGestorAsync(Id(u), id, ct)));

        g.MapGet("/solicitudes", (ServicioAdmin s, CancellationToken ct) => s.SolicitudesAsync(ct));

        g.MapPost("/solicitudes/{id}/resolver", async (string id, ResolucionSolicitud r, ClaimsPrincipal u, ServicioAdmin s, CancellationToken ct) =>
            Resultado(await s.ResolverSolicitudAsync(Id(u), Nombre(u), id, r.Aprobar, r.Comentario, ct)));

        g.MapGet("/alertas", (ServicioAdmin s, CancellationToken ct) => s.ConfiguracionAlertasAsync(ct));

        g.MapPut("/alertas", async (ConfiguracionAlertasEntrada e, ClaimsPrincipal u, ServicioAdmin s, CancellationToken ct) =>
            Resultado(await s.GuardarConfiguracionAlertasAsync(Id(u), e, ct)));

        var cuenta = app.MapGroup("/api/cuenta").RequireAuthorization();

        cuenta.MapGet("/solicitud-gestor", async (ClaimsPrincipal u, ServicioAdmin s, CancellationToken ct) =>
            await s.MiSolicitudAsync(Id(u), ct) is { } sol ? Results.Ok(sol) : Results.NoContent());

        cuenta.MapPost("/solicitud-gestor", async (NuevaSolicitudGestor n, ClaimsPrincipal u, ServicioCuentas cuentas, ServicioAdmin s, CancellationToken ct) =>
        {
            var usuario = await cuentas.BuscarAsync(Id(u).ToString(), ct);
            return usuario is null ? Results.Unauthorized() : Resultado(await s.SolicitarGestorAsync(usuario, n.Motivo, ct));
        });
    }

    private static IResult Resultado(string? error) => error is null ? Results.Ok(new { ok = true }) : Results.Problem(error, statusCode: 400);

    private static ObjectId Id(ClaimsPrincipal u) => ObjectId.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private static string Nombre(ClaimsPrincipal u) => u.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Administrador";
}

public sealed record NuevoNombreEquipo(string? Nombre);
public sealed record RetiroEquipo(bool Retirar);

/// <summary>Equipos: alta con código (N2, N3 y admin), canje del código (instalador/app, sin sesión) y gestión de nombres y retiros (admin).</summary>
public static class EquiposEndpoints
{
    public static void MapEquipos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        g.MapGet("/equipos", (ServicioHosts h, CancellationToken ct) => h.EquiposAsync(ct));

        g.MapPost("/equipos/{id}/nombre", async (string id, NuevoNombreEquipo n, ClaimsPrincipal u, ServicioHosts h, SysPulse.Infrastructure.Persistencia.Auditoria a, CancellationToken ct) =>
        {
            var error = await h.RenombrarAsync(id, n.Nombre, ct);
            if (error is null) await a.RegistrarAsync("admin.equipo_renombrado", ObjectId.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub)!), null, n.Nombre, "host", id, ct);
            return error is null ? Results.Ok(new { ok = true }) : Results.Problem(error, statusCode: 400);
        });

        g.MapPost("/equipos/{id}/retiro", async (string id, RetiroEquipo r, ClaimsPrincipal u, ServicioHosts h, SysPulse.Infrastructure.Persistencia.Auditoria a,
            SysPulse.Infrastructure.Notificaciones.ColaNotificaciones cola, SysPulse.Infrastructure.Cuentas.OpcionesCuentas web, ServicioChat chat, CancellationToken ct) =>
        {
            var error = await h.RetirarAsync(id, r.Retirar, ct);
            if (error is null)
            {
                await a.RegistrarAsync(r.Retirar ? "admin.equipo_retirado" : "admin.equipo_restaurado", ObjectId.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub)!), null, null, "host", id, ct);
                var nombre = (await h.EquiposAsync(ct)).FirstOrDefault(x => x.Id == id)?.Nombre ?? "Equipo";
                var accion = r.Retirar ? "retirado" : "restaurado";
                await chat.SistemaAsync(ServicioChat.Operaciones, r.Retirar ? $"🗄️ {nombre} fue retirado de la operación (su historial se conserva)." : $"♻️ {nombre} volvió a la operación.", ct);
                // Correo solo al desactivar (retirar), y solo a los admins que no están conectados.
                if (r.Retirar) cola.Encolar(SysPulse.Infrastructure.Notificaciones.Aviso.SoloCorreo($"equipo-{accion}", $"equipo:{id}:{accion}:{DateTime.UtcNow:yyyyMMddHHmm}",
                    new SysPulse.Infrastructure.Notificaciones.ContenidoCorreo($"[SysPulse] Equipo {accion}: {nombre}", $"Equipo {accion}", r.Retirar ? "advertencia" : "ok",
                        [("Equipo", nombre), ("Por", u.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Admin")], $"{web.UrlWeb}/admin", "Ver los equipos"), admins: true));
            }
            return error is null ? Results.Ok(new { ok = true }) : Results.Problem(error, statusCode: 400);
        });

        g.MapGet("/altas", (ServicioAltas s, CancellationToken ct) => s.CodigosAsync(ct));

        // Agregar equipos al monitoreo: técnicos N2 y N3 y el admin (política "N2+").
        app.MapPost("/api/equipos/altas", async (ClaimsPrincipal u, ServicioAltas s, CancellationToken ct) =>
        {
            var (codigo, expira) = await s.CrearAsync(ObjectId.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub)!), u.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Técnico", ct);
            return Results.Ok(new { codigo, expira });
        }).RequireAuthorization("N2+");

        // Canje del código (sin sesión): limitado por IP como el inicio de sesión, para impedir probar códigos al azar.
        // Sin credencial exclusiva de agentes no se entrega nada (ni se gasta el código).
        app.MapPost("/api/agentes/alta", async (SysPulse.Contracts.SolicitudAlta sol, HttpContext http, ServicioAltas s, OpcionesAlta opciones, CancellationToken ct) =>
        {
            if (!opciones.Disponible(sol.TipoHost?.ToLowerInvariant() ?? ""))
                return Results.Problem("El alta de equipos no está disponible en este momento. Avisa al administrador.", statusCode: 503);
            return await s.CanjearAsync(sol, http.Connection.RemoteIpAddress?.ToString(), ct) is { } r
                ? Results.Ok(r)
                : Results.Problem("El código no es válido, ya se usó o venció.", statusCode: 400);
        }).RequireRateLimiting("auth");
    }
}
