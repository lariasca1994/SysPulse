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
