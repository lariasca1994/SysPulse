namespace SysPulse.Agent.Core.Configuracion;

/// <summary>
/// Reglas de la cadena de conexión a Atlas para los agentes.
/// En Android, MongoDB.Driver no puede resolver registros SRV ("List of configured name servers must not be empty"),
/// así que el agente Android usa siempre la forma estándar mongodb://host1,host2,host3/... (validado en un celular Android).
/// </summary>
public static class CadenaMongo
{
    public const string PrefijoSrv = "mongodb+srv://";
    public const string PrefijoEstandar = "mongodb://";

    /// <summary>Devuelve un mensaje de error si la cadena no sirve para el tipo de host, o null si es válida.</summary>
    public static string? Validar(string? cadena, bool esAndroid)
    {
        if (string.IsNullOrWhiteSpace(cadena)) return "La cadena de conexión está vacía.";

        var esSrv = cadena.StartsWith(PrefijoSrv, StringComparison.Ordinal);
        var esEstandar = cadena.StartsWith(PrefijoEstandar, StringComparison.Ordinal);
        if (!esSrv && !esEstandar) return "La cadena debe empezar por mongodb:// o mongodb+srv://.";

        if (esAndroid && esSrv)
        {
            return "En Android use la cadena estándar mongodb://… (genérela en el PC con: SysPulse.AgentInstaller mongo-estandar).";
        }

        return null;
    }

    /// <summary>
    /// Convierte una cadena mongodb+srv:// a mongodb:// con los hosts del registro SRV y las opciones del registro TXT.
    /// Conserva usuario, contraseña (tal como venían codificados), base y parámetros propios de la cadena original.
    /// </summary>
    /// <param name="hosts">Destinos del registro SRV en formato host:puerto.</param>
    /// <param name="opcionesTxt">Contenido del registro TXT (ej. "authSource=admin&amp;replicaSet=atlas-xyz-shard-0").</param>
    public static string ConvertirSrvAEstandar(string cadenaSrv, IReadOnlyList<string> hosts, string? opcionesTxt)
    {
        ArgumentNullException.ThrowIfNull(cadenaSrv);
        ArgumentNullException.ThrowIfNull(hosts);
        if (!cadenaSrv.StartsWith(PrefijoSrv, StringComparison.Ordinal))
        {
            throw new ArgumentException("La cadena no es mongodb+srv://.", nameof(cadenaSrv));
        }
        if (hosts.Count == 0) throw new ArgumentException("El registro SRV no devolvió hosts.", nameof(hosts));

        var resto = cadenaSrv[PrefijoSrv.Length..];
        var arroba = resto.LastIndexOf('@');
        var credenciales = arroba >= 0 ? resto[..(arroba + 1)] : "";
        var trasHost = arroba >= 0 ? resto[(arroba + 1)..] : resto;

        var finHost = trasHost.IndexOfAny(['/', '?']);
        var rutaYConsulta = finHost >= 0 ? trasHost[finHost..] : "";
        var interrogacion = rutaYConsulta.IndexOf('?', StringComparison.Ordinal);
        var ruta = interrogacion >= 0 ? rutaYConsulta[..interrogacion] : rutaYConsulta;
        var consulta = interrogacion >= 0 ? rutaYConsulta[(interrogacion + 1)..] : "";

        // Opciones: primero las de la cadena (tienen prioridad), luego las del TXT y TLS (implícito en +srv).
        var opciones = new List<KeyValuePair<string, string>>();
        void Agregar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return;
            foreach (var par in texto.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var igual = par.IndexOf('=', StringComparison.Ordinal);
                var clave = igual >= 0 ? par[..igual] : par;
                var valor = igual >= 0 ? par[(igual + 1)..] : "";
                if (!opciones.Exists(o => o.Key.Equals(clave, StringComparison.OrdinalIgnoreCase)))
                {
                    opciones.Add(new(clave, valor));
                }
            }
        }
        Agregar(consulta);
        Agregar(opcionesTxt);
        Agregar("tls=true");

        var consultaFinal = string.Join('&', opciones.Select(o => $"{o.Key}={o.Value}"));
        var rutaFinal = string.IsNullOrEmpty(ruta) ? "/" : ruta;
        return $"{PrefijoEstandar}{credenciales}{string.Join(',', hosts)}{rutaFinal}?{consultaFinal}";
    }
}
