using SysPulse.Domain;

namespace SysPulse.Domain.Tests;

public class CalculadoraSlaTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-5));
    private static readonly TimeSpan CuatroHoras = TimeSpan.FromHours(4);

    [Fact]
    public void SinEsperas_A2Horas_Consume50PorCiento()
    {
        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, [], Inicio.AddHours(2));

        Assert.Equal(50, estado.Porcentaje, precision: 6);
        Assert.Equal(UmbralSla.Ninguno, estado.Umbral);
        Assert.Equal(Inicio.AddHours(4), estado.Vence);
    }

    [Fact]
    public void Al75PorCiento_DisparaAviso()
    {
        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, [], Inicio.AddHours(3));

        Assert.Equal(UmbralSla.Aviso75, estado.Umbral);
    }

    [Fact]
    public void Al100PorCiento_MarcaVencido()
    {
        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, [], Inicio.AddHours(4));

        Assert.Equal(UmbralSla.Vencido100, estado.Umbral);
    }

    [Fact]
    public void EsperaCerrada_PausaElSlaYCorreElVencimiento()
    {
        PeriodoEspera[] esperas = [new(Inicio.AddHours(1), Inicio.AddHours(2))];

        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, esperas, Inicio.AddHours(4));

        Assert.Equal(TimeSpan.FromHours(3), estado.Consumido);
        Assert.Equal(UmbralSla.Aviso75, estado.Umbral);
        Assert.Equal(Inicio.AddHours(5), estado.Vence);
    }

    [Fact]
    public void EsperaAbierta_CuentaHastaAhora()
    {
        PeriodoEspera[] esperas = [new(Inicio.AddHours(1), null)];

        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, esperas, Inicio.AddHours(10));

        Assert.Equal(TimeSpan.FromHours(1), estado.Consumido);
        Assert.Equal(UmbralSla.Ninguno, estado.Umbral);
    }

    [Fact]
    public void EsperaQueEmpiezaAntesDelInicio_SoloCuentaDesdeElInicio()
    {
        PeriodoEspera[] esperas = [new(Inicio.AddHours(-1), Inicio.AddHours(1))];

        var estado = CalculadoraSla.Calcular(Inicio, CuatroHoras, esperas, Inicio.AddHours(2));

        Assert.Equal(TimeSpan.FromHours(1), estado.Consumido);
    }

    [Fact]
    public void ObjetivoCero_EsInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraSla.Calcular(Inicio, TimeSpan.Zero, [], Inicio));
    }

    [Theory]
    [InlineData(Prioridad.P1, 15, 4)]
    [InlineData(Prioridad.P2, 30, 8)]
    [InlineData(Prioridad.P3, 120, 24)]
    [InlineData(Prioridad.P4, 480, 72)]
    [InlineData(Prioridad.P5, 1440, 120)]
    public void ObjetivosPorDefecto_CoincidenConLaEspecificacion(Prioridad prioridad, int respuestaMin, int resolucionHoras)
    {
        var objetivo = ObjetivosSla.PorDefecto[prioridad];

        Assert.Equal(TimeSpan.FromMinutes(respuestaMin), objetivo.Respuesta);
        Assert.Equal(TimeSpan.FromHours(resolucionHoras), objetivo.Resolucion);
    }
}
