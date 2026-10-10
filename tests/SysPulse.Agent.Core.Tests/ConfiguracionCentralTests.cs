using SysPulse.Agent.Core.Alertas;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Core.Tests;

public class ConfiguracionCentralTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Umbral CpuLocal = new("cpu.total", 85, 95, 75);
    private static readonly Umbral RamLocal = new("ram.pct", 85, 95, 80);

    [Fact]
    public void Combinar_ElUmbralCentralReemplazaAlLocalDeLaMismaMetrica()
    {
        var config = new ConfiguracionCentral { Umbrales = [new UmbralCentral("cpu.total", 70, 90, 60)] };

        var efectivos = config.Combinar([CpuLocal, RamLocal], "windows").ToDictionary(u => u.Metrica);

        Assert.Equal(70, efectivos["cpu.total"].Advertencia);
        Assert.Equal(85, efectivos["ram.pct"].Advertencia);
    }

    [Fact]
    public void Combinar_RespetaElTipoDeHost()
    {
        var config = new ConfiguracionCentral { Umbrales = [new UmbralCentral("cpu.total", 50, 60, 40, TipoHost: "android")] };

        var windows = config.Combinar([CpuLocal], "windows").Single();
        var android = config.Combinar([CpuLocal], "android").Single();

        Assert.Equal(85, windows.Advertencia);
        Assert.Equal(50, android.Advertencia);
    }

    [Fact]
    public void Combinar_PuedeAgregarMetricasNuevas()
    {
        var config = new ConfiguracionCentral { Umbrales = [new UmbralCentral("red.tcp_establecidas", 500, 1000, 400)] };

        Assert.Equal(2, config.Combinar([CpuLocal], "windows").Count);
    }

    [Fact]
    public void ActualizarUmbrales_ConservaLaAlertaAbierta()
    {
        var evaluador = new EvaluadorAlertas("pc", [CpuLocal]);
        for (var i = 0; i < 3; i++) evaluador.Evaluar([new Lectura("cpu.total", 90, "%", T0.AddMinutes(i))]);

        evaluador.ActualizarUmbrales([new Umbral("cpu.total", 80, 95, 70)]);
        var cambios = evaluador.Evaluar([new Lectura("cpu.total", 90, "%", T0.AddMinutes(5))]);

        Assert.Empty(cambios); // sigue abierta: no se vuelve a abrir ni a avisar
    }

    [Fact]
    public void CopiaLocal_SeGuardaYSeLee()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfiguracionCentral { TelegramChatIds = ["1", "2", "3", "4"], Actualizado = T0, Umbrales = [new UmbralCentral("cpu.total", 1, 2, 0.5)] };
            config.GuardarCopia(ruta);

            var leida = ConfiguracionCentral.LeerCopia(ruta);

            Assert.NotNull(leida);
            Assert.Equal(4, leida.TelegramChatIds.Count);
            Assert.Equal(T0, leida.Actualizado);
            Assert.Single(leida.Umbrales);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public void CopiaLocal_DanadaOAusente_DevuelveNull()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");
        Assert.Null(ConfiguracionCentral.LeerCopia(ruta));
        File.WriteAllText(ruta, "{ no es json");
        try
        {
            Assert.Null(ConfiguracionCentral.LeerCopia(ruta));
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
