import { useState } from "react";
import { Link, useParams } from "react-router";
import { AlertTriangle, ArrowLeft, CheckCircle2, Clock, Cpu, Power, RefreshCw } from "lucide-react";
import { hosts, type Host } from "../../api/hosts";
import { useConsulta } from "../../api/useConsulta";
import { Alerta } from "../../componentes/ui/Basicos";
import { claseHost, describir, duracion, GRUPOS, hace, nivel, NOMBRE_CLASE } from "./metricas";
import { EtiquetaSalud, IconoHost, Medidor, PildoraConexion } from "./Piezas";
import { Grafico } from "./Grafico";
import { useAhora } from "./useAhora";
import s from "./hosts.module.css";
import d from "./detalle.module.css";

const RANGOS = [
  { horas: 6, texto: "6 h" },
  { horas: 24, texto: "24 h" },
  { horas: 168, texto: "7 días" },
];

/** Gráficas por tipo de equipo (solo las que el host realmente reporta). */
function metricasGrafico(h: Host): string[] {
  const l = h.lecturas;
  const discos = Object.keys(l).filter((c) => /^disco_[A-Za-z]_pct$/.test(c)).sort();
  const lista = h.tipo === "android"
    ? ["bateria_pct", "bateria_temp_c", "ram_pct", "almacenamiento_pct", "red_ping_8_8_8_8_rtt_ms", "red_movil_bajada_mbs", "red_wifi_bajada_mbs"]
    : ["cpu_total", "ram_pct", ...discos, "paginacion_pct", "red_entrada_mbs", "red_utp_bajada_mbs", "red_wifi_bajada_mbs", "red_movil_bajada_mbs", "bateria_pct"];
  return lista.filter((c) => l[c] !== undefined);
}

