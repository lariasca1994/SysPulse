using SysPulse.Domain;

namespace SysPulse.Domain.Tests;

public class ReglasIncidenteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 9, 20, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Actor N1 = new("u1", Roles.N1);
    private static readonly Actor N2 = new("u2", Roles.N2);
    private static readonly Actor N3 = new("u3", Roles.N3);
    private static readonly Actor Gestor = new("g", Roles.Gestor);
    private static readonly Actor Admin = new("a", Roles.Admin);

    private static SituacionIncidente Inc(EstadoIncidente e = EstadoIncidente.Nuevo, NivelEscalamiento n = NivelEscalamiento.N1,
        Prioridad p = Prioridad.P3, string? asignado = null, DateTimeOffset? resuelto = null) => new(e, n, p, asignado, resuelto);

    [Fact]
    public void N1_TomaIncidenteNuevoDeSuNivel()
    {
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Tomar, Inc(), N1, Ahora));
        Assert.Equal(EstadoIncidente.Asignado, ReglasIncidente.Destino(AccionIncidente.Tomar, EstadoIncidente.Nuevo));
    }

    [Fact]
    public void N1_NoTomaIncidenteEscaladoAN2()
    {
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Tomar, Inc(n: NivelEscalamiento.N2), N1, Ahora));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Tomar, Inc(n: NivelEscalamiento.N2), N3, Ahora));
    }

    [Fact]
    public void Gestor_NoTomaNiResuelve_PeroReasigna()
    {
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Tomar, Inc(), Gestor, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Resolver, Inc(EstadoIncidente.EnProgreso, asignado: "u1"), Gestor, Ahora));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Asignar, Inc(), Gestor, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Asignar, Inc(), N3, Ahora));
    }

    [Fact]
    public void SoloElAsignadoOAdmin_ResuelveYPausa()
    {
        var inc = Inc(EstadoIncidente.EnProgreso, asignado: "u1");
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Resolver, inc, N1, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Resolver, inc, N2, Ahora));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Pausar, inc, Admin, Ahora));
    }

    [Theory]
    [InlineData(EstadoIncidente.Nuevo, AccionIncidente.Pausar)]
    [InlineData(EstadoIncidente.Cerrado, AccionIncidente.Escalar)]
    [InlineData(EstadoIncidente.EnEspera, AccionIncidente.Resolver)]
    [InlineData(EstadoIncidente.EnProgreso, AccionIncidente.Cerrar)]
    public void TransicionInvalida_LaRechazaElDominio(EstadoIncidente estado, AccionIncidente accion)
    {
        Assert.NotNull(ReglasIncidente.Validar(accion, Inc(estado, asignado: "a"), Admin, Ahora));
    }

    [Fact]
    public void Escalamiento_N1aN2_N2aN3_N3NoSube()
    {
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Escalar, Inc(), N1, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Escalar, Inc(n: NivelEscalamiento.N2), N1, Ahora));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Escalar, Inc(n: NivelEscalamiento.N2), N2, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Escalar, Inc(n: NivelEscalamiento.N3), Admin, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Escalar, Inc(), Gestor, Ahora));
        Assert.Equal(NivelEscalamiento.N3, ReglasIncidente.Siguiente(NivelEscalamiento.N2));
        Assert.Equal(EstadoIncidente.Nuevo, ReglasIncidente.Destino(AccionIncidente.Escalar, EstadoIncidente.EnProgreso));
    }

    [Fact]
    public void Prioridad_N1NoCambia_N2EntreP2yP5_P1SoloGestor()
    {
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(), N1, Ahora, Prioridad.P2));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(), N2, Ahora, Prioridad.P2));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(), N3, Ahora, Prioridad.P1));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(p: Prioridad.P1), N3, Ahora, Prioridad.P2));
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(), Gestor, Ahora, Prioridad.P1));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.CambiarPrioridad, Inc(), Admin, Ahora, Prioridad.P3));
    }

    [Fact]
    public void Reabrir_SoloDentroDe7Dias()
    {
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Reabrir, Inc(EstadoIncidente.Resuelto, resuelto: Ahora.AddDays(-6)), N1, Ahora));
        Assert.NotNull(ReglasIncidente.Validar(AccionIncidente.Reabrir, Inc(EstadoIncidente.Resuelto, resuelto: Ahora.AddDays(-8)), N1, Ahora));
    }

    [Fact]
    public void Comentar_SiemprePermitido()
    {
        Assert.Null(ReglasIncidente.Validar(AccionIncidente.Comentar, Inc(EstadoIncidente.Cerrado), Gestor, Ahora));
    }
}

public class SlaDeIncidenteTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void P2_SinTomarA25Min_AvisaRespuesta75()
    {
        var sla = SlaDeIncidente.Calcular(new TiemposIncidente(Inicio, Prioridad.P2, null, null, []), Inicio.AddMinutes(25));

        Assert.True(sla.Respuesta.Porcentaje is > 75 and < 100);
        Assert.Equal(["respuesta-75"], SlaDeIncidente.AvisosPendientes(sla, abierto: true, respondido: false, []));
    }

    [Fact]
    public void RespuestaCongeladaAlTomar()
    {
        var t = new TiemposIncidente(Inicio, Prioridad.P2, Inicio.AddMinutes(10), null, []);
        var sla = SlaDeIncidente.Calcular(t, Inicio.AddHours(3));

        Assert.True(sla.RespuestaCumplida);
        Assert.Equal(TimeSpan.FromMinutes(10), sla.Respuesta.Consumido);
        Assert.Empty(SlaDeIncidente.AvisosPendientes(sla, abierto: true, respondido: true, []));
    }

    [Fact]
    public void EsperaPausaLaResolucion()
    {
        var esperas = new[] { new PeriodoEspera(Inicio.AddHours(1), Inicio.AddHours(5)) };
        var sla = SlaDeIncidente.Calcular(new TiemposIncidente(Inicio, Prioridad.P2, Inicio.AddMinutes(5), null, esperas), Inicio.AddHours(8));

        Assert.Equal(TimeSpan.FromHours(4), sla.Resolucion.Consumido);
        Assert.Equal(50, sla.Resolucion.Porcentaje, precision: 6);
    }

    [Fact]
    public void Vencido_Sin75Previo_SoloAvisa100_YNoRepite()
    {
        var sla = SlaDeIncidente.Calcular(new TiemposIncidente(Inicio, Prioridad.P1, null, null, []), Inicio.AddMinutes(20));

        Assert.Equal(["respuesta-100"], SlaDeIncidente.AvisosPendientes(sla, true, false, []));
        Assert.Empty(SlaDeIncidente.AvisosPendientes(sla, true, false, ["respuesta-100"]));
        Assert.Empty(SlaDeIncidente.AvisosPendientes(sla, abierto: false, respondido: false, []));
    }

    [Theory]
    [InlineData("disco.C.pct", Prioridad.P2)]
    [InlineData("bateria.temp_c", Prioridad.P2)]
    [InlineData("cpu.total", Prioridad.P3)]
    [InlineData("ram.pct", Prioridad.P3)]
    public void PrioridadSugeridaPorMetrica(string metrica, Prioridad esperada)
    {
        Assert.Equal(esperada, PrioridadSugerida.ParaMetrica(metrica));
    }
}
