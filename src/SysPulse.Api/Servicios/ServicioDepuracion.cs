using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Domain;
using SysPulse.Infrastructure.Persistencia;

namespace SysPulse.Api.Servicios;

/// <summary>
/// Regla obligatoria del portafolio: todo registro con fecha se depura al cumplir la retención
/// (<see cref="ContextoMongo.Retencion"/>). Lo simple lo borra Atlas con índices TTL (ver ContextoMongo); aquí va lo
/// que depende de otros registros: los incidentes terminados con su sala, sus mensajes y sus lecturas, los mensajes
/// del canal general y las solicitudes de rol ya resueltas. Los incidentes abiertos nunca se tocan.
/// Corre al arrancar la API y cada pocas horas mientras está despierta (sin datos nuevos no hay nada que depurar).
/// </summary>
public sealed partial class ServicioDepuracion(ContextoMongo mongo, TimeProvider reloj, ILogger<ServicioDepuracion> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);
    private static readonly string[] Terminados = [nameof(EstadoIncidente.Resuelto), nameof(EstadoIncidente.Cerrado)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, reloj, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Intervalo, reloj);
            do
            {
                try
                {
                    await DepurarAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFallo(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // La API se detiene.
        }
    }

    public async Task DepurarAsync(CancellationToken ct)
    {
        var limite = reloj.GetUtcNow().UtcDateTime - ContextoMongo.Retencion;

        // Incidentes terminados: la reapertura solo se permite dentro de un plazo menor a la retención.
        var ids = await mongo.Incidentes.Find(i => Terminados.Contains(i.Estado) && i.Resuelto != null && i.Resuelto < limite)
            .Project(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        var salas = ids.Select(ServicioChat.SalaDeIncidente).ToList();
        long mensajes = 0;
        if (ids.Count > 0)
        {
            mensajes += (await mongo.Mensajes.DeleteManyAsync(m => salas.Contains(m.SalaId), ct).ConfigureAwait(false)).DeletedCount;
            await mongo.Lecturas.DeleteManyAsync(l => salas.Contains(l.SalaId), ct).ConfigureAwait(false);
            await mongo.Salas.DeleteManyAsync(s => salas.Contains(s.Id), ct).ConfigureAwait(false);
            await mongo.Incidentes.DeleteManyAsync(i => ids.Contains(i.Id), ct).ConfigureAwait(false);
        }

        // Canal general: mensajes que superan la retención.
        mensajes += (await mongo.Mensajes.DeleteManyAsync(m => m.SalaId == ServicioChat.Operaciones && m.Ts < limite, ct).ConfigureAwait(false)).DeletedCount;

        // Solicitudes de rol resueltas (las pendientes esperan al administrador).
        var solicitudes = (await mongo.SolicitudesRol.DeleteManyAsync(s => s.Estado != "pendiente" && s.Resuelto != null && s.Resuelto < limite, ct)
            .ConfigureAwait(false)).DeletedCount;

        if (ids.Count > 0 || mensajes > 0 || solicitudes > 0) LogDepurado(ids.Count, mensajes, solicitudes);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Depuración: {Incidentes} incidentes, {Mensajes} mensajes y {Solicitudes} solicitudes de rol")]
    private partial void LogDepurado(int incidentes, long mensajes, long solicitudes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La depuración no se completó; se reintenta en el próximo ciclo")]
    private partial void LogFallo(Exception ex);
}
