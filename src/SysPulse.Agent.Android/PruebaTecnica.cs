using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Driver;
using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Android;

/// <summary>
/// Prueba técnica (sección 5): DNS, TCP, ping y conexión a Atlas con la cadena estándar.
/// La cadena llega una sola vez en un archivo temporal (adb push), se valida, se guarda cifrada
/// en SecureStorage y el archivo se borra. Las ejecuciones siguientes usan la cadena guardada.
/// Solo lee (comando ping); no escribe en la base.
/// </summary>
public static class PruebaTecnica
{
    private const string ClaveCadena = "MONGO_URI_ANDROID";

    public sealed record Resultado(string Prueba, bool Ok, string Detalle);

    public static async Task<IReadOnlyList<Resultado>> EjecutarAsync()
    {
        var resultados = new List<Resultado>();
        var cadena = await ObtenerCadenaAsync(resultados);
        var host = cadena is null ? null : MongoUrl.Create(cadena).Servers.First().Host;

        if (host is not null)
        {
            resultados.Add(await MedirAsync("DNS (servidor de Atlas)", async () =>
            {
                var ips = await Dns.GetHostAddressesAsync(host);
                return $"{ips.Length} IP";
            }));

            resultados.Add(await MedirAsync("TCP 27017 (Atlas)", async () =>
            {
                using var cliente = new TcpClient();
                await cliente.ConnectAsync(host, 27017).WaitAsync(TimeSpan.FromSeconds(10));
                return "conectado";
            }));
        }

        resultados.Add(await MedirAsync("TCP 443 (1.1.1.1)", async () =>
        {
            using var cliente = new TcpClient();
            await cliente.ConnectAsync(IPAddress.Parse("1.1.1.1"), 443).WaitAsync(TimeSpan.FromSeconds(10));
            return "conectado";
        }));

        resultados.Add(await PingAsync("8.8.8.8"));

        if (cadena is not null)
        {
            resultados.Add(await MedirAsync("MongoDB (Atlas)", () => PingMongoAsync(cadena)));
        }

        return resultados;
    }

    private static async Task<string?> ObtenerCadenaAsync(List<Resultado> resultados)
    {
        var importacion = await ConfiguracionAndroid.ImportarSiExisteAsync();
        if (importacion is not null) resultados.Add(new("Importación", !importacion.StartsWith("Configuración rechazada", StringComparison.Ordinal), importacion));

        var guardada = await SecureStorage.Default.GetAsync(ClaveCadena);
        if (guardada is null)
        {
            resultados.Add(new("Configuración", false, "falta configurar la conexión a Atlas"));
            return null;
        }

        resultados.Add(new("Configuración", true, "usando la cadena guardada"));
        return guardada;
    }

    private static async Task<string> PingMongoAsync(string cadena)
    {
        var ajustes = MongoClientSettings.FromConnectionString(cadena);
        ajustes.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
        ajustes.ConnectTimeout = TimeSpan.FromSeconds(15);
        using var cliente = new MongoClient(ajustes);
        var db = cliente.GetDatabase(MongoUrl.Create(cadena).DatabaseName ?? "admin");
        await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        return "ping ok";
    }

    private static async Task<Resultado> PingAsync(string destino)
    {
        var nombre = $"Ping ICMP ({destino}, 5 paquetes)";
        try
        {
            using var ping = new Ping();
            var tiempos = new List<long>();
            for (var i = 0; i < 5; i++)
            {
                var r = await ping.SendPingAsync(destino, 3000);
                if (r.Status == IPStatus.Success) tiempos.Add(r.RoundtripTime);
            }
            var perdida = (5 - tiempos.Count) * 20;
            return tiempos.Count == 0
                ? new(nombre, false, "100 % de pérdida")
                : new(nombre, true, $"promedio {tiempos.Average():0} ms, pérdida {perdida} %");
        }
        catch (Exception ex)
        {
            return new(nombre, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<Resultado> MedirAsync(string nombre, Func<Task<string>> accion)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            var detalle = await accion();
            return new(nombre, true, $"{detalle} ({reloj.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            var interna = ex.InnerException is null ? "" : $" → {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            return new(nombre, false, $"{ex.GetType().Name}: {ex.Message}{interna}");
        }
    }
}
