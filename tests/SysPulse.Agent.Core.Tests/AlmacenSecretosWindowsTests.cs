using System.Runtime.Versioning;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class AlmacenSecretosWindowsTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"syspulse-test-{Guid.NewGuid():N}", AlmacenSecretosWindows.NombreArchivo);

    [Fact]
    public void Guardar_Y_Leer_RecuperaLosMismosSecretos()
    {
        if (!OperatingSystem.IsWindows()) return;
        var secretos = new Dictionary<string, string> { ["MONGO_URI"] = "mongodb://u:p@h:27017/db", ["AGENT_API_KEY"] = "clave-ñ-123" };

        AlmacenSecretosWindows.Guardar(_ruta, secretos);
        var leidos = AlmacenSecretosWindows.Leer(_ruta);

        Assert.Equal(secretos, leidos);
    }

    [Fact]
    public void ElArchivo_NoContieneLosSecretosEnClaro()
    {
        if (!OperatingSystem.IsWindows()) return;
        AlmacenSecretosWindows.Guardar(_ruta, new Dictionary<string, string> { ["AGENT_API_KEY"] = "valor-muy-secreto" });

        var contenido = File.ReadAllText(_ruta);

        Assert.DoesNotContain("valor-muy-secreto", contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("AGENT_API_KEY", contenido, StringComparison.Ordinal);
    }

    [Fact]
    public void Leer_SinArchivo_DevuelveNull()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(AlmacenSecretosWindows.Leer(_ruta));
    }

    public void Dispose()
    {
        var carpeta = Path.GetDirectoryName(_ruta)!;
        if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
    }
}
