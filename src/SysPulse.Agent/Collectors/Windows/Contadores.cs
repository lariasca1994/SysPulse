using System.Diagnostics;

namespace SysPulse.Agent.Collectors.Windows;

/// <summary>
/// Crea contadores de rendimiento de forma segura: si la categoría no existe en este equipo
/// (idioma, edición de Windows, contadores dañados) devuelve null en lugar de fallar.
/// La primera lectura de un contador de tasa siempre es 0, así que se "calienta" al crearlo.
/// </summary>
internal static class Contadores
{
    public static PerformanceCounter? Crear(string categoria, string contador, string instancia = "")
    {
        try
        {
            if (!PerformanceCounterCategory.Exists(categoria)) return null;
            var pc = new PerformanceCounter(categoria, contador, instancia, readOnly: true);
            _ = pc.NextValue();
            return pc;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
