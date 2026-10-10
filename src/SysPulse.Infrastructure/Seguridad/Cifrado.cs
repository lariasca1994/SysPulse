using System.Security.Cryptography;
using System.Text;

namespace SysPulse.Infrastructure.Seguridad;

/// <summary>
/// Cifrado simétrico AES-256-GCM para datos que hay que poder leer después (secreto MFA, teléfono).
/// La clave se deriva de MFA_ENCRYPTION_KEY con HKDF. Formato: base64(nonce | tag | texto cifrado).
/// </summary>
public sealed class Cifrado
{
    private const int TamNonce = 12;
    private const int TamTag = 16;
    private readonly byte[] _clave;

    public Cifrado(string claveMaestra)
    {
        if (string.IsNullOrWhiteSpace(claveMaestra) || claveMaestra.Length < 32)
            throw new ArgumentException("MFA_ENCRYPTION_KEY debe tener al menos 32 caracteres.", nameof(claveMaestra));
        _clave = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(claveMaestra), 32, info: Encoding.UTF8.GetBytes("SysPulse.Cifrado.v1"));
    }

    public string Cifrar(string texto)
    {
        var plano = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[TamNonce + TamTag + plano.Length];
        var nonce = salida.AsSpan(0, TamNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_clave, TamTag);
        aes.Encrypt(nonce, plano, salida.AsSpan(TamNonce + TamTag), salida.AsSpan(TamNonce, TamTag));
        return Convert.ToBase64String(salida);
    }

    public string Descifrar(string base64)
    {
        var datos = Convert.FromBase64String(base64);
        var plano = new byte[datos.Length - TamNonce - TamTag];
        using var aes = new AesGcm(_clave, TamTag);
        aes.Decrypt(datos.AsSpan(0, TamNonce), datos.AsSpan(TamNonce + TamTag), datos.AsSpan(TamNonce, TamTag), plano);
        return Encoding.UTF8.GetString(plano);
    }

    /// <summary>Hash SHA-256 para tokens y códigos de un solo uso (se comparan, nunca se recuperan).</summary>
    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    /// <summary>Token aleatorio apto para URL.</summary>
    public static string TokenAleatorio(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Código numérico de 6 dígitos para Telegram.</summary>
    public static string Codigo6() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IgualesTiempoConstante(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
