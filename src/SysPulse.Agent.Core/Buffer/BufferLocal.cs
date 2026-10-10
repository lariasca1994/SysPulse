using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SysPulse.Agent.Core.Collectors;

namespace SysPulse.Agent.Core.Buffer;

/// <summary>
/// Almacén local SQLite (store-and-forward, sección 3): lecturas crudas 48 h, agregados de 5 min pendientes
/// de enviar a Atlas y eventos pendientes de entregar a la API. Si no hay internet, nada se pierde.
/// </summary>
public sealed class BufferLocal : IDisposable
{
    private readonly SqliteConnection _conexion;
    private readonly Lock _bloqueo = new();

    public BufferLocal(string rutaArchivo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(rutaArchivo)!);
        _conexion = new SqliteConnection($"Data Source={rutaArchivo}");
        _conexion.Open();
        Ejecutar("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS lecturas (ts INTEGER NOT NULL, metrica TEXT NOT NULL, valor REAL NOT NULL, unidad TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_lecturas_ts ON lecturas(ts);
            CREATE TABLE IF NOT EXISTS agregados (id INTEGER PRIMARY KEY AUTOINCREMENT, ventana INTEGER NOT NULL UNIQUE, json TEXT NOT NULL, enviado INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS eventos (id INTEGER PRIMARY KEY AUTOINCREMENT, json TEXT NOT NULL, creado INTEGER NOT NULL, entregado INTEGER NOT NULL DEFAULT 0, intentos INTEGER NOT NULL DEFAULT 0);
            """);
    }

    public void GuardarLecturas(IEnumerable<Lectura> lecturas)
    {
        lock (_bloqueo)
        {
            using var tx = _conexion.BeginTransaction();
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "INSERT INTO lecturas (ts, metrica, valor, unidad) VALUES ($ts, $m, $v, $u)";
            var ts = cmd.Parameters.Add("$ts", SqliteType.Integer);
            var m = cmd.Parameters.Add("$m", SqliteType.Text);
            var v = cmd.Parameters.Add("$v", SqliteType.Real);
            var u = cmd.Parameters.Add("$u", SqliteType.Text);
            foreach (var l in lecturas)
            {
                ts.Value = l.Ts.ToUnixTimeMilliseconds();
                m.Value = l.Metrica;
                v.Value = l.Valor;
                u.Value = l.Unidad;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public IReadOnlyList<Lectura> LeerLecturas(DateTimeOffset desde, DateTimeOffset hasta)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "SELECT ts, metrica, valor, unidad FROM lecturas WHERE ts >= $d AND ts < $h";
            cmd.Parameters.AddWithValue("$d", desde.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$h", hasta.ToUnixTimeMilliseconds());
            using var r = cmd.ExecuteReader();
            var lista = new List<Lectura>();
            while (r.Read())
            {
                lista.Add(new Lectura(r.GetString(1), r.GetDouble(2), r.GetString(3), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0))));
            }
            return lista;
        }
    }

    /// <summary>Encola el agregado de una ventana (ignorado si esa ventana ya se encoló).</summary>
    public void EncolarAgregado(DateTimeOffset ventana, object documento)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO agregados (ventana, json) VALUES ($v, $j)";
            cmd.Parameters.AddWithValue("$v", ventana.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(documento));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<(long Id, string Json)> AgregadosPendientes(int maximo = 500)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "SELECT id, json FROM agregados WHERE enviado = 0 ORDER BY ventana LIMIT $max";
            cmd.Parameters.AddWithValue("$max", maximo);
            using var r = cmd.ExecuteReader();
            var lista = new List<(long, string)>();
            while (r.Read()) lista.Add((r.GetInt64(0), r.GetString(1)));
            return lista;
        }
    }

    public void MarcarAgregadosEnviados(IEnumerable<long> ids) => MarcarIds("UPDATE agregados SET enviado = 1 WHERE id = $id", ids);

    public void EncolarEvento(object evento)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "INSERT INTO eventos (json, creado) VALUES ($j, $c)";
            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(evento));
            cmd.Parameters.AddWithValue("$c", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<(long Id, string Json, int Intentos)> EventosPendientes()
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "SELECT id, json, intentos FROM eventos WHERE entregado = 0 ORDER BY id LIMIT 100";
            using var r = cmd.ExecuteReader();
            var lista = new List<(long, string, int)>();
            while (r.Read()) lista.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
            return lista;
        }
    }

    public void MarcarEventoEntregado(long id) => MarcarIds("UPDATE eventos SET entregado = 1 WHERE id = $id", [id]);

    public void SumarIntentoEvento(long id) => MarcarIds("UPDATE eventos SET intentos = intentos + 1 WHERE id = $id", [id]);

    /// <summary>Reemplaza el contenido de un evento pendiente (ej. para marcar que ya salió el respaldo por Telegram).</summary>
    public void ActualizarEvento(long id, string json)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "UPDATE eventos SET json = $j WHERE id = $id";
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Borra lecturas crudas antiguas y agregados/eventos ya enviados (mantiene el disco acotado).</summary>
    public int Purgar(TimeSpan retencionCrudo)
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = """
                DELETE FROM lecturas WHERE ts < $limite;
                DELETE FROM agregados WHERE enviado = 1 AND ventana < $limite;
                DELETE FROM eventos WHERE entregado = 1 AND creado < $limite;
                """;
            cmd.Parameters.AddWithValue("$limite", DateTimeOffset.UtcNow.Subtract(retencionCrudo).ToUnixTimeMilliseconds());
            return cmd.ExecuteNonQuery();
        }
    }

    public string Resumen()
    {
        lock (_bloqueo)
        {
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM lecturas), (SELECT COUNT(*) FROM agregados WHERE enviado = 0), (SELECT COUNT(*) FROM eventos WHERE entregado = 0)";
            using var r = cmd.ExecuteReader();
            r.Read();
            return string.Create(CultureInfo.InvariantCulture, $"lecturas={r.GetInt64(0)} agregados_pendientes={r.GetInt64(1)} eventos_pendientes={r.GetInt64(2)}");
        }
    }

    private void MarcarIds(string sql, IEnumerable<long> ids)
    {
        lock (_bloqueo)
        {
            using var tx = _conexion.BeginTransaction();
            using var cmd = _conexion.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.Parameters.Add("$id", SqliteType.Integer);
            foreach (var id in ids)
            {
                p.Value = id;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    private void Ejecutar(string sql)
    {
        using var cmd = _conexion.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conexion.Dispose();
}
