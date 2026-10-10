import { useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, ArrowUpCircle, CheckCircle2, Clock, GitCommitHorizontal, Inbox, Layers, RefreshCw, RotateCcw, ShieldCheck, Timer } from "lucide-react";
import { analitica, type Analitica as Datos, type IndicadorSla } from "../../api/analitica";
import { useConsulta } from "../../api/useConsulta";
import { Alerta } from "../../componentes/ui/Basicos";
import { InsigniaPrioridad } from "../incidentes/Piezas";
import type { Prioridad } from "../../api/incidentes";
import { Barras } from "./Barras";
import h from "../hosts/hosts.module.css";
import s from "./analitica.module.css";

const PERIODOS = [7, 30, 90];

function duracion(min: number | null): string {
  if (min === null) return "—";
  if (min < 60) return `${Math.round(min)} min`;
  if (min < 1440) return `${Math.floor(min / 60)} h ${Math.round(min % 60)} min`;
  return `${Math.floor(min / 1440)} d ${Math.round((min % 1440) / 60)} h`;
}

const pct = (v: number | null) => (v === null ? "—" : `${v.toLocaleString("es-CO", { maximumFractionDigits: 1 })}%`);

/** Meta de SLA de referencia: 95 % de cumplimiento. */
const tonoSla = (v: number | null) => (v === null ? "neutro" : v >= 95 ? "ok" : v >= 80 ? "advertencia" : "excepcion");

export default function Analitica() {
  const [dias, setDias] = useState(30);
  const { datos, error, cargando, recargar } = useConsulta((t) => analitica.obtener(t, dias), [dias], 5 * 60_000);

  return (
    <>
      <div className={h.encabezado}>
        <div>
          <h1 className={h.titulo}>Analítica</h1>
          <p className={h.subtitulo}>Indicadores ITIL: niveles de servicio, incidentes, problemas, disponibilidad y cambios.</p>
        </div>
        <div className={h.refresco}>
          <div className={s.periodos} role="radiogroup" aria-label="Período">
            {PERIODOS.map((p) => (
              <button key={p} type="button" role="radio" aria-checked={dias === p} onClick={() => setDias(p)}>{p} días</button>
            ))}
          </div>
          <button type="button" className={h.botonIcono} onClick={() => void recargar()} disabled={cargando} aria-label="Actualizar" title="Actualizar">
            <RefreshCw size={16} aria-hidden="true" className={cargando ? h.girando : undefined} />
          </button>
        </div>
      </div>

      {error && <div className={h.bloque}><Alerta tipo="error">{error}</Alerta></div>}
      {!datos ? <div className={`${s.cargando} ${h.esqueleto}`} aria-busy="true" /> : <Contenido d={datos} />}
    </>
  );
}

