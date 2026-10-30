using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SysPulse.AgentInstaller;

/// <summary>
/// Servicio de Windows "SysPulse Actualizador" (LocalSystem): cada hora ejecuta la actualización automática.
/// Reemplaza a la tarea programada: algunos antivirus (p. ej. el módulo de comportamiento de Kaspersky) tratan
/// como troyano a un programa sin firma que crea tareas programadas como SYSTEM y revierten toda la instalación.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ServicioActualizador(IHostApplicationLifetime vida) : BackgroundService
{
    public const string Nombre = "SysPulseActualizador";
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(5);
    // Actualización forzada: una versión publicada llega a todos los equipos encendidos en menos de 15 minutos.
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    public static async Task<int> EjecutarAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(o => o.ServiceName = Nombre);
        builder.Services.AddHostedService<ServicioActualizador>();
        await builder.Build().RunAsync().ConfigureAwait(false);
        return Environment.ExitCode;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Equipos instalados antes de la marca: el reinicio automático también aplica cuando el servicio se detiene
        // con código de error (el que usa al reemplazarse a sí mismo).
        Instalacion.ActivarReinicioActualizador();

        // Espera inicial: no compite con el arranque del equipo ni del agente.
        await Task.Delay(EsperaInicial, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Actualizador.EjecutarAsync(soloComprobar: false).ConfigureAwait(false);

            // Si la actualización reemplazó a este mismo ejecutable (queda el anterior como .old), se reinicia
            // con código de error para que Windows lo vuelva a iniciar ya con la versión nueva.
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old"))
            {
                // Proceso aparte con el ejecutable nuevo: espera a que este se detenga y lo vuelve a iniciar.
                // Antes dependía solo de la recuperación de Windows, que no actúa ante una detención ordenada.
                try
                {
                    Instalacion.ProgramarReinicioActualizador(exe);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Queda la recuperación de Windows (failureflag) como respaldo.
                }
                Environment.ExitCode = 1;
                vida.StopApplication();
                return;
            }
            await Task.Delay(Intervalo, stoppingToken).ConfigureAwait(false);
        }
    }
}
