using SysPulse.Infrastructure.Correo;
using SysPulse.Infrastructure.Cuentas;
using SysPulse.Infrastructure.Seguridad;

namespace SysPulse.Infrastructure.Tests;

/// <summary>"Olvidé mi contraseña" (solo contraseña nueva) y "Perdí mi autenticador" (código al correo).</summary>
public class RecuperacionTests
{
    private static readonly DateTime Ahora = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Restablecer_SoloPideContrasenaYConfirmacion()
    {
        Assert.Null(ServicioCuentas.ValidarNuevaPassword("frase larga y segura 2026", "frase larga y segura 2026", "ana@ejemplo.com"));
        Assert.Null(ServicioCuentas.ValidarNuevaPassword("frase larga y segura 2026", null, "ana@ejemplo.com"));
    }

    [Fact]
    public void Restablecer_ConfirmacionDistinta_SeRechaza() =>
        Assert.Equal("Las contraseñas no coinciden.",
            ServicioCuentas.ValidarNuevaPassword("frase larga y segura 2026", "otra frase distinta 2026", "ana@ejemplo.com"));

    [Fact]
    public void Restablecer_ContrasenaDebil_SeRechaza() =>
        Assert.NotNull(ServicioCuentas.ValidarNuevaPassword("corta", "corta", "ana@ejemplo.com"));

    [Fact]
    public void PlantillaRestablecer_YaNoPideCodigoDeLaApp()
    {
        var (_, html, texto) = PlantillasCorreo.RestablecerPassword("Ana", "https://ejemplo.com/restablecer?token=x");

        Assert.DoesNotContain("app autenticadora", html, StringComparison.Ordinal);
        Assert.DoesNotContain("app autenticadora", texto, StringComparison.Ordinal);
        Assert.Contains("una sola vez", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void CodigoRecuperacionMfa_Correcto_Incorrecto_YVencido()
    {
        var hash = Cifrado.Hash("123456");
        var vence = Ahora + ServicioCuentas.VigenciaRecuperacionMfa;

        Assert.Equal(ResultadoCodigo.Correcto, ServicioCuentas.EvaluarCodigoCorreo(hash, vence, 0, " 123456 ", Ahora));
        Assert.Equal(ResultadoCodigo.Incorrecto, ServicioCuentas.EvaluarCodigoCorreo(hash, vence, 0, "654321", Ahora));
        Assert.Equal(ResultadoCodigo.Vencido, ServicioCuentas.EvaluarCodigoCorreo(hash, vence, 0, "123456", vence.AddSeconds(1)));
        Assert.Equal(ResultadoCodigo.Vencido, ServicioCuentas.EvaluarCodigoCorreo(null, vence, 0, "123456", Ahora));
        Assert.Equal(ResultadoCodigo.Vencido, ServicioCuentas.EvaluarCodigoCorreo(hash, vence, 5, "123456", Ahora));
    }

    [Fact]
    public void CodigoRecuperacionMfa_VenceEn15Minutos() =>
        Assert.Equal(TimeSpan.FromMinutes(15), ServicioCuentas.VigenciaRecuperacionMfa);

    [Fact]
    public void LimiteDeEnvios_TresPorVentana_YLosViejosNoCuentan()
    {
        var ventana = ServicioCuentas.VigenciaRecuperacionMfa;
        var tres = new[] { Ahora.AddMinutes(-1), Ahora.AddMinutes(-2), Ahora.AddMinutes(-3) };

        Assert.Null(ServicioCuentas.EnviosPermitidos(tres, Ahora, ventana, 3));
        var conViejo = ServicioCuentas.EnviosPermitidos([Ahora.AddMinutes(-20), Ahora.AddMinutes(-1)], Ahora, ventana, 3);
        Assert.NotNull(conViejo);
        Assert.Equal([Ahora.AddMinutes(-1), Ahora], conViejo);
    }

    [Fact]
    public void PlantillaRecuperacionMfa_MuestraElCodigo_YEscapaElNombre()
    {
        var (asunto, html, texto) = PlantillasCorreo.CodigoRecuperacionMfa("<Ana>", "123456");

        Assert.StartsWith("123456", asunto, StringComparison.Ordinal);
        Assert.Contains(PlantillasCorreo.NombrePortal, asunto, StringComparison.Ordinal);
        Assert.Contains("&lt;Ana&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Ana>", html, StringComparison.Ordinal);
        Assert.Contains("15 minutos", texto, StringComparison.Ordinal);
    }
}

/// <summary>El nombre descriptivo "Monitoreo e incidentes TI" en correos y en el emisor del QR.</summary>
public class NombrePortalTests
{
    [Fact]
    public void CorreosLlevanElNombreDescriptivo_EnAsuntoYEncabezado()
    {
        var plantillas = new[]
        {
            PlantillasCorreo.VerificarCorreo("Ana", "https://ejemplo.com"),
            PlantillasCorreo.CodigoVerificacion("Ana", "123456"),
            PlantillasCorreo.RestablecerPassword("Ana", "https://ejemplo.com"),
            PlantillasCorreo.CodigoRecuperacionMfa("Ana", "123456"),
            PlantillasCorreo.Aviso(new Notificaciones.ContenidoCorreo("[Monitoreo e incidentes TI] Prueba", "Prueba", "info", [], null, null)),
        };
        foreach (var (asunto, html, _) in plantillas)
        {
            Assert.Contains("Monitoreo e incidentes TI", asunto, StringComparison.Ordinal);
            Assert.Contains("Monitoreo e incidentes TI</td>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("SysPulse", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null, "Monitoreo e incidentes TI")]
    [InlineData("", "Monitoreo e incidentes TI")]
    [InlineData("SysPulse", "Monitoreo e incidentes TI")]
    [InlineData("Mesa de ayuda", "Mesa de ayuda")]
    public void Remitente_UsaElNombreDescriptivo(string? configurado, string esperado) =>
        Assert.Equal(esperado, PlantillasCorreo.NombreRemitente(configurado));

    [Fact]
    public void EmisorDelQr_EsElNombreDescriptivo()
    {
        var uri = Mfa.UriConfiguracion("JBSWY3DPEHPK3PXP", "usuario@ejemplo.com");

        Assert.Contains("issuer=Monitoreo%20e%20incidentes%20TI", uri, StringComparison.Ordinal);
        Assert.StartsWith("otpauth://totp/Monitoreo%20e%20incidentes%20TI:", uri, StringComparison.Ordinal);
    }
}
