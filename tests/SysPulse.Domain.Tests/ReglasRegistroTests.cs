namespace SysPulse.Domain.Tests;

public class ReglasRegistroTests
{
    [Theory]
    [InlineData("ana@example.com", true)]
    [InlineData("Ana Pérez <ana@example.com>", false)]
    [InlineData("ana@localhost", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Email(string? email, bool valido) => Assert.Equal(valido, ReglasRegistro.ValidarEmail(email) is null);

    [Theory]
    [InlineData("Corta1!", false)]
    [InlineData("password1234", false)]
    [InlineData("aaaaaaaaaaaaaa", false)]
    [InlineData("ana.lopez.2026!", false)]
    [InlineData("Montaña-Azul-73", true)]
    public void Password(string password, bool valida) => Assert.Equal(valida, ReglasRegistro.ValidarPassword(password, "ana.lopez@example.com") is null);

    [Theory]
    [InlineData("N1", true)]
    [InlineData("N3", true)]
    [InlineData("gestor", false)]
    [InlineData("admin", false)]
    [InlineData(null, false)]
    public void Nivel_SoloN1N2N3(string? nivel, bool valido) => Assert.Equal(valido, ReglasRegistro.ValidarNivel(nivel) is null);

    [Theory]
    [InlineData("@AnaPerez7", "AnaPerez7", null)]
    [InlineData("AnaPerez7", "AnaPerez7", null)]
    [InlineData("300 123 4567", null, "+573001234567")]
    [InlineData("+57 300 123 4567", null, "+573001234567")]
    [InlineData("+1 (415) 555-2671", null, "+14155552671")]
    public void Telegram_Valido(string entrada, string? usuario, string? telefono)
    {
        var (contacto, error) = ReglasRegistro.ValidarTelegram(entrada);

        Assert.Null(error);
        Assert.Equal(usuario, contacto!.Usuario);
        Assert.Equal(telefono, contacto.TelefonoE164);
    }

    [Theory]
    [InlineData("@ab")]
    [InlineData("@9empieza")]
    [InlineData("123")]
    [InlineData("")]
    public void Telegram_Invalido(string entrada) => Assert.NotNull(ReglasRegistro.ValidarTelegram(entrada).Error);

    [Fact]
    public void Telefono_SeMuestraEnmascarado()
    {
        Assert.Equal("+57 ••• ••• 4567", new ContactoTelegram(null, "+573001234567").Mostrar());
        Assert.Equal("@AnaPerez7", new ContactoTelegram("AnaPerez7", null).Mostrar());
    }

    [Theory]
    [InlineData("AnaPerez7", "anaperez7", true)]
    [InlineData("AnaPerez7", "@AnaPerez7", true)]
    [InlineData("AnaPerez7", "AnaPerez8", false)]
    [InlineData("AnaPerez7", null, false)]
    public void CoincideUsuario(string registrado, string? deTelegram, bool coincide) =>
        Assert.Equal(coincide, ReglasRegistro.CoincideUsuario(registrado, deTelegram));

    [Theory]
    [InlineData("+573001234567", "573001234567", true)]
    [InlineData("+573001234567", "+573001234567", true)]
    [InlineData("+573001234567", "573001234568", false)]
    [InlineData("+573001234567", null, false)]
    public void CoincideTelefono(string registrado, string? deTelegram, bool coincide) =>
        Assert.Equal(coincide, ReglasRegistro.CoincideTelefono(registrado, deTelegram));
}
