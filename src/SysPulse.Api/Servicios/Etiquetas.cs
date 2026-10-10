using System.Globalization;
using System.Text.RegularExpressions;

namespace SysPulse.Api.Servicios;

/// <summary>
/// Nombres legibles de las métricas para Telegram y correo (la web tiene su propio catálogo en metricas.ts).
/// Acepta la clave con puntos (cpu.total) o con guiones bajos (cpu_total).
/// </summary>
public static partial class Etiquetas
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    private static readonly Dictionary<string, (string Nombre, string Unidad)> Fijas = new(StringComparer.Ordinal)
    {
        ["cpu.total"] = ("Uso de CPU", "%"),
        ["ram.pct"] = ("Uso de RAM", "%"),
        ["ram.baja"] = ("Memoria baja", "si/no"),
        ["paginacion.pct"] = ("Uso del archivo de paginación", "%"),
        ["almacenamiento.pct"] = ("Almacenamiento usado", "%"),
        ["eventlog.errores_5m"] = ("Errores en el visor de eventos (5 min)", ""),
        ["red.ping.perdida_pct"] = ("Pérdida de paquetes", "%"),
        ["bateria.pct"] = ("Batería", "%"),
        ["bateria.temp_c"] = ("Temperatura de batería", "°C"),
        ["bateria.baja_sin_cargar"] = ("Batería baja sin cargar", "nivel"),
        ["termico.estado"] = ("Estado térmico", "termico"),
    };

    private static readonly string[] Termico = ["Normal", "Leve", "Moderado", "Grave", "Crítico", "Emergencia", "Apagado"];

    public static string Nombre(string metrica) => Describir(metrica).Nombre;

    /// <summary>Valor con su unidad: "92%", "41,5 °C", "Grave".</summary>
    public static string Valor(string metrica, double valor)
    {
        var unidad = Describir(metrica).Unidad;
        return unidad switch
        {
            "%" => $"{valor.ToString("0.#", Es)}%",
            "°C" => $"{valor.ToString("0.#", Es)} °C",
            "si/no" => valor >= 0.5 ? "Sí" : "No",
            "nivel" => valor >= 2 ? "Crítica" : valor >= 1 ? "Sí" : "No",
            "termico" => (int)valor is var i && i >= 0 && i < Termico.Length ? Termico[i] : $"Nivel {valor:0}",
            _ => valor.ToString("0.##", Es),
        };
    }

    private static (string Nombre, string Unidad) Describir(string metrica)
    {
        ArgumentNullException.ThrowIfNull(metrica);
        var m = metrica.Replace('_', '.');
        // Las claves con '_' propias (eventlog.errores_5m, bateria.temp_c…) se buscan también tal cual.
        if (Fijas.TryGetValue(metrica, out var fija) || Fijas.TryGetValue(m, out fija)) return fija;
        foreach (var (clave, valor) in Fijas)
        {
            if (clave.Replace('_', '.') == m) return valor;
        }
        var disco = Disco().Match(m);
        if (disco.Success) return ($"Disco {disco.Groups[1].Value.ToUpperInvariant()}: usado", "%");
        return (metrica, "");
    }

    [GeneratedRegex(@"^disco\.([A-Za-z])\.pct$")]
    private static partial Regex Disco();
}
