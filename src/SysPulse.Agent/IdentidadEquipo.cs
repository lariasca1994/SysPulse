using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SysPulse.Agent;

/// <summary>
/// Identidad del host: "win-" + 10 caracteres de un hash del MachineGuid de Windows (estable) y su modelo comercial.
/// Nunca se lee ni se envía el nombre del equipo, el usuario ni el número de serie; la plataforma muestra un nombre
/// genérico por tipo ("Equipo de escritorio 2") junto al modelo.
/// </summary>
internal static class IdentidadEquipo
{
    private static readonly string[] Genericos =
    [
        "to be filled by o.e.m.", "system product name", "system manufacturer", "default string", "not applicable",
        "not specified", "o.e.m.", "oem", "none", "unknown", "x.x",
    ];

    /// <summary>Marcas con su nombre comercial corto.</summary>
    private static readonly (string Contiene, string Marca)[] Marcas =
    [
        ("hewlett", "HP"), ("hp", "HP"), ("lenovo", "Lenovo"), ("dell", "Dell"), ("asus", "ASUS"), ("acer", "Acer"),
        ("micro-star", "MSI"), ("msi", "MSI"), ("gigabyte", "Gigabyte"), ("microsoft", "Microsoft"), ("samsung", "Samsung"),
        ("huawei", "Huawei"), ("toshiba", "Toshiba"), ("dynabook", "Dynabook"), ("apple", "Apple"), ("xiaomi", "Xiaomi"),
        ("asrock", "ASRock"), ("biostar", "Biostar"), ("vmware", "VMware"), ("innotek", "VirtualBox"),
    ];

    /// <summary>
    /// Modelo comercial: fabricante + modelo del BIOS (en Lenovo el nombre comercial está en SystemVersion). Si el
    /// fabricante dejó valores genéricos (equipos armados), se usa la placa base. Null si no hay nada útil.
    /// </summary>
    public static string? Modelo()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios is null) return null;
            string? Leer(string nombre) => Util(bios.GetValue(nombre) as string);

            var fabricante = Leer("SystemManufacturer");
            var producto = Leer("SystemProductName");
            var version = Leer("SystemVersion");
            // Lenovo guarda el tipo de máquina (82H8) en el producto y el nombre comercial (IdeaPad 3 15ITL6) en la versión.
            if (fabricante?.Contains("lenovo", StringComparison.OrdinalIgnoreCase) == true && version is not null && version.Any(char.IsLetter)) producto = version;

            // Equipos armados: el fabricante deja valores genéricos o repite el modelo de la placa base.
            var placaMarca = Leer("BaseBoardManufacturer");
            var placa = Leer("BaseBoardProduct");
            if (producto is null || string.Equals(producto, placa, StringComparison.OrdinalIgnoreCase))
            {
                return placa is null ? null : Recortar($"PC armado · placa {Combinar(placaMarca, placa)}");
            }
            return Recortar(Combinar(fabricante, producto));
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Util(string? valor)
    {
        var v = valor?.Trim();
        return string.IsNullOrEmpty(v) || Genericos.Any(g => v.Equals(g, StringComparison.OrdinalIgnoreCase)) ? null : v;
    }

    /// <summary>"HP" + "HP ProDesk 400 G7" → "HP ProDesk 400 G7"; "LENOVO" + "IdeaPad 3" → "Lenovo IdeaPad 3".</summary>
    private static string Combinar(string? fabricante, string producto)
    {
        var marca = fabricante is null ? null
            : Marcas.FirstOrDefault(m => fabricante.Contains(m.Contiene, StringComparison.OrdinalIgnoreCase)).Marca ?? fabricante;
        if (marca is null || producto.StartsWith(marca, StringComparison.OrdinalIgnoreCase)) return producto;
        return $"{marca} {producto}";
    }

    private static string Recortar(string texto) => texto.Length <= 60 ? texto : texto[..60].TrimEnd();

    public static string Id()
    {
        using var clave = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var guid = clave?.GetValue("MachineGuid") as string ?? Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("SysPulse|" + guid));
        return "win-" + Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }
}
