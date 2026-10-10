using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Tests;

public class EvaluadorAlertasTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Umbral Cpu = new("cpu.total", Advertencia: 85, Excepcion: 95, Recuperacion: 75);

    private static EvaluadorAlertas Nuevo() => new("pc", [Cpu]);

    private static List<CambioAlerta> Alimentar(EvaluadorAlertas evaluador, params double[] valores)
    {
        var cambios = new List<CambioAlerta>();
        for (var i = 0; i < valores.Length; i++)
        {
            cambios.AddRange(evaluador.Evaluar([new Lectura("cpu.total", valores[i], "%", T0.AddMinutes(i))]));
        }
        return cambios;
    }

    [Fact]
    public void DosLecturasAltas_NoDisparan()
    {
        Assert.Empty(Alimentar(Nuevo(), 90, 90));
    }

    [Fact]
    public void TresLecturasSobreAdvertencia_AbrenAdvertencia()
    {
        var cambio = Assert.Single(Alimentar(Nuevo(), 90, 90, 90));

        Assert.Equal(TipoCambio.Abierto, cambio.Tipo);
        Assert.Equal(Severidad.Advertencia, cambio.Severidad);
        Assert.Equal("pc|umbral|cpu.total", cambio.ClaveDedup);
    }

    [Fact]
    public void PicoIntermitente_NoDispara()
    {
        Assert.Empty(Alimentar(Nuevo(), 90, 90, 50, 90, 90, 50));
    }

    [Fact]
    public void AlertaAbierta_NoSeDuplica()
    {
        var cambios = Alimentar(Nuevo(), 90, 90, 90, 90, 90, 90, 90);

        Assert.Single(cambios);
    }

    [Fact]
    public void TresLecturasSobreExcepcion_AbrenDirectamenteExcepcion()
    {
        var cambio = Assert.Single(Alimentar(Nuevo(), 99, 99, 99));

        Assert.Equal(Severidad.Excepcion, cambio.Severidad);
    }

    [Fact]
    public void AdvertenciaQueEmpeora_SeEscalaAExcepcion()
    {
        var cambios = Alimentar(Nuevo(), 90, 90, 90, 99, 99, 99);

        Assert.Equal([TipoCambio.Abierto, TipoCambio.Escalado], cambios.Select(c => c.Tipo));
        Assert.Equal(Severidad.Excepcion, cambios[1].Severidad);
    }

    [Fact]
    public void Histeresis_BajarDelUmbralPeroNoDeLaRecuperacion_NoCierra()
    {
        var cambios = Alimentar(Nuevo(), 90, 90, 90, 80, 80, 80, 80);

        Assert.Single(cambios);
    }

    [Fact]
    public void TresLecturasBajoRecuperacion_Cierran()
    {
        var cambios = Alimentar(Nuevo(), 90, 90, 90, 70, 70, 70);

        Assert.Equal([TipoCambio.Abierto, TipoCambio.Cerrado], cambios.Select(c => c.Tipo));
        Assert.Equal(Severidad.Advertencia, cambios[1].Severidad);
    }

    [Fact]
    public void TrasCerrar_PuedeVolverAAbrir()
    {
        var cambios = Alimentar(Nuevo(), 90, 90, 90, 70, 70, 70, 90, 90, 90);

        Assert.Equal([TipoCambio.Abierto, TipoCambio.Cerrado, TipoCambio.Abierto], cambios.Select(c => c.Tipo));
    }

    [Fact]
    public void MetricaSinUmbral_SeIgnora()
    {
        var evaluador = Nuevo();
        var cambios = evaluador.Evaluar([new Lectura("ram.pct", 100, "%", T0), new Lectura("ram.pct", 100, "%", T0), new Lectura("ram.pct", 100, "%", T0)]);

        Assert.Empty(cambios);
    }
}
