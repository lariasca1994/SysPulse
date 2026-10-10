using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Domain;

namespace SysPulse.Agent.Core.Tests;

/// <summary>Persistencia mínima de 8 minutos: sin "lluvia" de alertas ni de soluciones cuando el valor sube y baja.</summary>
public class PersistenciaAlertasTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Umbral Cpu = new("cpu.total", Advertencia: 85, Excepcion: 95, Recuperacion: 75);
    private static readonly TimeSpan OchoMinutos = TimeSpan.FromMinutes(8);

    /// <summary>Una lectura por minuto, como el agente.</summary>
    private static List<CambioAlerta> Alimentar(EvaluadorAlertas e, int desdeMinuto, params double[] valores)
    {
        var cambios = new List<CambioAlerta>();
        for (var i = 0; i < valores.Length; i++)
        {
            cambios.AddRange(e.Evaluar([new Lectura("cpu.total", valores[i], "%", T0.AddMinutes(desdeMinuto + i))]));
        }
        return cambios;
    }

    [Fact]
    public void PicoDeCincoMinutos_NoAvisa()
    {
        var e = new EvaluadorAlertas("pc", [Cpu], OchoMinutos);

        Assert.Empty(Alimentar(e, 0, 99, 99, 99, 99, 99, 60, 60));
    }

    [Fact]
    public void ExcepcionSostenida8Minutos_AbreEnElMinuto8()
    {
        var e = new EvaluadorAlertas("pc", [Cpu], OchoMinutos);

        // Minutos 0..7: aún no; en el minuto 8 la condición ya lleva 8 minutos.
        Assert.Empty(Alimentar(e, 0, 99, 99, 99, 99, 99, 99, 99, 99));
        var cambio = Assert.Single(Alimentar(e, 8, 99));
        Assert.Equal(TipoCambio.Abierto, cambio.Tipo);
        Assert.Equal(Severidad.Excepcion, cambio.Severidad);
    }

    [Fact]
    public void UnaLecturaNormalEnMedio_ReiniciaLaEspera()
    {
        var e = new EvaluadorAlertas("pc", [Cpu], OchoMinutos);

        Assert.Empty(Alimentar(e, 0, 99, 99, 99, 99, 99, 50, 99, 99, 99, 99));
    }

    [Fact]
    public void BajadaBreve_NoCierraLaAlerta_YBajadaSostenida_SiLaCierra()
    {
        var e = new EvaluadorAlertas("pc", [Cpu], OchoMinutos);
        Assert.Single(Alimentar(e, 0, 99, 99, 99, 99, 99, 99, 99, 99, 99));

        // 4 minutos por debajo y vuelve a subir: sigue abierta, sin aviso de "solucionada".
        Assert.Empty(Alimentar(e, 9, 60, 60, 60, 60, 99));

        // 8 minutos por debajo de la recuperación: se cierra una sola vez.
        var cambios = Alimentar(e, 14, 60, 60, 60, 60, 60, 60, 60, 60, 60);
        var cierre = Assert.Single(cambios);
        Assert.Equal(TipoCambio.Cerrado, cierre.Tipo);
    }

    [Fact]
    public void LaPersistenciaSePuedeCambiarEnCaliente()
    {
        var e = new EvaluadorAlertas("pc", [Cpu], OchoMinutos) { Persistencia = TimeSpan.FromMinutes(2) };

        Assert.Single(Alimentar(e, 0, 99, 99, 99));
    }
}
