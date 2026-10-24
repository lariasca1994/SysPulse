using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Infrastructure.Tests;

public class DosPasosTests
{
    private static UsuarioDoc Usuario(bool app, bool correo, string? preferido) =>
        new() { Mfa = new MfaDoc { Activo = app, CorreoActivo = correo, Preferido = preferido } };

    [Fact]
    public void SinMetodos_NoEstaConfigurado()
    {
        var estado = ServicioCuentas.Estado(Usuario(false, false, null));

        Assert.False(estado.Configurado);
        Assert.Null(estado.Preferido);
    }

    [Fact]
    public void SoloCorreo_EstaConfigurado_YSePideElCorreo()
    {
        var estado = ServicioCuentas.Estado(Usuario(false, true, null));

        Assert.True(estado.Configurado);
        Assert.Equal(ServicioCuentas.MetodoCorreo, estado.Preferido);
    }

    [Fact]
    public void ConLosDos_RespetaElPreferido()
    {
        Assert.Equal(ServicioCuentas.MetodoCorreo, ServicioCuentas.Estado(Usuario(true, true, "correo")).Preferido);
        Assert.Equal(ServicioCuentas.MetodoApp, ServicioCuentas.Estado(Usuario(true, true, "app")).Preferido);
    }

    [Fact]
    public void PreferidoInactivo_CaeEnElMetodoActivo()
    {
        // Cuentas anteriores al código por correo: solo app y sin preferido guardado.
        Assert.Equal(ServicioCuentas.MetodoApp, ServicioCuentas.Estado(Usuario(true, false, null)).Preferido);
        Assert.Equal(ServicioCuentas.MetodoApp, ServicioCuentas.Estado(Usuario(true, false, "correo")).Preferido);
    }

    [Fact]
    public void PlantillaDelCodigo_LoMuestraYEscapaElNombre()
    {
        var (asunto, html, texto) = PlantillasCorreo.CodigoVerificacion("<Ana>", "123456");

        Assert.StartsWith("123456", asunto, StringComparison.Ordinal);
        Assert.Contains("123456", html, StringComparison.Ordinal);
        Assert.Contains("&lt;Ana&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Ana>", html, StringComparison.Ordinal);
        Assert.Contains("10 minutos", texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ResultadoCodigo.Bloqueado, "15 minutos")]
    [InlineData(ResultadoCodigo.Vencido, "venció")]
    [InlineData(ResultadoCodigo.Incorrecto, "no es correcto")]
    public void Mensajes_SonClaros(ResultadoCodigo resultado, string fragmento) =>
        Assert.Contains(fragmento, ServicioCuentas.Mensaje(resultado), StringComparison.Ordinal);
}
