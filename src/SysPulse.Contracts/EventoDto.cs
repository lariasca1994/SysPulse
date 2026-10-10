namespace SysPulse.Contracts;

/// <summary>Evento que un agente envía a <c>POST /api/eventos</c> cuando una alerta se abre, escala o cierra.</summary>
/// <param name="Tipo">"abierto", "escalado" o "cerrado".</param>
/// <param name="Severidad">"informativo", "advertencia" o "excepcion".</param>
/// <param name="ClaveDedup">host + tipo + métrica: un evento abierto no genera otro igual.</param>
/// <param name="RespaldoEnviado">El agente ya avisó a las cuentas fijas por Telegram porque la API no respondió a tiempo.</param>
public sealed record EventoDto(
    string Host,
    string TipoHost,
    string Tipo,
    string Severidad,
    string Metrica,
    double Valor,
    double Umbral,
    string ClaveDedup,
    DateTimeOffset Ts,
    bool RespaldoEnviado = false);

/// <summary>Cruce de umbral de SLA detectado por el agente de Windows (<c>POST /api/sla/umbral</c>).</summary>
public sealed record CruceSlaDto(string IncidenteId, string Marca);
