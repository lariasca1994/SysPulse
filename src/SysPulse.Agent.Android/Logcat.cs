using Microsoft.Extensions.Logging;
using AndroidLog = global::Android.Util.Log;

namespace SysPulse.Agent.Android;

/// <summary>Envía el registro del motor a logcat con la etiqueta "SysPulse" (adb logcat -s SysPulse).</summary>
public sealed class Logcat : ILoggerProvider
{
    public const string Etiqueta = "SysPulse";

    public static ILoggerFactory CrearFactory() => LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new Logcat()));

    public ILogger CreateLogger(string categoryName) => new Registro();

    public void Dispose()
    {
    }

    private sealed class Registro : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var mensaje = formatter(state, exception) + (exception is null ? "" : $" | {exception.GetType().Name}: {exception.Message}");
            switch (logLevel)
            {
                case LogLevel.Warning:
                    AndroidLog.Warn(Etiqueta, mensaje);
                    break;
                case LogLevel.Error or LogLevel.Critical:
                    AndroidLog.Error(Etiqueta, mensaje);
                    break;
                default:
                    AndroidLog.Info(Etiqueta, mensaje);
                    break;
            }
        }
    }
}
