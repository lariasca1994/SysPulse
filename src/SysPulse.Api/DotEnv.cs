namespace SysPulse.Api;

/// <summary>
/// Carga el archivo .env de la raíz del repositorio solo en desarrollo local.
/// En Render las variables llegan como variables de entorno reales y este archivo no existe.
/// No sobrescribe variables que ya estén definidas.
/// </summary>
internal static class DotEnv
{
    public static void CargarSiExiste(string directorioInicial)
    {
        var archivo = Buscar(directorioInicial);
        if (archivo is null) return;

        foreach (var linea in File.ReadAllLines(archivo, System.Text.Encoding.UTF8))
        {
            var texto = linea.Trim();
            if (texto.Length == 0 || texto.StartsWith('#')) continue;

            var separador = texto.IndexOf('=', StringComparison.Ordinal);
            if (separador <= 0) continue;

            var clave = texto[..separador].Trim();
            var valor = texto[(separador + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(clave) is null)
            {
                Environment.SetEnvironmentVariable(clave, valor);
            }
        }
    }

    private static string? Buscar(string directorio)
    {
        for (var actual = new DirectoryInfo(directorio); actual is not null; actual = actual.Parent)
        {
            var candidato = Path.Combine(actual.FullName, ".env");
            if (File.Exists(candidato)) return candidato;
            if (Directory.Exists(Path.Combine(actual.FullName, ".git"))) return null;
        }
        return null;
    }
}
