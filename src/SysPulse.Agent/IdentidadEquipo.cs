using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SysPulse.Agent;

/// <summary>
/// Identidad anónima del host: "win-" + 10 caracteres de un hash del MachineGuid de Windows (estable).
/// Nunca se lee ni se envía el nombre del equipo, el fabricante ni el modelo; la plataforma muestra un nombre
/// genérico por tipo ("Equipo de escritorio", "Equipo portátil").
/// </summary>
internal static class IdentidadEquipo
{
    public static string Id()
    {
        using var clave = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var guid = clave?.GetValue("MachineGuid") as string ?? Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("SysPulse|" + guid));
        return "win-" + Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }
}
