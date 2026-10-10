using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SysPulse.Api;
using SysPulse.Api.Endpoints;
using SysPulse.Api.Seguridad;
using SysPulse.Api.Servicios;
using SysPulse.Domain;
using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Persistencia;
using SysPulse.Infrastructure.Seguridad;
using SysPulse.Infrastructure.Notificaciones;
using SysPulse.Infrastructure.Telegram;

var desarrollo = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
if (desarrollo) DotEnv.CargarSiExiste(Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Seguridad: el registro de HttpClient incluye la URL de cada petición, y la de Telegram lleva el token del bot.
// Se fija en código (no solo en appsettings) para que nunca dependa de cómo se arranque la API.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
string Requerida(string clave) => config[clave] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Falta la variable de entorno {clave}.");

// ───── Servicios ─────
var tokens = new Tokens(Requerida("JWT_SECRET"));
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(new ContextoMongo(Requerida("MONGO_URI")));
builder.Services.AddSingleton(new Cifrado(Requerida("MFA_ENCRYPTION_KEY")));
builder.Services.AddSingleton<Auditoria>();

builder.Services.AddHttpClient<ClienteTelegram>().AddTypedClient((http, _) => new ClienteTelegram(http, config["TELEGRAM_BOT_TOKEN"]));
builder.Services.AddHttpClient<ServicioCorreo>().AddTypedClient((http, sp) => new ServicioCorreo(http,
    new OpcionesCorreo(config["BREVO_API_KEY"], config["BREVO_SENDER_EMAIL"], config["BREVO_SENDER_NAME"] ?? "SysPulse",
        EnviarDeVerdad: string.Equals(config["CORREO_ENVIAR"], "true", StringComparison.OrdinalIgnoreCase)),
    sp.GetRequiredService<ILogger<ServicioCorreo>>()));

var usuarioBot = config["TELEGRAM_BOT_USUARIO"] ?? "sysplusbot";
builder.Services.AddSingleton(new OpcionesCuentas(config["WEB_URL"] ?? "http://localhost:5173", usuarioBot));
builder.Services.AddScoped<ServicioCuentas>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServicioHosts>();
builder.Services.AddSingleton<ServicioChat>();
builder.Services.AddScoped<ServicioIncidentes>();
builder.Services.AddScoped<ServicioAnalitica>();
builder.Services.AddScoped<ServicioAdmin>();
builder.Services.AddScoped<ServicioAltas>();
// Lo que recibe un equipo al darse de alta. MONGO_URI_AGENTES* permite usar un usuario de base exclusivo (y limitado) para los agentes.
builder.Services.AddSingleton(new OpcionesAlta(
    config["MONGO_URI_AGENTES"] ?? config["MONGO_URI"],
    config["MONGO_URI_AGENTES_ANDROID"] ?? config["MONGO_URI_ANDROID"],
    (config["API_PUBLICA"] ?? "https://syspulse-api-7dj0.onrender.com").TrimEnd('/'),
    config["AGENT_API_KEY"],
    config["TELEGRAM_BOT_TOKEN"],
    [.. (config["TELEGRAM_FIXED_CHAT_IDS"] ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)]));
builder.Services.AddSingleton(new SysPulse.Agent.Core.Envio.PublicadorAtlas(Requerida("MONGO_URI")));

// Notificaciones (sección 7): cola en memoria + envío en segundo plano. Las cuentas fijas reciben todo.
var chatsFijos = (config["TELEGRAM_FIXED_CHAT_IDS"] ?? "")
    .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
    .Select(c => long.TryParse(c, out var id) ? id : (long?)null).OfType<long>().ToList();
builder.Services.AddSingleton(new OpcionesNotificaciones(chatsFijos));
builder.Services.AddSingleton<ColaNotificaciones>();
builder.Services.AddScoped<DespachadorNotificaciones>();
builder.Services.AddHostedService<ProcesadorNotificaciones>();
builder.Services.AddHostedService<InicioApi>();
if (desarrollo) builder.Services.AddHostedService<SondeoTelegramDesarrollo>();

// ───── Autenticación y autorización (sección 12) ─────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = Tokens.Emisor,
        ValidAudience = Tokens.AudienciaSesion,
        IssuerSigningKey = tokens.Clave,
        RoleClaimType = Tokens.ClaimRol,
        NameClaimType = JwtRegisteredClaimNames.Name,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
    o.Events = new JwtBearerEvents
    {
        // SignalR envía el token por query string (access_token).
        OnMessageReceived = c =>
        {
            if (c.Request.Path.StartsWithSegments("/hubs", StringComparison.Ordinal)) c.Token = c.Request.Query["access_token"];
            return Task.CompletedTask;
        },
        // Un token emitido antes de cambiar contraseña, rol o MFA (o de una cuenta desactivada) deja de servir.
        OnTokenValidated = async c =>
        {
            var cuentas = c.HttpContext.RequestServices.GetRequiredService<ServicioCuentas>();
            var usuario = await cuentas.BuscarAsync(c.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "", c.HttpContext.RequestAborted);
            if (usuario is null || !usuario.Activo || usuario.SelloSeguridad != c.Principal?.FindFirstValue(Tokens.ClaimSello)) c.Fail("Sesión revocada.");
        },
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("N1+", p => p.RequireRole(Roles.N1, Roles.N2, Roles.N3, Roles.Admin))
    .AddPolicy("N2+", p => p.RequireRole(Roles.N2, Roles.N3, Roles.Admin))
    .AddPolicy("N3+", p => p.RequireRole(Roles.N3, Roles.Admin))
    .AddPolicy("Gestor", p => p.RequireRole(Roles.Gestor, Roles.Admin))
    .AddPolicy("Admin", p => p.RequireRole(Roles.Admin));

// Límite por IP en /api/auth: frena fuerza bruta y abuso del registro (20/min; configurable para pruebas).
var limiteAuth = int.TryParse(config["AUTH_LIMITE_MINUTO"], out var l) && l > 0 ? l : 20;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteAuth, Window = TimeSpan.FromMinutes(1) }));
    // Agentes: pocos eventos por minuto en operación normal; el límite frena un agente desbocado o una clave filtrada.
    o.AddPolicy("agente", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

var corsOrigin = config["CORS_ORIGIN"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (!string.IsNullOrWhiteSpace(corsOrigin)) p.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddProblemDetails();
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 16 * 1024);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health check para Render y para GitHub Actions.
app.MapGet("/health", () => Results.Ok(new { estado = "ok" }));
app.MapAuth();
app.MapHosts();
app.MapIncidentes(config["AGENT_API_KEY"]);
app.MapSalas();
app.MapAnalitica();
app.MapAdmin();
app.MapEquipos();
app.MapHub<SysPulse.Api.Hubs.ChatHub>("/hubs/chat");
app.MapTelegram(config["TELEGRAM_WEBHOOK_SECRET"]);

app.Run();
