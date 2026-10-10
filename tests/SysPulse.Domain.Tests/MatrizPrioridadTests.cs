using SysPulse.Domain;

namespace SysPulse.Domain.Tests;

public class MatrizPrioridadTests
{
    [Theory]
    [InlineData(Impacto.Alto, Urgencia.Alta, Prioridad.P1)]
    [InlineData(Impacto.Alto, Urgencia.Media, Prioridad.P2)]
    [InlineData(Impacto.Alto, Urgencia.Baja, Prioridad.P3)]
    [InlineData(Impacto.Medio, Urgencia.Alta, Prioridad.P2)]
    [InlineData(Impacto.Medio, Urgencia.Media, Prioridad.P3)]
    [InlineData(Impacto.Medio, Urgencia.Baja, Prioridad.P4)]
    [InlineData(Impacto.Bajo, Urgencia.Alta, Prioridad.P3)]
    [InlineData(Impacto.Bajo, Urgencia.Media, Prioridad.P4)]
    [InlineData(Impacto.Bajo, Urgencia.Baja, Prioridad.P5)]
    public void Calcular_SigueLaMatrizDeLaEspecificacion(Impacto impacto, Urgencia urgencia, Prioridad esperada)
    {
        Assert.Equal(esperada, MatrizPrioridad.Calcular(impacto, urgencia));
    }

    [Fact]
    public void Registro_SoloPermiteElegirNiveles()
    {
        Assert.Equal(["N1", "N2", "N3"], Roles.SeleccionablesEnRegistro);
        Assert.DoesNotContain(Roles.Gestor, Roles.SeleccionablesEnRegistro);
        Assert.DoesNotContain(Roles.Admin, Roles.SeleccionablesEnRegistro);
    }
}
