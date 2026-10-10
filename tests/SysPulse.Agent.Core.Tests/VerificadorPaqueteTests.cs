using SysPulse.Agent.Core.Actualizaciones;

namespace SysPulse.Agent.Core.Tests;

public class VerificadorPaqueteTests
{
    [Theory]
    [InlineData("https://github.com/x/y/releases/download/v1.2.0/agente.zip", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]
    [InlineData("https://release-assets.githubusercontent.com/abc", true)]
    [InlineData("http://github.com/x/y/releases/download/v1.2.0/agente.zip", false)]
    [InlineData("https://github.com.evil.example/agente.zip", false)]
    [InlineData("https://example.com/agente.zip", false)]
    [InlineData("file:///C:/agente.zip", false)]
    public void SoloGitHubPorHttps(string url, bool permitida)
    {
        Assert.Equal(permitida, VerificadorPaquete.UrlPermitida(new Uri(url)));
    }

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.2.0", "1.1.0+a1b2c3", true)]
    [InlineData("basura", "1.1.0", false)]
    public void ComparaVersiones(string disponible, string instalada, bool actualiza)
    {
        Assert.Equal(actualiza, VerificadorPaquete.EsMasNueva(disponible, instalada));
    }

    [Fact]
    public void HuellaDetectaArchivoAlterado()
    {
        var ruta = Path.GetTempFileName();
        try
        {
            File.WriteAllText(ruta, "paquete original");
            var huella = VerificadorPaquete.Huella(ruta);

            Assert.True(VerificadorPaquete.HuellaValida(ruta, huella.ToUpperInvariant()));
            File.AppendAllText(ruta, "!");
            Assert.False(VerificadorPaquete.HuellaValida(ruta, huella));
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public async Task UrlNoPermitida_NoDescargaNada()
    {
        var destino = Path.Combine(Path.GetTempPath(), $"syspulse-{Guid.NewGuid():N}.zip");
        var paquete = new Configuracion.PaqueteActualizacion("9.9.9", "https://example.com/agente.zip", "00");

        await Assert.ThrowsAsync<InvalidOperationException>(() => VerificadorPaquete.DescargarAsync(paquete, destino, CancellationToken.None));
        Assert.False(File.Exists(destino));
    }
}
