using System.Security.Cryptography;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Core.Actualizaciones;

/// <summary>
/// Reglas de seguridad de la actualización automática (Windows y Android):
/// - solo HTTPS y solo desde GitHub Releases (también se valida el destino final tras las redirecciones);
/// - la huella SHA-256 esperada viene de la configuración central en Atlas, no del mismo sitio de descarga;
/// - tamaño máximo para no llenar el disco con una descarga anómala.
/// Si algo no cuadra, el archivo se borra y la versión instalada sigue funcionando.
/// </summary>
public static class VerificadorPaquete
{
    public const long TamanoMaximo = 250L * 1024 * 1024;

    private static readonly HashSet<string> HostsPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    public static bool UrlPermitida(Uri? url) =>
        url is not null && url.Scheme == Uri.UriSchemeHttps && HostsPermitidos.Contains(url.Host);

    /// <summary>¿La versión publicada es más nueva que la instalada? Ante un formato desconocido, no actualiza.</summary>
    public static bool EsMasNueva(string? disponible, string? instalada)
    {
        if (!Version.TryParse(Limpiar(disponible), out var nueva)) return false;
        return !Version.TryParse(Limpiar(instalada), out var actual) || nueva > actual;
    }

    /// <summary>"1.1.0+abc" → "1.1.0" (la versión informativa puede traer el commit).</summary>
    private static string? Limpiar(string? v) => v?.Split('+', '-')[0].Trim();

    public static bool HuellaValida(string ruta, string sha256Esperado)
    {
        using var archivo = File.OpenRead(ruta);
        var calculada = Convert.ToHexString(SHA256.HashData(archivo));
        return string.Equals(calculada, sha256Esperado.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string Huella(string ruta)
    {
        using var archivo = File.OpenRead(ruta);
        return Convert.ToHexString(SHA256.HashData(archivo)).ToLowerInvariant();
    }

    /// <summary>Descarga el paquete a <paramref name="destino"/> y verifica origen, tamaño y huella. Lanza si algo falla (y borra el archivo).</summary>
    public static async Task DescargarAsync(PaqueteActualizacion paquete, string destino, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        if (!Uri.TryCreate(paquete.Url, UriKind.Absolute, out var url) || !UrlPermitida(url))
            throw new InvalidOperationException("La URL de la actualización no es de GitHub Releases por HTTPS.");

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        try
        {
            using var respuesta = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            respuesta.EnsureSuccessStatusCode();
            if (!UrlPermitida(respuesta.RequestMessage?.RequestUri))
                throw new InvalidOperationException("La descarga terminó en un sitio que no es de GitHub.");
            if (respuesta.Content.Headers.ContentLength > TamanoMaximo)
                throw new InvalidOperationException("El paquete supera el tamaño máximo permitido.");

            await using (var entrada = await respuesta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var salida = File.Create(destino))
            {
                var buffer = new byte[81920];
                long total = 0;
                int leidos;
                while ((leidos = await entrada.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += leidos;
                    if (total > TamanoMaximo) throw new InvalidOperationException("El paquete supera el tamaño máximo permitido.");
                    await salida.WriteAsync(buffer.AsMemory(0, leidos), ct).ConfigureAwait(false);
                }
            }

            if (!HuellaValida(destino, paquete.Sha256))
                throw new InvalidOperationException("La huella SHA-256 no coincide: el archivo descargado no es el publicado.");
        }
        catch
        {
            if (File.Exists(destino)) File.Delete(destino);
            throw;
        }
    }
}
