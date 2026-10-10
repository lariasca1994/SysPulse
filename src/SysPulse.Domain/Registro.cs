using System.Net.Mail;
using System.Text.RegularExpressions;
using PhoneNumbers;

namespace SysPulse.Domain;

/// <summary>Contacto de Telegram que la persona registra: su @usuario o su número con código de país.</summary>
public sealed record ContactoTelegram(string? Usuario, string? TelefonoE164)
{
    public bool EsTelefono => TelefonoE164 is not null;

    /// <summary>Teléfono enmascarado para mostrar (+57 ••• ••• 1234).</summary>
    public string Mostrar() => Usuario is not null ? "@" + Usuario : $"{TelefonoE164![..3]} ••• ••• {TelefonoE164[^4..]}";
}

/// <summary>Reglas del registro propio (sección 12: "Registro de cuentas, contraseña y MFA").</summary>
public static partial class ReglasRegistro
{
    public const int LongitudMinimaPassword = 12;

    // Muestra de contraseñas comunes/filtradas; se rechazan aunque cumplan la longitud.
    private static readonly HashSet<string> Comunes = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "contraseña123", "contrasena123", "password1234", "qwertyuiop12", "administrador",
        "abcdefghijkl", "111111111111", "000000000000", "iloveyou1234", "syspulse1234", "colombia1234",
    };

    private static readonly PhoneNumberUtil Telefonos = PhoneNumberUtil.GetInstance();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{4,31}$")]
    private static partial Regex UsuarioTelegram();

    public static string? ValidarNombre(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length < 3 || nombre.Trim().Length > 80
            ? "El nombre debe tener entre 3 y 80 caracteres."
            : null;

    public static string? ValidarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return "Ingresa un correo válido.";
        return MailAddress.TryCreate(email.Trim(), out var direccion) && direccion.Address == email.Trim() && direccion.Host.Contains('.', StringComparison.Ordinal)
            ? null
            : "Ingresa un correo válido.";
    }

    public static string? ValidarPassword(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < LongitudMinimaPassword)
            return $"La contraseña debe tener al menos {LongitudMinimaPassword} caracteres.";
        if (password.Length > 128) return "La contraseña no puede superar 128 caracteres.";
        if (Comunes.Contains(password)) return "Esa contraseña es demasiado común. Elige otra.";
        if (password.Distinct().Count() < 5) return "La contraseña repite demasiado los mismos caracteres.";
        var local = email?.Split('@')[0];
        if (local is { Length: >= 4 } && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return "La contraseña no debe contener tu correo.";
        return null;
    }

    /// <summary>Solo N1, N2 o N3: gestor se solicita y lo aprueba el admin; admin no se elige.</summary>
    public static string? ValidarNivel(string? nivel) =>
        nivel is not null && Roles.SeleccionablesEnRegistro.Contains(nivel) ? null : "Elige tu nivel: N1, N2 o N3.";

    /// <summary>
    /// Acepta "@usuario" / "usuario" o un número. Sin "+", el número se interpreta con <paramref name="paisPorDefecto"/> (CO = +57).
    /// </summary>
    public static (ContactoTelegram? Contacto, string? Error) ValidarTelegram(string? entrada, string paisPorDefecto = "CO")
    {
        if (string.IsNullOrWhiteSpace(entrada)) return (null, "Ingresa tu @usuario de Telegram o tu número con código de país.");
        var texto = entrada.Trim();

        var pareceTelefono = texto.StartsWith('+') || texto.All(c => char.IsDigit(c) || c is ' ' or '-' or '(' or ')');
        if (pareceTelefono)
        {
            try
            {
                var numero = Telefonos.Parse(texto, paisPorDefecto);
                if (!Telefonos.IsValidNumber(numero)) return (null, "El número no es válido para ese país.");
                return (new ContactoTelegram(null, Telefonos.Format(numero, PhoneNumberFormat.E164)), null);
            }
            catch (NumberParseException)
            {
                return (null, "El número no es válido. Inclúyelo con el código de país, por ejemplo +57 300 123 4567.");
            }
        }

        var usuario = texto.TrimStart('@');
        return UsuarioTelegram().IsMatch(usuario)
            ? (new ContactoTelegram(usuario, null), null)
            : (null, "El @usuario de Telegram debe tener entre 5 y 32 caracteres: letras, números y _ (empieza por letra).");
    }

    /// <summary>Compara el @usuario registrado con el que informa Telegram (sin distinguir mayúsculas).</summary>
    public static bool CoincideUsuario(string registrado, string? deTelegram) =>
        deTelegram is not null && string.Equals(registrado.TrimStart('@'), deTelegram.TrimStart('@'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Compara el número registrado (E.164) con el que comparte Telegram (a veces llega sin "+").</summary>
    public static bool CoincideTelefono(string registradoE164, string? deTelegram)
    {
        if (string.IsNullOrWhiteSpace(deTelegram)) return false;
        var normalizado = deTelegram.StartsWith('+') ? deTelegram : "+" + deTelegram;
        try
        {
            var numero = Telefonos.Parse(normalizado, null);
            return Telefonos.Format(numero, PhoneNumberFormat.E164) == registradoE164;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }
}
