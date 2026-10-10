namespace SysPulse.Domain;

/// <summary>Ciclo de vida del incidente (sección 11).</summary>
public enum EstadoIncidente { Nuevo, Asignado, EnProgreso, EnEspera, Resuelto, Cerrado, Reabierto }

public enum AccionIncidente { Tomar, Asignar, Iniciar, Pausar, Reanudar, Escalar, CambiarPrioridad, Resolver, Cerrar, Reabrir, Comentar }

/// <summary>Quién intenta la acción: id, rol y, si es técnico, su nivel.</summary>
public sealed record Actor(string Id, string Rol)
{
    public bool EsAdmin => Rol == Roles.Admin;
    public bool EsGestor => Rol == Roles.Gestor;

    /// <summary>Nivel técnico (1–3); 0 si no es técnico. El admin cuenta como N3 para recibir cualquier incidente.</summary>
    public int Nivel => Rol switch { Roles.N1 => 1, Roles.N2 => 2, Roles.N3 => 3, Roles.Admin => 3, _ => 0 };

    public bool EsTecnico => Nivel > 0;
}

/// <summary>Lo que las reglas necesitan saber del incidente.</summary>
public sealed record SituacionIncidente(EstadoIncidente Estado, NivelEscalamiento Nivel, Prioridad Prioridad, string? AsignadoId, DateTimeOffset? Resuelto);

/// <summary>
/// Reglas del incidente: transiciones válidas (una transición inválida la rechaza el dominio, nunca un if suelto
/// en el endpoint) y permisos por rol (matriz de la sección 12). Cada método devuelve null si se permite,
/// o el motivo del rechazo en lenguaje claro.
/// </summary>
public static class ReglasIncidente
{
    public static readonly TimeSpan PlazoReapertura = TimeSpan.FromDays(7);

    private static readonly Dictionary<AccionIncidente, EstadoIncidente[]> Origenes = new()
    {
        [AccionIncidente.Tomar] = [EstadoIncidente.Nuevo, EstadoIncidente.Reabierto],
        [AccionIncidente.Asignar] = [EstadoIncidente.Nuevo, EstadoIncidente.Asignado, EstadoIncidente.EnProgreso, EstadoIncidente.EnEspera, EstadoIncidente.Reabierto],
        [AccionIncidente.Iniciar] = [EstadoIncidente.Asignado, EstadoIncidente.Reabierto],
        [AccionIncidente.Pausar] = [EstadoIncidente.EnProgreso],
        [AccionIncidente.Reanudar] = [EstadoIncidente.EnEspera],
        [AccionIncidente.Escalar] = [EstadoIncidente.Nuevo, EstadoIncidente.Asignado, EstadoIncidente.EnProgreso, EstadoIncidente.EnEspera, EstadoIncidente.Reabierto],
        [AccionIncidente.CambiarPrioridad] = [EstadoIncidente.Nuevo, EstadoIncidente.Asignado, EstadoIncidente.EnProgreso, EstadoIncidente.EnEspera, EstadoIncidente.Reabierto],
        [AccionIncidente.Resolver] = [EstadoIncidente.Asignado, EstadoIncidente.EnProgreso, EstadoIncidente.Reabierto],
        [AccionIncidente.Cerrar] = [EstadoIncidente.Resuelto],
        [AccionIncidente.Reabrir] = [EstadoIncidente.Resuelto],
        [AccionIncidente.Comentar] = Enum.GetValues<EstadoIncidente>(),
    };

    public static bool Abierto(EstadoIncidente e) => e is not (EstadoIncidente.Resuelto or EstadoIncidente.Cerrado);

    /// <summary>Estado resultante de una acción permitida.</summary>
    public static EstadoIncidente Destino(AccionIncidente accion, EstadoIncidente actual) => accion switch
    {
        AccionIncidente.Tomar => EstadoIncidente.Asignado,
        AccionIncidente.Asignar => actual is EstadoIncidente.Nuevo or EstadoIncidente.Reabierto ? EstadoIncidente.Asignado : actual,
        AccionIncidente.Iniciar or AccionIncidente.Reanudar => EstadoIncidente.EnProgreso,
        AccionIncidente.Pausar => EstadoIncidente.EnEspera,
        // Al escalar, el incidente queda pendiente de que alguien del nuevo nivel lo tome.
        AccionIncidente.Escalar => EstadoIncidente.Nuevo,
        AccionIncidente.Resolver => EstadoIncidente.Resuelto,
        AccionIncidente.Cerrar => EstadoIncidente.Cerrado,
        AccionIncidente.Reabrir => EstadoIncidente.Reabierto,
        _ => actual,
    };

