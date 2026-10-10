namespace SysPulse.Domain;

public enum Impacto { Alto, Medio, Bajo }

public enum Urgencia { Alta, Media, Baja }

public enum Prioridad { P1 = 1, P2 = 2, P3 = 3, P4 = 4, P5 = 5 }

/// <summary>Prioridad ITIL = impacto × urgencia (sección 6 de la especificación).</summary>
public static class MatrizPrioridad
{
    public static Prioridad Calcular(Impacto impacto, Urgencia urgencia)
    {
        // Alto+Alta = P1; cada paso hacia abajo en impacto o urgencia baja un nivel.
        var nivel = 1 + (int)impacto + (int)urgencia;
        return (Prioridad)nivel;
    }
}
