using System.Net;
using System.Text.Json;
using SysPulse.Agent.Core.Agregacion;
using SysPulse.Agent.Core.Collectors;
using SysPulse.Agent.Core.Envio;

namespace SysPulse.Agent.Core.Tests;

public class PublicadorIngestaTests
{
    /// <summary>Manejador falso: guarda las peticiones y responde lo indicado.</summary>
    private sealed class Falso(HttpStatusCode estado, string? cuerpo = null) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Ruta, string? Clave, string Cuerpo)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var contenido = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Peticiones.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.GetValues("X-Agent-Key").Single(), contenido));
            return new HttpResponseMessage(estado) { Content = cuerpo is null ? null : new StringContent(cuerpo) };
        }
    }

    [Fact]
    public async Task Estado_VaConLaClaveYConClavesDeCampoSinPuntos()
    {
        var falso = new Falso(HttpStatusCode.NoContent);
        using var p = new PublicadorIngesta("https://ejemplo.test", "clave-agente", falso);

        await p.ActualizarEstadoAsync("and-0123456789", "android",
            [new Lectura("red.wifi_bajada_mb", 12.5, "MB", DateTimeOffset.UtcNow), new Lectura("bateria.pct", double.NaN, "%", DateTimeOffset.UtcNow)],
            CancellationToken.None);

        var (metodo, ruta, clave, cuerpo) = falso.Peticiones.Single();
        Assert.Equal(HttpMethod.Put, metodo);
        Assert.Equal("/api/agente/estado", ruta);
        Assert.Equal("clave-agente", clave);
        using var json = JsonDocument.Parse(cuerpo);
        var lecturas = json.RootElement.GetProperty("lecturas");
        Assert.Equal(12.5, lecturas.GetProperty("red_wifi_bajada_mb").GetDouble());
        Assert.False(lecturas.TryGetProperty("bateria_pct", out _)); // NaN no se envía
    }

    [Fact]
    public async Task Metricas_UsanElMismoFormatoQueAtlas()
    {
        var falso = new Falso(HttpStatusCode.NoContent);
        using var p = new PublicadorIngesta("https://ejemplo.test/", "k", falso);
        var doc = new DocumentoAgregado("and-0123456789", "android", new DateTimeOffset(2026, 10, 28, 10, 5, 0, TimeSpan.Zero),
            new Dictionary<string, Resumen> { ["red.movil_bajada_mbs"] = new(0.1, 0.2, 0.3, 0.29, 5, "MB/s") });

        await p.PublicarAgregadosAsync([JsonSerializer.Serialize(doc)], CancellationToken.None);

        using var json = JsonDocument.Parse(falso.Peticiones.Single().Cuerpo);
        var d = json.RootElement[0];
        Assert.Equal("and-0123456789", d.GetProperty("host").GetString());
        Assert.Equal("android", d.GetProperty("tipo_host").GetString());
        Assert.Equal(new DateTime(2026, 10, 28, 10, 5, 0, DateTimeKind.Utc), d.GetProperty("ts").GetDateTime().ToUniversalTime());
        var m = d.GetProperty("metricas").GetProperty("red_movil_bajada_mbs");
        Assert.Equal(0.29, m.GetProperty("p95").GetDouble());
        Assert.Equal(5, m.GetProperty("n").GetInt32());
        Assert.Equal("MB/s", m.GetProperty("u").GetString());
    }

    [Fact]
    public async Task Configuracion_InterpretaElJsonExtendidoDeLaRuta()
    {
        const string ejson = """
            {"_id":"agentes","persistencia_minutos":8,"actualizado":{"$date":"2026-10-15T20:08:26.000Z"},
             "actualizacion_android":{"version":"0.7.0","url":"https://github.com/x/y/releases/download/v/a.apk","sha256":"ab","codigo":7},
             "umbrales":[]}
            """;
        using var p = new PublicadorIngesta("https://ejemplo.test", "k", new Falso(HttpStatusCode.OK, ejson));

        var config = await p.LeerConfiguracionAsync(CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(7, config!.ActualizacionAndroid!.Codigo);
        Assert.Equal(8, config.PersistenciaMinutos);
    }

    [Fact]
    public async Task ErrorDelServidor_LanzaExcepcion_ParaQueElMotorReintente()
    {
        using var p = new PublicadorIngesta("https://ejemplo.test", "k", new Falso(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() => p.RegistrarHostAsync("and-0123456789", "android", "0.7.0", CancellationToken.None));
    }
}