    /// <summary>Valida estado y permisos. <paramref name="prioridadNueva"/> solo aplica a CambiarPrioridad.</summary>
    public static string? Validar(AccionIncidente accion, SituacionIncidente inc, Actor actor, DateTimeOffset ahora, Prioridad? prioridadNueva = null)
    {
        ArgumentNullException.ThrowIfNull(inc);
        ArgumentNullException.ThrowIfNull(actor);

        if (!Origenes[accion].Contains(inc.Estado)) return $"No se puede {Verbo(accion)} un incidente en estado {Nombre(inc.Estado)}.";

        var asignadoAMi = inc.AsignadoId is not null && inc.AsignadoId == actor.Id;
        switch (accion)
        {
            case AccionIncidente.Comentar:
                return null;

            case AccionIncidente.Tomar:
                if (!actor.EsTecnico) return "Solo un técnico (N1, N2, N3) o el admin pueden tomar incidentes.";
                if (actor.Nivel < (int)inc.Nivel) return $"Este incidente está escalado a {inc.Nivel}; tu nivel no alcanza para tomarlo.";
                return null;

            case AccionIncidente.Asignar:
                return actor.EsGestor || actor.EsAdmin ? null : "Solo el gestor o el admin reasignan incidentes.";

            case AccionIncidente.Iniciar:
            case AccionIncidente.Pausar:
            case AccionIncidente.Reanudar:
            case AccionIncidente.Resolver:
            case AccionIncidente.Cerrar:
                return asignadoAMi || actor.EsAdmin ? null : "Solo la persona asignada (o el admin) puede hacer esto.";

            case AccionIncidente.Escalar:
                if (inc.Nivel == NivelEscalamiento.N3) return "El incidente ya está en N3, el nivel más alto.";
                if (actor.EsAdmin) return null;
                if (!actor.EsTecnico) return "Solo los técnicos escalan funcionalmente (el gestor reasigna).";
                return actor.Nivel >= (int)inc.Nivel ? null : $"El incidente está en {inc.Nivel}; solo ese nivel o superior puede escalarlo.";

            case AccionIncidente.CambiarPrioridad:
                if (prioridadNueva is null || prioridadNueva == inc.Prioridad) return "Elige una prioridad distinta a la actual.";
                if (prioridadNueva == Prioridad.P1 || inc.Prioridad == Prioridad.P1)
                    return actor.EsGestor || actor.EsAdmin ? null : "Subir o bajar de P1 lo autoriza solo el gestor.";
                return actor.Nivel >= 2 || actor.EsGestor ? null : "Cambiar la prioridad requiere ser N2, N3, gestor o admin.";

            case AccionIncidente.Reabrir:
                if (!actor.EsTecnico && !actor.EsGestor) return "Solo técnicos, gestor o admin pueden reabrir.";
                return inc.Resuelto is { } r && ahora - r > PlazoReapertura ? "Pasaron más de 7 días desde la resolución: registra un incidente nuevo." : null;

            default:
                return null;
        }
    }

    /// <summary>Siguiente nivel de escalamiento funcional.</summary>
    public static NivelEscalamiento Siguiente(NivelEscalamiento n) => n == NivelEscalamiento.N1 ? NivelEscalamiento.N2 : NivelEscalamiento.N3;

    public static string Nombre(EstadoIncidente e) => e switch
    {
        EstadoIncidente.Nuevo => "Nuevo",
        EstadoIncidente.Asignado => "Asignado",
        EstadoIncidente.EnProgreso => "En progreso",
        EstadoIncidente.EnEspera => "En espera",
        EstadoIncidente.Resuelto => "Resuelto",
        EstadoIncidente.Cerrado => "Cerrado",
        _ => "Reabierto",
    };

