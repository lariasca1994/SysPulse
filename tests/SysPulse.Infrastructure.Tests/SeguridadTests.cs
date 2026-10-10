using SysPulse.Infrastructure.Seguridad;

namespace SysPulse.Infrastructure.Tests;

public class CifradoTests
{
    private static readonly Cifrado Cifrador = new("clave-maestra-de-prueba-con-mas-de-32-caracteres");

    [Fact]
    public void CifrarYDescifrar_RecuperaElTexto()
    {
        var cifrado = Cifrador.Cifrar("+573001234567");

        Assert.NotEqual("+573001234567", cifrado);
        Assert.Equal("+573001234567", Cifrador.Descifrar(cifrado));
    }

    [Fact]
    public void CadaCifrado_EsDistinto_PorElNonceAleatorio()
    {
        Assert.NotEqual(Cifrador.Cifrar("mismo"), Cifrador.Cifrar("mismo"));
    }

    [Fact]
    public void ConOtraClave_NoSePuedeDescifrar()
    {
        var cifrado = Cifrador.Cifrar("secreto");
        var otro = new Cifrado("otra-clave-maestra-distinta-de-mas-de-32-caracteres");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otro.Descifrar(cifrado));
    }

    [Fact]
    public void DatoAlterado_SeDetecta()
    {
        var bytes = Convert.FromBase64String(Cifrador.Cifrar("secreto"));
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Cifrador.Descifrar(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void ClaveCorta_EsRechazada()
    {
        Assert.Throws<ArgumentException>(() => new Cifrado("corta"));
    }

    [Fact]
    public void Codigo6_SiempreTieneSeisDigitos()
    {
        for (var i = 0; i < 200; i++)
        {
            var c = Cifrado.Codigo6();
            Assert.Equal(6, c.Length);
            Assert.True(c.All(char.IsDigit));
        }
    }

    [Fact]
    public void TokenAleatorio_EsAptoParaUrlYTelegram()
    {
        var token = Cifrado.TokenAleatorio(24);

        Assert.InRange(token.Length, 30, 64); // límite del parámetro start de Telegram: 64
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }
}

public class MfaTests
{
    private static readonly DateTime Ahora = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CodigoActual_EsAceptado()
    {
        var secreto = Mfa.NuevoSecreto();

        Assert.NotNull(Mfa.Verificar(secreto, Mfa.CodigoActual(secreto, Ahora), 0, Ahora));
    }

    [Fact]
    public void CodigoDe30sAntes_SeAcepta_PorDesfaseDeReloj()
    {
        var secreto = Mfa.NuevoSecreto();

        Assert.NotNull(Mfa.Verificar(secreto, Mfa.CodigoActual(secreto, Ahora.AddSeconds(-30)), 0, Ahora));
    }

    [Fact]
    public void CodigoDe2MinutosAntes_SeRechaza()
    {
        var secreto = Mfa.NuevoSecreto();

        Assert.Null(Mfa.Verificar(secreto, Mfa.CodigoActual(secreto, Ahora.AddMinutes(-2)), 0, Ahora));
    }

    [Fact]
    public void MismoCodigo_NoSePuedeReutilizar()
    {
        var secreto = Mfa.NuevoSecreto();
        var codigo = Mfa.CodigoActual(secreto, Ahora);
        var paso = Mfa.Verificar(secreto, codigo, 0, Ahora)!.Value;

        Assert.Null(Mfa.Verificar(secreto, codigo, paso, Ahora));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public void CodigoMalFormado_SeRechaza(string? codigo)
    {
        Assert.Null(Mfa.Verificar(Mfa.NuevoSecreto(), codigo, 0, Ahora));
    }

    [Fact]
    public void UriQr_TieneEmisorYCorreo()
    {
        var uri = Mfa.UriConfiguracion("JBSWY3DPEHPK3PXP", "ana@example.com");

        Assert.StartsWith("otpauth://totp/SysPulse:ana%40example.com?secret=JBSWY3DPEHPK3PXP", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=SysPulse", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void CodigosRecuperacion_SonDiezUnicosYLegibles()
    {
        var codigos = Mfa.CodigosRecuperacion();

        Assert.Equal(10, codigos.Count);
        Assert.Equal(10, codigos.Distinct().Count());
        Assert.All(codigos, c => Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", c));
    }

    [Fact]
    public void CodigoRecuperacion_SeNormaliza()
    {
        Assert.Equal("ABCD-EFGH", Mfa.NormalizarCodigoRecuperacion(" abcd-efgh "));
    }
}
