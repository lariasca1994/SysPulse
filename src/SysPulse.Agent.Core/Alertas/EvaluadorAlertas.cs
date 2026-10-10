using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Alertas;

/// <summary>
/// Umbral de una métrica (sección 5, "Alertas"). Mayor es peor (CPU, RAM, disco, temperatura…).
/// Se dispara tras <see cref="LecturasParaDisparar"/> lecturas seguidas sobre el umbral y se cierra tras
/// <see cref="LecturasParaRecuperar"/> lecturas seguidas bajo <see cref="Recuperacion"/> (histéresis).
/// </summary>
public sealed record Umbral(
    string Metrica,
    double Advertencia,
    double Excepcion,
    double Recuperacion,
    int LecturasParaDisparar = 3,
    int LecturasParaRecuperar = 3);

public enum TipoCambio { Abierto, Escalado, Cerrado }

/// <summary>Cambio de estado de una alerta. <see cref="ClaveDedup"/> = host + tipo + métrica.</summary>
public sealed record CambioAlerta(TipoCambio Tipo, string ClaveDedup, string Metrica, Severidad Severidad, double Valor, double Umbral, DateTimeOffset Ts);

/// <summary>
/// Evalúa lecturas contra umbrales con histéresis y deduplicación: un evento abierto no genera otro igual;
/// solo produce cambios (abrir, escalar a excepción, cerrar).
/// </summary>
public sealed class EvaluadorAlertas
{
    private sealed class Estado
    {
        public Severidad? Abierta;
        public int SobreAdvertencia;
        public int SobreExcepcion;
        public int BajoRecuperacion;
        // Desde cuándo se cumple cada condición sin interrupción (para la persistencia mínima).
        public DateTimeOffset? DesdeAdvertencia;
        public DateTimeOffset? DesdeExcepcion;
        public DateTimeOffset? DesdeRecuperacion;
    }

    private readonly string _host;
    private Dictionary<string, Umbral> _umbrales;
    private readonly Dictionary<string, Estado> _estados = new(StringComparer.Ordinal);

    /// <summary>
    /// Tiempo mínimo que una condición debe mantenerse (además de las lecturas seguidas) para abrir, escalar o cerrar
    /// una alerta. Evita la "lluvia" de avisos y soluciones cuando un valor sube y baja (por defecto 8 min en el agente).
    /// </summary>
    public TimeSpan Persistencia { get; set; }

    public EvaluadorAlertas(string host, IEnumerable<Umbral> umbrales, TimeSpan? persistencia = null)
    {
        ArgumentNullException.ThrowIfNull(umbrales);
        _host = host;
        _umbrales = umbrales.ToDictionary(u => u.Metrica, StringComparer.Ordinal);
        Persistencia = persistencia ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Reemplaza los umbrales en caliente (configuración central). Las alertas abiertas y los contadores se conservan;
    /// solo se descarta el estado de métricas que ya no tienen umbral.
    /// </summary>
    public void ActualizarUmbrales(IEnumerable<Umbral> umbrales)
    {
        ArgumentNullException.ThrowIfNull(umbrales);
        _umbrales = umbrales.ToDictionary(u => u.Metrica, StringComparer.Ordinal);
        foreach (var metrica in _estados.Keys.Where(m => !_umbrales.ContainsKey(m)).ToList()) _estados.Remove(metrica);
    }

    public IReadOnlyList<CambioAlerta> Evaluar(IEnumerable<Lectura> lecturas)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        var cambios = new List<CambioAlerta>();
        foreach (var lectura in lecturas)
        {
            if (!_umbrales.TryGetValue(lectura.Metrica, out var umbral)) continue;
            if (!_estados.TryGetValue(lectura.Metrica, out var estado))
            {
                estado = new Estado();
                _estados[lectura.Metrica] = estado;
            }

            var cambio = Evaluar(lectura, umbral, estado);
            if (cambio is not null) cambios.Add(cambio);
        }
        return cambios;
    }

    private CambioAlerta? Evaluar(Lectura lectura, Umbral umbral, Estado estado)
    {
        var v = lectura.Valor;
        var ts = lectura.Ts;
        estado.SobreAdvertencia = v >= umbral.Advertencia ? estado.SobreAdvertencia + 1 : 0;
        estado.SobreExcepcion = v >= umbral.Excepcion ? estado.SobreExcepcion + 1 : 0;
        estado.BajoRecuperacion = v < umbral.Recuperacion ? estado.BajoRecuperacion + 1 : 0;
        estado.DesdeAdvertencia = estado.SobreAdvertencia > 0 ? estado.DesdeAdvertencia ?? ts : null;
        estado.DesdeExcepcion = estado.SobreExcepcion > 0 ? estado.DesdeExcepcion ?? ts : null;
        estado.DesdeRecuperacion = estado.BajoRecuperacion > 0 ? estado.DesdeRecuperacion ?? ts : null;

        // Lecturas seguidas suficientes Y la condición sostenida durante la persistencia mínima.
        bool Sostenida(int lecturas, int minimo, DateTimeOffset? desde) => lecturas >= minimo && desde is not null && ts - desde.Value >= Persistencia;

        var clave = $"{_host}|umbral|{lectura.Metrica}";

        if (estado.Abierta is null)
        {
            if (Sostenida(estado.SobreExcepcion, umbral.LecturasParaDisparar, estado.DesdeExcepcion))
            {
                estado.Abierta = Severidad.Excepcion;
                return new(TipoCambio.Abierto, clave, lectura.Metrica, Severidad.Excepcion, v, umbral.Excepcion, ts);
            }
            if (Sostenida(estado.SobreAdvertencia, umbral.LecturasParaDisparar, estado.DesdeAdvertencia))
            {
                estado.Abierta = Severidad.Advertencia;
                return new(TipoCambio.Abierto, clave, lectura.Metrica, Severidad.Advertencia, v, umbral.Advertencia, ts);
            }
            return null;
        }

        if (estado.Abierta == Severidad.Advertencia && Sostenida(estado.SobreExcepcion, umbral.LecturasParaDisparar, estado.DesdeExcepcion))
        {
            estado.Abierta = Severidad.Excepcion;
            return new(TipoCambio.Escalado, clave, lectura.Metrica, Severidad.Excepcion, v, umbral.Excepcion, ts);
        }

        if (Sostenida(estado.BajoRecuperacion, umbral.LecturasParaRecuperar, estado.DesdeRecuperacion))
        {
            var severidad = estado.Abierta.Value;
            estado.Abierta = null;
            estado.SobreAdvertencia = estado.SobreExcepcion = estado.BajoRecuperacion = 0;
            estado.DesdeAdvertencia = estado.DesdeExcepcion = estado.DesdeRecuperacion = null;
            return new(TipoCambio.Cerrado, clave, lectura.Metrica, severidad, v, umbral.Recuperacion, ts);
        }

        return null;
    }
}
