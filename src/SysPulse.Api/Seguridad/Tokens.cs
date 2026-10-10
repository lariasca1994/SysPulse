using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Seguridad;

/// <summary>
/// JWT de sesión (8 h) y tokens temporales de un paso del inicio de sesión (10 min, audiencia distinta:
/// no sirven para llamar a la API). El "sello" invalida los tokens al cambiar contraseña, rol o MFA.
/// </summary>
public sealed class Tokens(string secreto)
{
    public const string Emisor = "syspulse-api";
    public const string AudienciaSesion = "syspulse";
    public const string AudienciaPaso = "syspulse-paso";
    public const string ClaimRol = "rol";
    public const string ClaimSello = "sello";
    public const string ClaimPaso = "paso";
    public static readonly TimeSpan DuracionSesion = TimeSpan.FromHours(8);
    private static readonly TimeSpan DuracionPaso = TimeSpan.FromMinutes(10);

    public SymmetricSecurityKey Clave { get; } = new(Encoding.UTF8.GetBytes(secreto));

    public string Sesion(UsuarioDoc u) => Emitir(AudienciaSesion, DuracionSesion,
    [
        new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
        new(JwtRegisteredClaimNames.Name, u.Nombre),
        new(ClaimRol, u.Rol),
        new(ClaimSello, u.SelloSeguridad),
    ]);

    public string Paso(UsuarioDoc u, string paso) => Emitir(AudienciaPaso, DuracionPaso,
    [
        new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
        new(ClaimPaso, paso),
        new(ClaimSello, u.SelloSeguridad),
    ]);

    /// <summary>Valida un token de paso y devuelve (usuarioId, sello) si es del paso indicado.</summary>
    public (string UsuarioId, string Sello)? ValidarPaso(string? token, string pasoEsperado)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = Emisor,
                ValidAudience = AudienciaPaso,
                IssuerSigningKey = Clave,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);
            if (principal.FindFirstValue(ClaimPaso) != pasoEsperado) return null;
            return (principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!, principal.FindFirstValue(ClaimSello)!);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string Emitir(string audiencia, TimeSpan duracion, IEnumerable<Claim> claims)
    {
        var token = new JwtSecurityToken(Emisor, audiencia, claims, DateTime.UtcNow, DateTime.UtcNow + duracion,
            new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
