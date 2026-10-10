namespace SysPulse.Domain;

public sealed record ObjetivoSla(TimeSpan Respuesta, TimeSpan Resolucion);

/// <summary>Objetivos por prioridad (sección 11). El admin puede reemplazarlos por configuración.</summary>
public static class ObjetivosSla
{
    public static readonly IReadOnlyDictionary<Prioridad, ObjetivoSla> PorDefecto = new Dictionary<Prioridad, ObjetivoSla>
    {
        [Prioridad.P1] = new(TimeSpan.FromMinutes(15), TimeSpan.FromHours(4)),
        [Prioridad.P2] = new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(8)),
        [Prioridad.P3] = new(TimeSpan.FromHours(2), TimeSpan.FromHours(24)),
        [Prioridad.P4] = new(TimeSpan.FromHours(8), TimeSpan.FromHours(72)),
        [Prioridad.P5] = new(TimeSpan.FromHours(24), TimeSpan.FromDays(5)),
    };
}

/// <summary>Intervalo en el que el incidente estuvo "En espera" (pausa el SLA). Fin nulo = sigue en espera.</summary>
public sealed record PeriodoEspera(DateTimeOffset Inicio, DateTimeOffset? Fin);

public enum UmbralSla { Ninguno, Aviso75, Vencido100 }

public sealed record EstadoSla(TimeSpan Consumido, TimeSpan Objetivo, DateTimeOffset Vence, double Porcentaje, UmbralSla Umbral);

/// <summary>
/// Calcula el SLA al consultar, a partir de marcas de tiempo (sin procesos en segundo plano).
/// La usan la API y el agente de Windows, que solo despierta a la API cuando se cruza un umbral.
/// </summary>
public static class CalculadoraSla
{
    public static EstadoSla Calcular(DateTimeOffset inicio, TimeSpan objetivo, IEnumerable<PeriodoEspera> esperas, DateTimeOffset ahora)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(objetivo, TimeSpan.Zero);

        var pausado = TimeSpan.Zero;
        foreach (var espera in esperas)
        {
            var desde = espera.Inicio < inicio ? inicio : espera.Inicio;
            var hasta = espera.Fin ?? ahora;
            if (hasta > ahora) hasta = ahora;
            if (hasta > desde) pausado += hasta - desde;
        }

        var transcurrido = ahora - inicio;
        var consumido = transcurrido - pausado;
        if (consumido < TimeSpan.Zero) consumido = TimeSpan.Zero;

        var porcentaje = consumido / objetivo * 100;
        var umbral = porcentaje >= 100 ? UmbralSla.Vencido100
            : porcentaje >= 75 ? UmbralSla.Aviso75
            : UmbralSla.Ninguno;

        return new EstadoSla(consumido, objetivo, inicio + objetivo + pausado, porcentaje, umbral);
    }
}
