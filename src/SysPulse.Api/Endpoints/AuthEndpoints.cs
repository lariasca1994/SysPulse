using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SysPulse.Api.Seguridad;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Endpoints;

public sealed record LoginSolicitud(string? Email, string? Password);
public sealed record PasoSolicitud(string? Token, string? Codigo);
public sealed record CambioPasswordSolicitud(string? Token, string? Nueva);
public sealed record ConfirmarTelegramSolicitud(string? UsuarioId, string? Codigo);

/// <summary>
/// Inicio de sesión por pasos. Cada respuesta indica el siguiente paso y, si aplica, un token temporal:
///   credenciales → [cambiar-password] → configurar-mfa | mfa → sesión (JWT).
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        g.MapPost("/registro", async (SolicitudRegistro s, ServicioCuentas cuentas, HttpContext http, SysPulse.Api.Servicios.ServicioChat chat,
            SysPulse.Infrastructure.Notificaciones.ColaNotificaciones cola, OpcionesCuentas web, CancellationToken ct) =>
        {
            var (resultado, errores) = await cuentas.RegistrarAsync(s, Ip(http), ct);
            if (resultado is not null)
            {
                // Aviso al admin: en #operaciones y, si no está conectado, por correo (sin datos de contacto del usuario).
                await chat.SistemaAsync(SysPulse.Api.Servicios.ServicioChat.Operaciones, $"👋 Se registró una cuenta nueva de nivel {s.Nivel}.", ct);
                cola.Encolar(SysPulse.Infrastructure.Notificaciones.Aviso.SoloCorreo("usuario-nuevo", $"usuario:{resultado.UsuarioId}",
                    new SysPulse.Infrastructure.Notificaciones.ContenidoCorreo("[SysPulse] Usuario nuevo registrado", "Se registró un usuario nuevo", "info",
                        [("Nombre", s.Nombre?.Trim() ?? ""), ("Nivel", s.Nivel ?? "")], $"{web.UrlWeb}/admin", "Ver los usuarios"), admins: true));
            }
            return resultado is null
                ? Results.ValidationProblem(errores.ToDictionary(e => e.Key, e => new[] { e.Value }), title: "Revisa los datos del registro.")
                : Results.Created($"/api/usuarios/{resultado.UsuarioId}", resultado);
        });

        g.MapPost("/verificar-correo", async (VerificarCorreoSolicitud s, ServicioCuentas cuentas, HttpContext http, CancellationToken ct) =>
            await cuentas.VerificarCorreoAsync(s.Token, Ip(http), ct)
                ? Results.Ok(new { mensaje = "Correo confirmado." })
                : Results.Problem("El enlace no es válido o ya venció.", statusCode: 400));

        g.MapPost("/telegram/confirmar", async (ConfirmarTelegramSolicitud s, ServicioCuentas cuentas, HttpContext http, CancellationToken ct) =>
        {
            var error = await cuentas.ConfirmarTelegramAsync(s.UsuarioId ?? "", s.Codigo, Ip(http), ct);
            return error is null ? Results.Ok(new { mensaje = "Telegram verificado." }) : Results.Problem(error, statusCode: 400);
        });

        g.MapPost("/telegram/nuevo-enlace", async (NuevoEnlaceSolicitud s, ServicioCuentas cuentas, CancellationToken ct) =>
        {
            var enlace = await cuentas.NuevoEnlaceTelegramAsync(s.UsuarioId ?? "", ct);
            return enlace is null ? Results.Problem("No se pudo generar el enlace.", statusCode: 400) : Results.Ok(new { enlaceTelegram = enlace });
        });

        g.MapPost("/login", async (LoginSolicitud s, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var r = await cuentas.LoginAsync(s.Email, s.Password, Ip(http), ct);
            return r.Paso switch
            {
                PasoLogin.CredencialesInvalidas => Results.Problem("Correo o contraseña incorrectos.", statusCode: 401),
                PasoLogin.Bloqueado => Results.Problem($"Cuenta bloqueada por intentos fallidos. Intenta de nuevo después de las {r.BloqueadoHasta!.Value.ToLocalTime():HH:mm}.", statusCode: 423),
                PasoLogin.Inactivo => Results.Problem("La cuenta está desactivada. Contacta al administrador.", statusCode: 403),
                PasoLogin.FaltaCorreo => Results.Json(new { paso = "verificar-correo", mensaje = "Confirma tu correo con el enlace que te enviamos." }, statusCode: 403),
                PasoLogin.FaltaTelegram => Results.Json(new { paso = "verificar-telegram", usuarioId = r.Usuario!.Id.ToString(), mensaje = "Falta verificar tu Telegram." }, statusCode: 403),
                PasoLogin.CambiarPassword => Results.Ok(new { paso = "cambiar-password", token = tokens.Paso(r.Usuario!, "cambiar-password") }),
                PasoLogin.ConfigurarMfa => Results.Ok(new { paso = "configurar-mfa", token = tokens.Paso(r.Usuario!, "configurar-mfa") }),
                _ => Results.Ok(new { paso = "mfa", token = tokens.Paso(r.Usuario!, "mfa") }),
            };
        });

        g.MapPost("/cambiar-password", async (CambioPasswordSolicitud s, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await UsuarioDelPasoAsync(s.Token, "cambiar-password", cuentas, tokens, ct);
            if (usuario is null) return Results.Problem("La sesión de este paso venció. Inicia sesión de nuevo.", statusCode: 401);
            var error = await cuentas.CambiarPasswordInicialAsync(usuario, s.Nueva, Ip(http), ct);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nueva"] = [error] });
            var actualizado = (await cuentas.BuscarAsync(usuario.Id.ToString(), ct))!;
            return actualizado.Mfa.Activo
                ? Results.Ok(new { paso = "mfa", token = tokens.Paso(actualizado, "mfa") })
                : Results.Ok(new { paso = "configurar-mfa", token = tokens.Paso(actualizado, "configurar-mfa") });
        });

        g.MapPost("/mfa/iniciar", async (PasoSolicitud s, ServicioCuentas cuentas, Tokens tokens, CancellationToken ct) =>
        {
            var usuario = await UsuarioDelPasoAsync(s.Token, "configurar-mfa", cuentas, tokens, ct);
            if (usuario is null) return Results.Problem("La sesión de este paso venció. Inicia sesión de nuevo.", statusCode: 401);
            var config = await cuentas.IniciarMfaAsync(usuario, ct);
            return Results.Ok(new { secreto = config.Secreto, uri = config.UriQr });
        });

        g.MapPost("/mfa/activar", async (PasoSolicitud s, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await UsuarioDelPasoAsync(s.Token, "configurar-mfa", cuentas, tokens, ct);
            if (usuario is null) return Results.Problem("La sesión de este paso venció. Inicia sesión de nuevo.", statusCode: 401);
            var codigos = await cuentas.ActivarMfaAsync(usuario, s.Codigo, Ip(http), ct);
            if (codigos is null) return Results.Problem("El código no es correcto. Revisa la hora de tu teléfono e inténtalo de nuevo.", statusCode: 400);
            var actualizado = (await cuentas.BuscarAsync(usuario.Id.ToString(), ct))!;
            return Results.Ok(new { codigosRecuperacion = codigos, sesion = Sesion(actualizado, tokens) });
        });

        g.MapPost("/mfa/verificar", async (PasoSolicitud s, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await UsuarioDelPasoAsync(s.Token, "mfa", cuentas, tokens, ct);
            if (usuario is null) return Results.Problem("La sesión de este paso venció. Inicia sesión de nuevo.", statusCode: 401);
            return await cuentas.VerificarMfaAsync(usuario, s.Codigo, Ip(http), ct)
                ? Results.Ok(new { sesion = Sesion(usuario, tokens) })
                : Results.Problem("El código no es correcto.", statusCode: 400);
        });

        app.MapGet("/api/auth/yo", async (ClaimsPrincipal user, ServicioCuentas cuentas, CancellationToken ct) =>
        {
            var u = await cuentas.BuscarAsync(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", ct);
            return u is null ? Results.Unauthorized() : Results.Ok(Perfil(u));
        }).RequireAuthorization();

        // ───── Contraseña ─────
        // Cambio con sesión: devuelve una sesión nueva para este dispositivo (las demás quedan cerradas por el nuevo sello).
        g.MapPost("/password/cambiar", async (CambioPasswordConSesion s, ClaimsPrincipal user, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await cuentas.BuscarAsync(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", ct);
            if (usuario is null) return Results.Unauthorized();
            if (await cuentas.CambiarPasswordAsync(usuario, s.Actual, s.Nueva, Ip(http), ct) is { } error)
                return Results.ValidationProblem(new Dictionary<string, string[]> { [error.StartsWith("La contraseña actual", StringComparison.Ordinal) ? "actual" : "nueva"] = [error] }, title: error);
            var actualizado = await cuentas.BuscarAsync(usuario.Id.ToString(), ct);
            return Results.Ok(new { sesion = Sesion(actualizado!, tokens) });
        }).RequireAuthorization();

        // ───── Cambiar de app autenticadora (Mi cuenta) ─────
        g.MapPost("/mfa/cambiar/iniciar", async (CambioMfaInicio s, ClaimsPrincipal user, ServicioCuentas cuentas, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await cuentas.BuscarAsync(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", ct);
            if (usuario is null) return Results.Unauthorized();
            var (config, error) = await cuentas.IniciarCambioMfaAsync(usuario, s.Password, s.Codigo, Ip(http), ct);
            return error is not null ? Results.Problem(error, statusCode: 400) : Results.Ok(new { secreto = config!.Secreto, uri = config.UriQr });
        }).RequireAuthorization();

        g.MapPost("/mfa/cambiar/confirmar", async (PasoSolicitud s, ClaimsPrincipal user, ServicioCuentas cuentas, Tokens tokens, HttpContext http, CancellationToken ct) =>
        {
            var usuario = await cuentas.BuscarAsync(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", ct);
            if (usuario is null) return Results.Unauthorized();
            var (codigos, error) = await cuentas.ConfirmarCambioMfaAsync(usuario, s.Codigo, Ip(http), ct);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var actualizado = await cuentas.BuscarAsync(usuario.Id.ToString(), ct);
            return Results.Ok(new { codigosRecuperacion = codigos, sesion = Sesion(actualizado!, tokens) });
        }).RequireAuthorization();

        // Respuesta idéntica exista o no el correo (no revela cuentas). En desarrollo devuelve el enlace (correo simulado).
        g.MapPost("/password/olvide", async (OlvidePasswordSolicitud s, ServicioCuentas cuentas, HttpContext http, CancellationToken ct) =>
        {
            var enlace = await cuentas.SolicitarRestablecimientoAsync(s.Email, Ip(http), ct);
            return Results.Ok(new { mensaje = "Si el correo corresponde a una cuenta activa, te enviamos un enlace para restablecer la contraseña.", enlaceDesarrollo = enlace });
        });

        g.MapPost("/password/restablecer", async (RestablecerPasswordSolicitud s, ServicioCuentas cuentas, HttpContext http, CancellationToken ct) =>
            await cuentas.RestablecerPasswordAsync(s.Token, s.Nueva, s.Codigo, Ip(http), ct) is { } error
                ? Results.Problem(error, statusCode: 400)
                : Results.Ok(new { mensaje = "Contraseña actualizada. Ya puedes iniciar sesión." }));
    }

    private static object Sesion(UsuarioDoc u, Tokens tokens) => new
    {
        token = tokens.Sesion(u),
        expira = DateTime.UtcNow + Tokens.DuracionSesion,
        usuario = Perfil(u),
    };

    private static object Perfil(UsuarioDoc u) => new
    {
        id = u.Id.ToString(),
        nombre = u.Nombre,
        email = u.Email,
        rol = u.Rol,
        telegram = u.Telegram.Usuario is not null ? "@" + u.Telegram.Usuario : u.Telegram.TelefonoMascara,
        telegramVerificado = u.Telegram.Verificado,
    };

    private static async Task<UsuarioDoc?> UsuarioDelPasoAsync(string? token, string paso, ServicioCuentas cuentas, Tokens tokens, CancellationToken ct)
    {
        if (tokens.ValidarPaso(token, paso) is not { } datos) return null;
        var usuario = await cuentas.BuscarAsync(datos.UsuarioId, ct);
        return usuario is not null && usuario.Activo && usuario.SelloSeguridad == datos.Sello ? usuario : null;
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}

public sealed record VerificarCorreoSolicitud(string? Token);
public sealed record CambioPasswordConSesion(string? Actual, string? Nueva);
public sealed record OlvidePasswordSolicitud(string? Email);
public sealed record CambioMfaInicio(string? Password, string? Codigo);
public sealed record RestablecerPasswordSolicitud(string? Token, string? Nueva, string? Codigo);
public sealed record NuevoEnlaceSolicitud(string? UsuarioId);
