namespace SysPulse.Domain;

/// <summary>Roles de la plataforma (sección 12). Todos ven todo; los permisos de escritura dependen del rol.</summary>
public static class Roles
{
    public const string N1 = "N1";
    public const string N2 = "N2";
    public const string N3 = "N3";
    public const string Gestor = "gestor";
    public const string Admin = "admin";

    /// <summary>Niveles que una persona puede elegir al registrarse. Gestor solo lo asigna el admin.</summary>
    public static readonly IReadOnlyList<string> SeleccionablesEnRegistro = [N1, N2, N3];
}

public enum NivelEscalamiento { N1 = 1, N2 = 2, N3 = 3 }

public enum Severidad { Informativo, Advertencia, Excepcion }
