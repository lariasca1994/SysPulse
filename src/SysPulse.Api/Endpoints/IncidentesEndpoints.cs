using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using SysPulse.Api.Seguridad;
using SysPulse.Api.Servicios;
using SysPulse.Contracts;
using SysPulse.Domain;
using SysPulse.Infrastructure.Seguridad;

namespace SysPulse.Api.Endpoints;

/// <summary>
/// Eventos de los agentes (clave de agente, sin sesión) e incidentes (sesión; las reglas las aplica el dominio).
/// La clave del agente solo permite registrar eventos y avisar cruces de SLA: no lee ni modifica nada más.
/// </summary>
public static partial class IncidentesEndpoints
{
    private static readonly HashSet<string> Severidades = ["informativo", "advertencia", "excepcion"];
    private static readonly HashSet<string> TiposEvento = ["abierto", "escalado", "cerrado"];

    private static readonly Dictionary<string, AccionIncidente> Acciones = new(StringComparer.Ordinal)
    {
        ["tomar"] = AccionIncidente.Tomar,
        ["asignar"] = AccionIncidente.Asignar,
        ["iniciar"] = AccionIncidente.Iniciar,
        ["pausar"] = AccionIncidente.Pausar,
        ["reanudar"] = AccionIncidente.Reanudar,
        ["escalar"] = AccionIncidente.Escalar,
        ["prioridad"] = AccionIncidente.CambiarPrioridad,
        ["resolver"] = AccionIncidente.Resolver,
        ["cerrar"] = AccionIncidente.Cerrar,
        ["reabrir"] = AccionIncidente.Reabrir,
        ["comentar"] = AccionIncidente.Comentar,
    };

    public static void MapIncidentes(this IEndpointRouteBuilder app, string? claveAgente)
    {
        // ───── Agentes ─────
        var agentes = app.MapGroup("/api").RequireRateLimiting("agente");

        agentes.MapPost("/eventos", async (HttpRequest req, EventoDto e, ServicioIncidentes servicio, CancellationToken ct) =>
        {
            if (!ClaveValida(req, claveAgente)) return Results.Unauthorized();
            if (Validar(e) is { } error) return Results.Problem(error, statusCode: 400);
            var inc = await servicio.RecibirEventoAsync(e, ct);
            return Results.Accepted(value: new { incidente = inc?.Codigo });
        });

        agentes.MapPost("/sla/umbral", async (HttpRequest req, ServicioIncidentes servicio, CancellationToken ct) =>
        {
            if (!ClaveValida(req, claveAgente)) return Results.Unauthorized();
            // La API vuelve a calcular: el agente solo la despierta, nunca decide el aviso.
            return Results.Ok(new { avisos = await servicio.RevisarSlaAsync(ct) });
        });

        // ───── Personas ─────
        var g = app.MapGroup("/api/incidentes").RequireAuthorization();

        g.MapGet("/", (bool? todos, ClaimsPrincipal u, ServicioIncidentes s, CancellationToken ct) =>
            s.ListarAsync(Actor(u), soloAbiertos: todos != true, ct));

        g.MapGet("/{id}", async (string id, ClaimsPrincipal u, ServicioIncidentes s, CancellationToken ct) =>
            await s.ObtenerAsync(id, Actor(u), ct) is { } inc ? Results.Ok(inc) : Results.Problem("El incidente no existe.", statusCode: 404));

        g.MapPost("/", (NuevoIncidente n, ClaimsPrincipal u, ServicioIncidentes s, CancellationToken ct) =>
            Ejecutar(async () => Results.Created($"/api/incidentes", await s.CrearManualAsync(n, Actor(u), Nombre(u), ct))));

        g.MapPost("/{id}/{accion}", (string id, string accion, DatosAccion? datos, ClaimsPrincipal u, ServicioIncidentes s, CancellationToken ct) =>
            Acciones.TryGetValue(accion, out var a)
                ? Ejecutar(async () => Results.Ok(await s.AplicarAsync(id, a, datos ?? new DatosAccion(null, null, null, null), Actor(u), Nombre(u), ct)))
                : Task.FromResult(Results.Problem("Acción desconocida.", statusCode: 404)));

        app.MapGet("/api/usuarios/tecnicos", (ServicioIncidentes s, CancellationToken ct) => s.TecnicosAsync(ct)).RequireAuthorization();
    }

    private static async Task<IResult> Ejecutar(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (NoEncontradoException ex)
        {
            return Results.Problem(ex.Message, statusCode: 404);
        }
        catch (ValidacionException ex)
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
        catch (ReglaException ex)
        {
            return Results.Problem(ex.Message, statusCode: 409);
        }
    }

    private static Actor Actor(ClaimsPrincipal u) => new(u.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", u.FindFirstValue(Tokens.ClaimRol) ?? "");

    private static string Nombre(ClaimsPrincipal u) => u.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Usuario";

    private static bool ClaveValida(HttpRequest req, string? clave) =>
        !string.IsNullOrEmpty(clave) && Cifrado.IgualesTiempoConstante(req.Headers["X-Agent-Key"].ToString(), clave);

    private static string? Validar(EventoDto e)
    {
        if (e is null) return "Falta el evento.";
        if (!HostId().IsMatch(e.Host ?? "")) return "Host inválido.";
        if (!TiposEvento.Contains(e.Tipo?.ToLowerInvariant() ?? "")) return "Tipo de evento inválido.";
        if (!Severidades.Contains(e.Severidad?.ToLowerInvariant() ?? "")) return "Severidad inválida.";
        if (string.IsNullOrWhiteSpace(e.Metrica) || e.Metrica.Length > 100) return "Métrica inválida.";
        if (string.IsNullOrWhiteSpace(e.ClaveDedup) || e.ClaveDedup.Length > 200) return "Clave de deduplicación inválida.";
        if (!double.IsFinite(e.Valor) || !double.IsFinite(e.Umbral)) return "Valor inválido.";
        if (e.Ts > DateTimeOffset.UtcNow.AddMinutes(10) || e.Ts < DateTimeOffset.UtcNow.AddDays(-7)) return "Fecha del evento fuera de rango.";
        return null;
    }

    /// <summary>Ids anónimos de los agentes: win-xxxxxxxxxx / and-xxxxxxxxxx.</summary>
    [GeneratedRegex("^(win|and)-[0-9a-f]{10}$")]
    private static partial Regex HostId();
}