function Contenido({ d }: { d: Datos }) {
  const categorias = useMemo(() => d.porDia.map((p) => new Date(`${p.dia}T12:00:00`).toLocaleDateString("es-CO", { day: "2-digit", month: "short" })), [d.porDia]);
  const series = useMemo(() => [
    { nombre: "Creados", valores: d.porDia.map((p) => p.creados), color: "--accent" },
    { nombre: "Resueltos", valores: d.porDia.map((p) => p.resueltos), color: "--ok" },
  ], [d.porDia]);

  return (
    <>
      <h2 className={s.seccion}><ShieldCheck size={18} aria-hidden="true" /> Niveles de servicio</h2>
      <section className={s.indicadores}>
        <Indicador icono={<Timer size={18} />} titulo="SLA de respuesta" valor={pct(d.slaRespuesta.porcentaje)} tono={tonoSla(d.slaRespuesta.porcentaje)} detalle={detalleSla(d.slaRespuesta)} />
        <Indicador icono={<CheckCircle2 size={18} />} titulo="SLA de resolución" valor={pct(d.slaResolucion.porcentaje)} tono={tonoSla(d.slaResolucion.porcentaje)} detalle={detalleSla(d.slaResolucion)} />
        <Indicador icono={<Clock size={18} />} titulo="MTTA" valor={duracion(d.mttaMinutos)} detalle="Tiempo medio hasta que alguien lo toma" />
        <Indicador icono={<Clock size={18} />} titulo="MTTR" valor={duracion(d.mttrMinutos)} detalle="Tiempo medio hasta resolverlo" />
      </section>

      <h2 className={s.seccion}><Inbox size={18} aria-hidden="true" /> Gestión de incidentes</h2>
      <section className={s.indicadores}>
        <Indicador icono={<Inbox size={18} />} titulo="Incidentes del período" valor={String(d.incidentesTotal)}
          detalle={`${d.porOrigen.evento ?? 0} del monitoreo · ${d.porOrigen.manual ?? 0} manuales · ${d.abiertos} abiertos hoy`} />
        <Indicador icono={<ArrowUpCircle size={18} />} titulo="Tasa de escalamiento" valor={pct(d.tasaEscalamiento)} detalle="Incidentes que pasaron a N2 o N3" />
        <Indicador icono={<CheckCircle2 size={18} />} titulo="Resueltos en primer nivel" valor={pct(d.resueltosEnN1)} detalle="Resueltos por N1 sin escalar" />
        <Indicador icono={<RotateCcw size={18} />} titulo="Reaperturas" valor={String(d.reaperturas)} tono={d.reaperturas > 0 ? "advertencia" : "neutro"} detalle="Resueltos que volvieron a abrirse" />
      </section>

      <div className={s.rejilla}>
        <section className={s.panel}>
          <h3 className={s.tituloPanel}>Incidentes por día</h3>
          {d.incidentesTotal === 0
            ? <p className={s.vacio}>Sin incidentes en el período.</p>
            : <Barras categorias={categorias} series={series} />}
        </section>

        <section className={s.panel}>
          <h3 className={s.tituloPanel}>Por prioridad</h3>
          <table className={s.tabla}>
            <thead><tr><th>Prioridad</th><th className={s.num}>Incidentes</th><th>SLA de resolución</th></tr></thead>
            <tbody>
              {d.porPrioridad.map((p) => (
                <tr key={p.prioridad}>
                  <td><InsigniaPrioridad prioridad={p.prioridad as Prioridad} /></td>
                  <td className={`${s.num} num`}>{p.total}</td>
                  <td>
                    {p.resolucion.porcentaje === null ? <span className={s.tenue}>Sin medir</span> : (
                      <span className={s.medida}>
                        <span className={s.barra}><span data-tono={tonoSla(p.resolucion.porcentaje)} style={{ width: `${p.resolucion.porcentaje}%` }} /></span>
                        <span className="num">{pct(p.resolucion.porcentaje)}</span>
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>

      <h2 className={s.seccion}><Layers size={18} aria-hidden="true" /> Gestión de problemas</h2>
      <section className={s.panel}>
        <p className={s.nota}>Candidatos a problema: la misma alerta del mismo equipo en excepción {3} veces o más en los últimos 7 días. Investigar la causa raíz evita que el incidente se repita.</p>
        {d.candidatosProblema.length === 0 ? (
          <p className={s.todoBien}><CheckCircle2 size={16} aria-hidden="true" /> Ninguna alerta se repite lo suficiente como para proponer un problema.</p>
        ) : (
          <div className={s.desplazable}>
            <table className={s.tabla}>
              <thead><tr><th>Equipo</th><th>Alerta</th><th className={s.num}>Excepciones (7 d)</th><th>Última</th><th /></tr></thead>
              <tbody>
                {d.candidatosProblema.map((c) => (
                  <tr key={`${c.hostNombre}-${c.metrica}`}>
                    <td>{c.hostNombre}</td>
                    <td><AlertTriangle size={14} aria-hidden="true" className={s.iconoAlerta} /> {c.metrica}</td>
                    <td className={`${s.num} num`}>{c.excepciones}</td>
                    <td>{new Date(c.ultima).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" })}</td>
                    <td>{c.incidenteAbiertoId && <Link to={`/incidentes/${c.incidenteAbiertoId}`}>Ver incidente</Link>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <h2 className={s.seccion}><ShieldCheck size={18} aria-hidden="true" /> Disponibilidad y capacidad de los equipos</h2>
      <section className={s.panel}>
        <p className={s.nota}>Disponibilidad = porcentaje de ventanas de 5 minutos con datos desde que el equipo empezó a reportar en el período.</p>
        <div className={s.desplazable}>
          <table className={s.tabla}>
            <thead><tr><th>Equipo</th><th>Disponibilidad</th><th className={s.num}>CPU prom.</th><th className={s.num}>CPU máx.</th><th className={s.num}>RAM prom.</th><th className={s.num}>RAM máx.</th></tr></thead>
            <tbody>
              {d.hosts.map((x) => (
                <tr key={x.id}>
                  <td><Link to={`/hosts/${encodeURIComponent(x.id)}`}>{x.nombre}</Link></td>
                  <td>
                    <span className={s.medida}>
                      <span className={s.barra}><span data-tono={x.disponibilidad >= 99 ? "ok" : x.disponibilidad >= 95 ? "advertencia" : "excepcion"} style={{ width: `${x.disponibilidad}%` }} /></span>
                      <span className="num">{pct(x.disponibilidad)}</span>
                    </span>
                  </td>
                  <td className={`${s.num} num`}>{pct(x.cpuPromedio)}</td>
                  <td className={`${s.num} num`}>{pct(x.cpuMaximo)}</td>
                  <td className={`${s.num} num`}>{pct(x.ramPromedio)}</td>
                  <td className={`${s.num} num`}>{pct(x.ramMaximo)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <h2 className={s.seccion}><GitCommitHorizontal size={18} aria-hidden="true" /> Habilitación de cambios</h2>
      <section className={s.panel}>
        <p className={s.nota}>Cambios estándar registrados en el período (versiones de los agentes publicadas para actualización automática).</p>
        {d.cambios.length === 0 ? <p className={s.vacio}>Sin cambios en el período.</p> : (
          <ul className={s.cambios}>
            {d.cambios.map((c) => (
              <li key={c.id}>
                <span className={s.tipoCambio}>{c.tipo === "estandar" ? "Estándar" : c.tipo}</span>
                <strong>{c.titulo}</strong>
                <span className={s.tenue}>{c.estado} · {new Date(c.creado).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" })}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <p className={s.pie}>Eventos de monitoreo en el período: {d.eventosTotal} ({d.eventosExcepcion} excepciones, {d.eventosAdvertencia} advertencias).</p>
    </>
  );
}

function detalleSla(i: IndicadorSla): string {
  const total = i.cumplidos + i.incumplidos;
  return total === 0 ? "Aún no hay incidentes medidos" : `${i.cumplidos} de ${total} dentro del objetivo (meta 95 %)`;
}

function Indicador({ icono, titulo, valor, detalle, tono = "neutro" }: { icono: ReactNode; titulo: string; valor: string; detalle: string; tono?: string }) {
  return (
    <div className={s.indicador} data-tono={tono}>
      <span className={s.indicadorIcono} aria-hidden="true">{icono}</span>
      <div>
        <p className={s.indicadorTitulo}>{titulo}</p>
        <p className={`${s.indicadorValor} num`}>{valor}</p>
        <p className={s.indicadorDetalle}>{detalle}</p>
      </div>
    </div>
  );
}
