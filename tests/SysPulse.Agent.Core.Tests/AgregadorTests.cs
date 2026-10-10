using SysPulse.Agent.Core.Agregacion;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Core.Tests;

public class AgregadorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resumir_CalculaMinAvgMaxYP95PorMetrica()
    {
        var lecturas = Enumerable.Range(1, 5)
            .Select(i => new Lectura("cpu.total", i * 10, "%", T0.AddMinutes(i)))
            .Append(new Lectura("ram.pct", 50, "%", T0));

        var resumen = Agregador.Resumir(lecturas);

        var cpu = resumen["cpu.total"];
        Assert.Equal(10, cpu.Min);
        Assert.Equal(30, cpu.Avg);
        Assert.Equal(50, cpu.Max);
        Assert.Equal(48, cpu.P95, precision: 6);
        Assert.Equal(5, cpu.Muestras);
        Assert.Equal("%", cpu.Unidad);
        Assert.Equal(1, resumen["ram.pct"].Muestras);
    }

    [Fact]
    public void Percentil_ConUnSoloValor_DevuelveEseValor()
    {
        Assert.Equal(7, Agregador.Percentil([7], 95));
    }

    [Fact]
    public void Percentil_SinValores_EsInvalido()
    {
        Assert.Throws<ArgumentException>(() => Agregador.Percentil([], 95));
    }

    [Theory]
    [InlineData(7, 5)]
    [InlineData(5, 5)]
    [InlineData(4, 0)]
    [InlineData(59, 55)]
    public void InicioVentana_RedondeaHaciaAbajoA5Minutos(int minuto, int esperado)
    {
        var inicio = Agregador.InicioVentana(T0.AddMinutes(minuto).AddSeconds(30), TimeSpan.FromMinutes(5));

        Assert.Equal(T0.AddMinutes(esperado), inicio);
    }
}
