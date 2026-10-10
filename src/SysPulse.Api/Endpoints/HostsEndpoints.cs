using SysPulse.Api.Servicios;

namespace SysPulse.Api.Endpoints;

/// <summary>
/// Salud de hosts. Todos los roles ven todos los hosts (sección 12), por eso basta con estar autenticado.
/// Solo lectura: los agentes escriben directo en Atlas.
/// </summary>
public static class HostsEndpoints
{
    public static void MapHosts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hosts").RequireAuthorization();

        g.MapGet("/", (ServicioHosts hosts, CancellationToken ct) => hosts.ListarAsync(ct));

        g.MapGet("/{id}", async (string id, ServicioHosts hosts, CancellationToken ct) =>
            await hosts.ObtenerAsync(id, ct) is { } host ? Results.Ok(host) : Results.Problem("El host no existe.", statusCode: 404));

        // ?horas=24&m=cpu_total,ram_pct
        g.MapGet("/{id}/metricas", async (string id, int? horas, string? m, ServicioHosts hosts, CancellationToken ct) =>
        {
            var claves = (m ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(await hosts.HistorialAsync(id, horas ?? 24, claves, ct));
        });
    }
}
