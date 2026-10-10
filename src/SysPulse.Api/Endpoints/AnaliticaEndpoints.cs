using SysPulse.Api.Servicios;

namespace SysPulse.Api.Endpoints;

/// <summary>Indicadores ITIL del período (7, 30 o 90 días). Todos los roles ven la analítica (sección 12).</summary>
public static class AnaliticaEndpoints
{
    public static void MapAnalitica(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/analitica", (int? dias, ServicioAnalitica s, CancellationToken ct) => s.CalcularAsync(dias ?? 30, ct))
            .RequireAuthorization();
}
