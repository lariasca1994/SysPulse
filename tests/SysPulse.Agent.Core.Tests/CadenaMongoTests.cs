using SysPulse.Agent.Core.Configuracion;

namespace SysPulse.Agent.Core.Tests;

public class CadenaMongoTests
{
    private static readonly string[] Hosts =
    [
        "ac-x-shard-00-00.abc.mongodb.net:27017",
        "ac-x-shard-00-01.abc.mongodb.net:27017",
        "ac-x-shard-00-02.abc.mongodb.net:27017",
    ];

    [Fact]
    public void Convertir_UsaHostsDelSrvYOpcionesDelTxtYTls()
    {
        var resultado = CadenaMongo.ConvertirSrvAEstandar(
            "mongodb+srv://usuario:cl%40ve@cluster1.abc.mongodb.net/syspulse?appName=Cluster1",
            Hosts,
            "authSource=admin&replicaSet=atlas-x-shard-0");

        Assert.Equal(
            "mongodb://usuario:cl%40ve@ac-x-shard-00-00.abc.mongodb.net:27017,ac-x-shard-00-01.abc.mongodb.net:27017,ac-x-shard-00-02.abc.mongodb.net:27017/syspulse?appName=Cluster1&authSource=admin&replicaSet=atlas-x-shard-0&tls=true",
            resultado);
    }

    [Fact]
    public void Convertir_LasOpcionesDeLaCadenaTienenPrioridadSobreElTxt()
    {
        var resultado = CadenaMongo.ConvertirSrvAEstandar(
            "mongodb+srv://u:p@c.abc.mongodb.net/db?authSource=otra&tls=false",
            Hosts,
            "authSource=admin");

        Assert.EndsWith("/db?authSource=otra&tls=false", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Convertir_SinBaseNiParametros_AgregaRaizYTls()
    {
        var resultado = CadenaMongo.ConvertirSrvAEstandar("mongodb+srv://u:p@c.abc.mongodb.net", Hosts, null);

        Assert.EndsWith(":27017/?tls=true", resultado, StringComparison.Ordinal);
    }

    [Fact]
    public void Convertir_RechazaCadenaQueNoEsSrv()
    {
        Assert.Throws<ArgumentException>(() => CadenaMongo.ConvertirSrvAEstandar("mongodb://u:p@h:27017/db", Hosts, null));
    }

    [Fact]
    public void Convertir_RechazaSrvSinHosts()
    {
        Assert.Throws<ArgumentException>(() => CadenaMongo.ConvertirSrvAEstandar("mongodb+srv://u:p@c.abc.mongodb.net/db", [], null));
    }

    [Theory]
    [InlineData("mongodb+srv://u:p@c.abc.mongodb.net/db", true, false)]
    [InlineData("mongodb+srv://u:p@c.abc.mongodb.net/db", false, true)]
    [InlineData("mongodb://u:p@h1:27017,h2:27017/db?tls=true", true, true)]
    [InlineData("mongodb://u:p@h1:27017/db", false, true)]
    [InlineData("postgres://u:p@h/db", false, false)]
    [InlineData("", true, false)]
    [InlineData(null, false, false)]
    public void Validar_SrvNoSirveEnAndroid(string? cadena, bool esAndroid, bool esValida)
    {
        var error = CadenaMongo.Validar(cadena, esAndroid);

        Assert.Equal(esValida, error is null);
    }
}
