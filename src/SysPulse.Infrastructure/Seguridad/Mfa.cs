using System.Security.Cryptography;
using OtpNet;

namespace SysPulse.Infrastructure.Seguridad;

/// <summary>MFA con TOTP (RFC 6238, 30 s, 6 dígitos): Google/Microsoft Authenticator, Authy, etc.</summary>
public static class Mfa
{
    public const string Emisor = "SysPulse";
    public const int CantidadCodigosRecuperacion = 10;

    public static string NuevoSecreto() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    /// <summary>URI otpauth:// para el código QR que se escanea con la app autenticadora.</summary>
    public static string UriConfiguracion(string secretoBase32, string email) =>
        $"otpauth://totp/{Uri.EscapeDataString(Emisor)}:{Uri.EscapeDataString(email)}?secret={secretoBase32}&issuer={Uri.EscapeDataString(Emisor)}&algorithm=SHA1&digits=6&period=30";

    /// <summary>
    /// Verifica un código aceptando ±1 paso (reloj desfasado). Devuelve el paso usado, o null si es inválido
    /// o ya se usó (<paramref name="ultimoPasoUsado"/> impide reutilizar un mismo código).
    /// </summary>
    public static long? Verificar(string secretoBase32, string? codigo, long ultimoPasoUsado, DateTime? ahoraUtc = null)
    {
        if (codigo is null || codigo.Length != 6 || !codigo.All(char.IsDigit)) return null;
        var totp = new Totp(Base32Encoding.ToBytes(secretoBase32));
        var ok = totp.VerifyTotp(ahoraUtc ?? DateTime.UtcNow, codigo, out var paso, new VerificationWindow(previous: 1, future: 1));
        return ok && paso > ultimoPasoUsado ? paso : null;
    }

    public static string CodigoActual(string secretoBase32, DateTime? ahoraUtc = null) =>
        new Totp(Base32Encoding.ToBytes(secretoBase32)).ComputeTotp(ahoraUtc ?? DateTime.UtcNow);

    /// <summary>10 códigos de recuperación tipo "ABCD-EFGH" (un solo uso; se guardan como hash).</summary>
    public static IReadOnlyList<string> CodigosRecuperacion()
    {
        const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return Enumerable.Range(0, CantidadCodigosRecuperacion).Select(_ =>
        {
            var c = RandomNumberGenerator.GetString(Alfabeto, 8);
            return $"{c[..4]}-{c[4..]}";
        }).ToList();
    }

    public static string NormalizarCodigoRecuperacion(string codigo) => codigo.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
}
