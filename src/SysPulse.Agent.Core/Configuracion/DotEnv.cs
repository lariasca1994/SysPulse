namespace SysPulse.Agent.Core.Configuracion;

/// <summary>
/// Carga el .env de la raíz del repositorio en desarrollo local. En producción los secretos vienen
/// del archivo cifrado con DPAPI (Windows) o de SecureStorage (Android). No sobrescribe variables existentes.
/// </summary>
public static class DotEnv
{
    public static string? CargarSiExiste(string directorioInicial)
    {
        for (var actual = new DirectoryInfo(directorioInicial); actual is not null; actual = actual.Parent)
        {
            var archivo = Path.Combine(actual.FullName, ".env");
            if (File.Exists(archivo))
            {
                Cargar(archivo);
                return archivo;
            }
            if (Directory.Exists(Path.Combine(actual.FullName, ".git"))) return null;
        }
        return null;
    }

    private static void Cargar(string archivo)
    {
        foreach (var linea in File.ReadAllLines(archivo, System.Text.Encoding.UTF8))
        {
            var texto = linea.Trim();
            if (texto.Length == 0 || texto.StartsWith('#')) continue;
            var separador = texto.IndexOf('=', StringComparison.Ordinal);
            if (separador <= 0) continue;
            var clave = texto[..separador].Trim();
            if (Environment.GetEnvironmentVariable(clave) is null)
            {
                Environment.SetEnvironmentVariable(clave, texto[(separador + 1)..].Trim().Trim('"'));
            }
        }
    }
}
