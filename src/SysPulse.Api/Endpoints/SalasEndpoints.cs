using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MongoDB.Bson;
using SysPulse.Api.Servicios;

namespace SysPulse.Api.Endpoints;

/// <summary>Lectura de salas y mensajes (el envío en tiempo real va por el hub /hubs/chat). Todos los roles ven todo.</summary>
public static class SalasEndpoints
{
    public static void MapSalas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/salas").RequireAuthorization();

        g.MapGet("/", (ClaimsPrincipal u, ServicioChat chat, CancellationToken ct) =>
            chat.ListarAsync(ObjectId.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub)!), ct));

        g.MapGet("/{id}/mensajes", (string id, string? antes, ServicioChat chat, CancellationToken ct) =>
            chat.MensajesAsync(id, antes, ct));
    }
}
