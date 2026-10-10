using SysPulse.Agent.Core.Envio;
using SysPulse.Contracts;

namespace SysPulse.Agent.Core.Tests;

public class NotificadorRespaldoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static EventoDto Evento(string tipo, string severidad, double valor, double umbral) =>
        new("win-0123456789", "windows", tipo, severidad, "cpu.total", valor, umbral, "k", T0);

    [Fact]
    public void Apertura_MuestraUmbralDeExcepcionYNombreGenerico()
    {
        var texto = NotificadorRespaldo.Formatear(Evento("abierto", "excepcion", 97.4, 95), "Equipo portátil");

        Assert.Contains("Excepción abierta", texto, StringComparison.Ordinal);
        Assert.Contains("Equipo: Equipo portátil", texto, StringComparison.Ordinal);
        Assert.Contains("cpu.total = 97.4 (umbral de excepción: 95)", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("win-", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Cierre_ExplicaQueEsElUmbralDeRecuperacion()
    {
        var texto = NotificadorRespaldo.Formatear(Evento("cerrado", "excepcion", 63.7, 75), "Celular");

        Assert.Contains("Resuelta", texto, StringComparison.Ordinal);
        Assert.Contains("la alerta se cierra por debajo de 75", texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("excepcion", true)]
    [InlineData("advertencia", false)]
    [InlineData("informativo", false)]
    public void SoloLasExcepcionesSalenPorElRespaldo(string severidad, bool avisa)
    {
        Assert.Equal(avisa, NotificadorRespaldo.DebeAvisar(Evento("abierto", severidad, 1, 1)));
    }
}