    private static string Verbo(AccionIncidente a) => a switch
    {
        AccionIncidente.Tomar => "tomar",
        AccionIncidente.Asignar => "asignar",
        AccionIncidente.Iniciar => "iniciar",
        AccionIncidente.Pausar => "pausar",
        AccionIncidente.Reanudar => "reanudar",
        AccionIncidente.Escalar => "escalar",
        AccionIncidente.CambiarPrioridad => "cambiar la prioridad de",
        AccionIncidente.Resolver => "resolver",
        AccionIncidente.Cerrar => "cerrar",
        AccionIncidente.Reabrir => "reabrir",
        _ => "comentar",
    };
}

/// <summary>Marcas de tiempo del incidente que determinan su SLA.</summary>
public sealed record TiemposIncidente(
    DateTimeOffset InicioSla,
    Prioridad Prioridad,
    DateTimeOffset? PrimeraRespuesta,
    DateTimeOffset? Resuelto,
    IReadOnlyList<PeriodoEspera> Esperas);

public sealed record SlaIncidente(EstadoSla Respuesta, EstadoSla Resolucion, bool RespuestaCumplida, bool ResolucionCumplida);

/// <summary>
/// SLA de un incidente (sección 11): respuesta = hasta que alguien lo toma; resolución = hasta resolverlo,
/// descontando el tiempo En espera. Lo comparten la API y el agente de Windows (vigilancia de umbrales).
/// </summary>
public static class SlaDeIncidente
{
    public static SlaIncidente Calcular(TiemposIncidente t, DateTimeOffset ahora, IReadOnlyDictionary<Prioridad, ObjetivoSla>? objetivos = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        var o = (objetivos ?? ObjetivosSla.PorDefecto)[t.Prioridad];
        var finRespuesta = t.PrimeraRespuesta ?? ahora;
        var finResolucion = t.Resuelto ?? ahora;
        var respuesta = CalculadoraSla.Calcular(t.InicioSla, o.Respuesta, [], finRespuesta < t.InicioSla ? t.InicioSla : finRespuesta);
        var resolucion = CalculadoraSla.Calcular(t.InicioSla, o.Resolucion, t.Esperas, finResolucion < t.InicioSla ? t.InicioSla : finResolucion);
        return new SlaIncidente(respuesta, resolucion, t.PrimeraRespuesta is not null && respuesta.Porcentaje < 100, t.Resuelto is not null && resolucion.Porcentaje < 100);
    }

    /// <summary>
    /// Avisos jerárquicos pendientes ("respuesta-75", "resolucion-100"…) que aún no se registraron como enviados.
    /// Solo mientras el incidente sigue abierto; el de respuesta solo si nadie lo ha tomado.
    /// </summary>
    public static IReadOnlyList<string> AvisosPendientes(SlaIncidente sla, bool abierto, bool respondido, IEnumerable<string> enviados)
    {
        ArgumentNullException.ThrowIfNull(sla);
        if (!abierto) return [];
        var ya = enviados.ToHashSet(StringComparer.Ordinal);
        var lista = new List<string>();
        if (!respondido) Agregar(lista, "respuesta", sla.Respuesta.Porcentaje, ya);
        Agregar(lista, "resolucion", sla.Resolucion.Porcentaje, ya);
        return lista;
    }

    private static void Agregar(List<string> lista, string tipo, double porcentaje, HashSet<string> ya)
    {
        // Si se cruzó el 100 % sin haber avisado el 75 %, basta con el aviso del 100 %.
        if (porcentaje >= 100) { if (!ya.Contains($"{tipo}-100")) lista.Add($"{tipo}-100"); }
        else if (porcentaje >= 75 && !ya.Contains($"{tipo}-75")) lista.Add($"{tipo}-75");
    }
}

/// <summary>Prioridad sugerida para un incidente creado desde una excepción de un host (impacto medio).</summary>
public static class PrioridadSugerida
{
    /// <summary>Urgencia alta si el recurso puede dejar el equipo fuera de servicio (disco, batería, temperatura, red).</summary>
    public static Prioridad ParaMetrica(string metrica)
    {
        ArgumentNullException.ThrowIfNull(metrica);
        var critica = metrica.StartsWith("disco.", StringComparison.Ordinal) || metrica.StartsWith("almacenamiento.", StringComparison.Ordinal)
            || metrica.StartsWith("bateria.", StringComparison.Ordinal) || metrica.StartsWith("termico.", StringComparison.Ordinal)
            || metrica.StartsWith("red.", StringComparison.Ordinal);
        return MatrizPrioridad.Calcular(Impacto.Medio, critica ? Urgencia.Alta : Urgencia.Media);
    }
}