export default function DetalleHost() {
  const { id = "" } = useParams();
  const ahora = useAhora();
  const [horas, setHoras] = useState(24);
  const host = useConsulta((t) => hosts.obtener(t, id), [id], 30_000);
  const h = host.datos;
  const claves = h ? metricasGrafico(h) : [];
  const historial = useConsulta(
    (t) => (claves.length ? hosts.historial(t, id, horas, claves) : Promise.resolve(null)),
    [id, horas, claves.join(",")],
    5 * 60_000,
  );

  if (host.error && !h) {
    return (
      <>
        <Volver />
        <Alerta tipo="error">{host.error}</Alerta>
      </>
    );
  }

  if (!h) {
    return (
      <>
        <Volver />
        <div className={`${d.cabecera} ${d.cargandoCabecera} ${s.esqueleto}`} aria-busy="true" />
      </>
    );
  }

  const destacadas = ["cpu_total", "ram_pct", "almacenamiento_pct", "bateria_pct"].filter((c) => h.lecturas[c] !== undefined);
  const discos = Object.keys(h.lecturas).filter((c) => /^disco_[A-Za-z]_pct$/.test(c)).sort();

  return (
    <>
      <Volver />

      <section className={d.cabecera} data-salud={h.salud}>
        <IconoHost host={h} size={26} />
        <div className={d.identidad}>
          <h1 className={s.titulo}>{h.nombre}</h1>
          <p className={s.subtitulo}>
            {NOMBRE_CLASE[claseHost(h)]}
          </p>
          <div className={d.chips}>
            <PildoraConexion conexion={h.conexion} />
            <EtiquetaSalud salud={h.salud} />
          </div>
        </div>
        <dl className={d.ficha}>
          <div><dt><Clock size={14} aria-hidden="true" /> Último reporte</dt><dd>{hace(h.ultimoReporte, ahora)}</dd></div>
          <div><dt><Power size={14} aria-hidden="true" /> Encendido</dt><dd>{h.lecturas.sistema_uptime_h !== undefined ? duracion(h.lecturas.sistema_uptime_h) : "—"}</dd></div>
          <div><dt><Cpu size={14} aria-hidden="true" /> Agente</dt><dd className="num">v{h.versionAgente ?? "?"}</dd></div>
        </dl>
      </section>

      <section className={d.bloque} aria-labelledby="alertas-titulo">
        <h2 id="alertas-titulo" className={d.tituloBloque}>Alertas según el valor actual</h2>
        {h.salud === "desconocida" ? (
          <p className={d.nota}>El agente no reporta desde {hace(h.ultimoReporte, ahora)}; los valores pueden no estar al día.</p>
        ) : h.alertas.length === 0 ? (
          <p className={d.todoBien}><CheckCircle2 size={16} aria-hidden="true" /> Ninguna métrica supera su umbral.</p>
        ) : (
          <ul className={d.alertas}>
            {h.alertas.map((a) => {
              const m = describir(a.metrica);
              return (
                <li key={a.metrica} data-severidad={a.severidad}>
                  <AlertTriangle size={16} aria-hidden="true" />
                  <span className={d.alertaNombre}>{m.etiqueta}</span>
                  <span className="num"><strong>{m.formato(a.valor)}</strong> · umbral de {a.severidad === "excepcion" ? "excepción" : "advertencia"} {m.formato(a.umbral)}</span>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className={d.medidoresGrid} aria-label="Uso actual">
        {[...destacadas.filter((c) => c !== "bateria_pct"), ...discos, ...destacadas.filter((c) => c === "bateria_pct")].map((c) => (
          <div key={c} className={d.tarjetaMedidor}><Medidor host={h} clave={c} /></div>
        ))}
      </section>

      <section className={d.bloque} aria-labelledby="historial-titulo">
        <div className={d.cabeceraBloque}>
          <div>
            <h2 id="historial-titulo" className={d.tituloBloque}>Historial</h2>
            <p className={d.nota}>Promedio y máximo cada 5 min. El agente sube el historial cada 30 min; el valor actual está arriba.</p>
          </div>
          <div className={d.rangos} role="radiogroup" aria-label="Rango de tiempo">
            {RANGOS.map((r) => (
              <button key={r.horas} type="button" role="radio" aria-checked={horas === r.horas} onClick={() => setHoras(r.horas)}>
                {r.texto}
              </button>
            ))}
          </div>
        </div>
        {historial.error && <Alerta tipo="error">{historial.error}</Alerta>}
        <div className={d.graficos}>
          {claves.map((c) => {
            const m = describir(c);
            const serie = historial.datos?.series[c];
            return (
              <article key={c} className={d.grafico}>
                <header>
                  <h3>{m.etiqueta}</h3>
                  <span className={`num ${d.actual}`} data-nivel={nivel(h, c)}>{m.formato(h.lecturas[c])}</span>
                </header>
                {!historial.datos && historial.cargando ? (
                  <div className={`${d.lienzo} ${s.esqueleto}`} />
                ) : serie && serie.puntos.length > 1 ? (
                  <Grafico puntos={serie.puntos} formato={m.formato} umbral={h.umbrales[c]} maximo100={m.porcentaje} horas={horas} />
                ) : (
                  <div className={d.lienzoVacio}><RefreshCw size={16} aria-hidden="true" /> Aún no hay historial en este rango.</div>
                )}
              </article>
            );
          })}
        </div>
      </section>

      <section className={d.bloque} aria-labelledby="lecturas-titulo">
        <h2 id="lecturas-titulo" className={d.tituloBloque}>Todas las lecturas</h2>
        <div className={d.grupos}>
          {GRUPOS.map((g) => {
            const filas = Object.keys(h.lecturas).filter((c) => describir(c).grupo === g).sort((a, b) => describir(a).etiqueta.localeCompare(describir(b).etiqueta, "es"));
            if (filas.length === 0) return null;
            return (
              <article key={g} className={d.grupo}>
                <h3>{g}</h3>
                <dl>
                  {filas.map((c) => (
                    <div key={c}>
                      <dt>{describir(c).etiqueta}</dt>
                      <dd className="num" data-nivel={nivel(h, c)}>{describir(c).formato(h.lecturas[c])}</dd>
                    </div>
                  ))}
                </dl>
              </article>
            );
          })}
        </div>
      </section>
    </>
  );
}

function Volver() {
  return (
    <Link to="/hosts" className={d.volver}>
      <ArrowLeft size={16} aria-hidden="true" /> Salud de hosts
    </Link>
  );
}
