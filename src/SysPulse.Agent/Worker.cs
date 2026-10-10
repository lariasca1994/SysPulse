using System.Reflection;
using SysPulse.Agent.Collectors.Windows;
using SysPulse.Agent.Core;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent;

/// <summary>Servicio de Windows: ejecuta el motor común con los colectores de Windows y un umbral por unidad de disco.</summary>
public sealed class Worker(MotorAgente motor) : BackgroundService
{
    private static readonly string Version =
        typeof(Worker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var umbralesDisco = DiscoCollector.Unidades().Select(u => UmbralesPorDefecto.Disco($"disco.{u}.pct"));
        return motor.EjecutarAsync(Version, umbralesDisco, stoppingToken);
    }
}
