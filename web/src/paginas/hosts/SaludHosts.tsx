import { useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Cable, CheckCircle2, ChevronRight, Clock, Laptop, LayoutGrid, List, Monitor, RefreshCw, Search, ServerCrash, Signal, Smartphone, Wifi } from "lucide-react";
import { hosts, type Host } from "../../api/hosts";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { puedeAgregarEquipos } from "../../api/admin";
import { AltaEquipo } from "../admin/AltaEquipo";
import { Alerta } from "../../componentes/ui/Basicos";
import { describir, destacadas, duracion, hace, nivel, NOMBRE_CLASE, claseHost, type Clase } from "./metricas";
import { EtiquetaSalud, IconoHost, Medidor, PildoraConexion } from "./Piezas";
import { useAhora } from "./useAhora";
import s from "./hosts.module.css";

/** Los agentes publican el estado cada minuto. */
const REFRESCO_MS = 30_000;

export default function SaludHosts() {
  const { datos, error, cargando, actualizado, recargar } = useConsulta((t) => hosts.listar(t), [], REFRESCO_MS);
  const ahora = useAhora();
  const { usuario } = useSesion();
  const lista = datos ?? [];

  const enLinea = lista.filter((h) => h.conexion === "en-linea").length;
  const conAlertas = lista.filter((h) => h.salud === "advertencia" || h.salud === "excepcion").length;
  const excepciones = lista.filter((h) => h.salud === "excepcion").length;

  return (
    <>
      <div className={s.encabezado}>
        <div>
          <h1 className={s.titulo}>Salud de hosts</h1>
          <p className={s.subtitulo}>Estado en vivo de los equipos con agente. Se actualiza cada 30 segundos.</p>
        </div>
        <div className={s.refresco}>
          {actualizado && <span className={s.marcaTiempo}>Actualizado {hace(actualizado.toISOString(), ahora)}</span>}
          <button type="button" className={s.botonIcono} onClick={() => void recargar()} disabled={cargando} aria-label="Actualizar ahora" title="Actualizar ahora">
            <RefreshCw size={16} aria-hidden="true" className={cargando ? s.girando : undefined} />
          </button>
          {puedeAgregarEquipos(usuario?.rol) && <AltaEquipo />}
        </div>
      </div>

      {error && <div className={s.bloque}><Alerta tipo="error">{error}</Alerta></div>}

      <section className={s.resumen} aria-label="Resumen">
        <Indicador icono={<Wifi size={18} />} tono="info" valor={datos ? `${enLinea}/${lista.length}` : "—"} texto="Hosts en línea" />
        <Indicador icono={<CheckCircle2 size={18} />} tono="ok" valor={datos ? String(lista.filter((h) => h.salud === "ok").length) : "—"} texto="Saludables" />
        <Indicador icono={<AlertTriangle size={18} />} tono="advertencia" valor={datos ? String(conAlertas) : "—"} texto="Con alertas" />
        <Indicador icono={<ServerCrash size={18} />} tono="excepcion" valor={datos ? String(excepciones) : "—"} texto="En excepción" />
      </section>

      {!datos && cargando ? (
        <section className={s.grid} aria-busy="true" aria-label="Cargando hosts">
          {[0, 1, 2].map((i) => <div key={i} className={`${s.tarjeta} ${s.esqueleto}`} />)}
        </section>
      ) : lista.length === 0 && !error ? (
        <div className={s.vacio}>
          <ServerCrash size={28} aria-hidden="true" />
          <h2>Todavía no hay hosts</h2>
          <p>{puedeAgregarEquipos(usuario?.rol) ? "Pulsa «Agregar equipo», instala el agente con el código y aparecerá aquí en menos de un minuto." : "Un técnico N2 o N3 puede agregar equipos; aparecerán aquí en menos de un minuto."}</p>
        </div>
      ) : (
        <Equipos lista={lista} ahora={ahora} />
      )}
    </>
  );
}

type Vista = "tarjetas" | "lista";
type FiltroEstado = "todos" | "atencion";
type FiltroTipo = "todos" | Clase;

const GRUPOS_CLASE: { clase: Clase; titulo: string; icono: ReactNode }[] = [
  { clase: "escritorio", titulo: "Escritorios", icono: <Monitor size={16} aria-hidden="true" /> },
  { clase: "portatil", titulo: "Portátiles", icono: <Laptop size={16} aria-hidden="true" /> },
  { clase: "movil", titulo: "Celulares y tablets", icono: <Smartphone size={16} aria-hidden="true" /> },
];

/** Primero lo que requiere atención: excepción, advertencia, sin reporte, retrasado y, al final, lo saludable. */
function prioridad(h: Host): number {
  if (h.salud === "excepcion") return 0;
  if (h.salud === "advertencia") return 1;
  if (h.conexion === "sin-reporte") return 2;
  if (h.conexion === "retrasado") return 3;
  return 4;
}
const requiereAtencion = (h: Host) => prioridad(h) < 4;
const ordenar = (a: Host, b: Host) => prioridad(a) - prioridad(b) || a.nombre.localeCompare(b.nombre, "es", { numeric: true });

/** Preferencia recordada en este navegador (si el almacenamiento no está disponible, solo dura la visita). */
function usePreferencia<T extends string>(clave: string, inicial: T, validos: readonly T[]): [T, (v: T) => void] {
  const [valor, setValor] = useState<T>(() => {
    try {
      const guardado = localStorage.getItem(clave) as T | null;
      return guardado && validos.includes(guardado) ? guardado : inicial;
    } catch {
      return inicial;
    }
  });
  const guardar = (v: T) => {
    setValor(v);
    try {
      localStorage.setItem(clave, v);
    } catch {
      // Sin almacenamiento: la preferencia solo dura esta visita.
    }
  };
  return [valor, guardar];
}

function Equipos({ lista, ahora }: { lista: Host[]; ahora: number }) {
  const [vista, setVista] = usePreferencia<Vista>("syspulse.hosts.vista", "tarjetas", ["tarjetas", "lista"]);
  const [estado, setEstado] = usePreferencia<FiltroEstado>("syspulse.hosts.estado", "todos", ["todos", "atencion"]);
  const [tipo, setTipo] = usePreferencia<FiltroTipo>("syspulse.hosts.tipo", "todos", ["todos", "escritorio", "portatil", "movil"]);
  const [busqueda, setBusqueda] = useState("");

  const conteoClase = useMemo(() => {
    const c: Record<Clase, number> = { escritorio: 0, portatil: 0, movil: 0 };
    for (const h of lista) c[claseHost(h)]++;
    return c;
  }, [lista]);
  const atencion = lista.filter(requiereAtencion).length;

  const texto = busqueda.trim().toLocaleLowerCase("es");
  const visibles = lista
    .filter((h) => estado === "todos" || requiereAtencion(h))
    .filter((h) => tipo === "todos" || claseHost(h) === tipo)
    .filter((h) => !texto || h.nombre.toLocaleLowerCase("es").includes(texto))
    .sort(ordenar);

  return (
    <>
      <div className={s.barra}>
        <label className={s.buscador}>
          <Search size={16} aria-hidden="true" />
          <input type="search" value={busqueda} onChange={(e) => setBusqueda(e.target.value)} placeholder="Buscar equipo" aria-label="Buscar equipo por nombre" />
        </label>
        <div className={s.filtros} role="group" aria-label="Filtrar por estado">
          <Chip activo={estado === "todos"} onClick={() => setEstado("todos")}>Todos <b className="num">{lista.length}</b></Chip>
          <Chip activo={estado === "atencion"} onClick={() => setEstado("atencion")} tono={atencion > 0 ? "atencion" : undefined}>
            Requieren atención <b className="num">{atencion}</b>
          </Chip>
        </div>
        <div className={s.filtros} role="group" aria-label="Filtrar por tipo de equipo">
          <Chip activo={tipo === "todos"} onClick={() => setTipo("todos")}>Todos los tipos</Chip>
          {GRUPOS_CLASE.filter((g) => conteoClase[g.clase] > 0).map((g) => (
            <Chip key={g.clase} activo={tipo === g.clase} onClick={() => setTipo(g.clase)}>
              {g.icono} {g.titulo} <b className="num">{conteoClase[g.clase]}</b>
            </Chip>
          ))}
        </div>
        <div className={s.vistas} role="group" aria-label="Vista">
          <button type="button" className={s.botonVista} aria-pressed={vista === "tarjetas"} onClick={() => setVista("tarjetas")} title="Ver como tarjetas">
            <LayoutGrid size={16} aria-hidden="true" /><span>Tarjetas</span>
          </button>
          <button type="button" className={s.botonVista} aria-pressed={vista === "lista"} onClick={() => setVista("lista")} title="Ver como lista">
            <List size={16} aria-hidden="true" /><span>Lista</span>
          </button>
        </div>
      </div>

      {visibles.length === 0 ? (
        <p className={s.sinResultados}>Ningún equipo coincide con la búsqueda o los filtros.</p>
      ) : vista === "lista" ? (
        <TablaEquipos lista={visibles} ahora={ahora} />
      ) : (
        GRUPOS_CLASE.map((g) => {
          const grupo = visibles.filter((h) => claseHost(h) === g.clase);
          if (grupo.length === 0) return null;
          const conAtencion = grupo.filter(requiereAtencion).length;
          return (
            <section key={g.clase} className={s.grupo} aria-label={g.titulo}>
              <h2 className={s.grupoTitulo}>
                <span className={s.grupoIcono}>{g.icono}</span>
                {g.titulo}
                <span className={`${s.grupoConteo} num`}>{grupo.length}</span>
                {conAtencion > 0 && <span className={s.grupoAtencion}>{conAtencion} requieren atención</span>}
              </h2>
              <div className={s.grid}>
                {grupo.map((h) => <TarjetaHost key={h.id} host={h} ahora={ahora} />)}
              </div>
            </section>
          );
        })
      )}
    </>
  );
}

function Chip({ activo, onClick, tono, children }: { activo: boolean; onClick: () => void; tono?: "atencion"; children: ReactNode }) {
  return (
    <button type="button" className={s.chip} aria-pressed={activo} data-tono={tono} onClick={onClick}>
      {children}
    </button>
  );
}

/** Vista compacta: una fila por equipo, para revisar muchos de un vistazo. */
function TablaEquipos({ lista, ahora }: { lista: Host[]; ahora: number }) {
  // Mismo color que en las tarjetas: amarillo o rojo si el valor pasa su umbral (en batería, menos es peor).
  const valor = (h: Host, clave: string | undefined) => {
    if (!clave || h.lecturas[clave] === undefined) return "—";
    const v = h.lecturas[clave];
    const tono = clave === "bateria_pct" ? (v <= 15 ? "excepcion" : v <= 30 ? "advertencia" : "ok") : nivel(h, clave);
    return <span className={s.medidorValor} data-nivel={tono}>{describir(clave).formato(v)}</span>;
  };
  return (
    <div className={s.tablaContenedor}>
      <table className={s.tabla}>
        <thead>
          <tr>
            <th scope="col">Equipo</th>
            <th scope="col">Estado</th>
            <th scope="col" className={s.numCol}>CPU / Batería</th>
            <th scope="col" className={s.numCol}>RAM</th>
            <th scope="col" className={s.numCol}>Disco</th>
            <th scope="col">Red activa</th>
            <th scope="col">Último reporte</th>
          </tr>
        </thead>
        <tbody>
          {lista.map((h) => {
            const l = h.lecturas;
            const disco = Object.keys(l).filter((c) => /^disco_[A-Za-z]_pct$/.test(c)).sort((a, b) => l[b] - l[a])[0]
              ?? (l.almacenamiento_pct !== undefined ? "almacenamiento_pct" : undefined);
            const principal = h.tipo === "android" ? "bateria_pct" : "cpu_total";
            return (
              <tr key={h.id} data-salud={h.salud}>
                <th scope="row">
                  <Link to={`/hosts/${encodeURIComponent(h.id)}`} className={s.tablaEquipo}>
                    <IconoHost host={h} size={16} />
                    <span>
                      <span className={s.tablaNombre}>{h.nombre}</span>
                      <span className={s.tablaTipo}>{NOMBRE_CLASE[claseHost(h)]}</span>
                    </span>
                  </Link>
                </th>
                <td><span className={s.tablaEstado}><EtiquetaSalud salud={h.salud} /><PildoraConexion conexion={h.conexion} /></span></td>
                <td className={`${s.numCol} num`}>{valor(h, principal)}</td>
                <td className={`${s.numCol} num`}>{valor(h, "ram_pct")}</td>
                <td className={`${s.numCol} num`}>{valor(h, disco)}</td>
                <td><RedResumen host={h} /></td>
                <td className={s.tablaReporte}>{hace(h.ultimoReporte, ahora)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/** Conexión activa (o la de más tráfico) y su bajada acumulada, para la vista de lista. */
function RedResumen({ host }: { host: Host }) {
  const l = host.lecturas;
  const filas = CONEXIONES
    .map((c) => ({ ...c, bajada: l[`${c.prefijo}_bajada_mb`] ?? 0, tasa: (l[`${c.prefijo}_bajada_mbs`] ?? 0) + (l[`${c.prefijo}_subida_mbs`] ?? 0) }))
    .filter((c) => c.bajada >= 0.05 || c.tasa > 0);
  if (filas.length === 0) return <span className={s.tablaTipo}>—</span>;
  const activa = (l.red_tipo !== undefined ? filas.find((c) => c.tipo === Math.round(l.red_tipo)) : undefined)
    ?? [...filas].sort((a, b) => b.tasa - a.tasa || b.bajada - a.bajada)[0];
  return (
    <span className={s.tablaRed}>
      {activa.icono} {activa.nombre} <span className="num">↓ {describir("red_wifi_bajada_mb").formato(activa.bajada)}</span>
    </span>
  );
}

function TarjetaHost({ host, ahora }: { host: Host; ahora: number }) {
  const claves = destacadas(host);
  const primera = host.alertas[0];
  const l = host.lecturas;
  const red = host.tipo === "android"
    ? l.red_ping_8_8_8_8_rtt_ms !== undefined ? `Ping ${Math.round(l.red_ping_8_8_8_8_rtt_ms)} ms` : null
    : l.red_entrada_mbs !== undefined ? `↓ ${describir("red_entrada_mbs").formato(l.red_entrada_mbs)}  ↑ ${describir("red_salida_mbs").formato(l.red_salida_mbs ?? 0)}` : null;

  return (
    <Link to={`/hosts/${encodeURIComponent(host.id)}`} className={s.tarjeta} data-salud={host.salud} data-conexion={host.conexion}>
      <div className={s.tarjetaCabeza}>
        <IconoHost host={host} />
        <div className={s.identidad}>
          <h3 className={s.nombre} title={host.nombre}>{host.nombre}</h3>
          <p className={s.tipo}>{NOMBRE_CLASE[claseHost(host)]}</p>
        </div>
        <PildoraConexion conexion={host.conexion} />
      </div>

      <div className={s.lineaEstado}>
        <EtiquetaSalud salud={host.salud} />
        <span className={s.reporte}>
          <Clock size={13} aria-hidden="true" /> {hace(host.ultimoReporte, ahora)}
        </span>
      </div>

      {primera && host.salud !== "desconocida" && (
        <p className={s.alertaTarjeta} data-severidad={primera.severidad}>
          <AlertTriangle size={14} aria-hidden="true" />
          <span>
            {describir(primera.metrica).etiqueta}: <strong className="num">{describir(primera.metrica).formato(primera.valor)}</strong>
            {" "}(umbral {describir(primera.metrica).formato(primera.umbral)})
          </span>
          {host.alertas.length > 1 && <span className={s.mas}>+{host.alertas.length - 1}</span>}
        </p>
      )}

      <div className={s.medidores}>
        {claves.map((c) => <Medidor key={c} host={host} clave={c} />)}
        {claves.length === 0 && <p className={s.sinDatos}>El agente aún no ha enviado lecturas.</p>}
      </div>

      <ConsumoRed host={host} />

      <div className={s.pie}>
        <span>{l.sistema_uptime_h !== undefined ? `Encendido ${duracion(l.sistema_uptime_h)}` : "—"}</span>
        {red && <span className="num">{red}</span>}
        <ChevronRight size={16} aria-hidden="true" className={s.flecha} />
      </div>
    </Link>
  );
}

/** Conexiones que reportan los agentes: código de red_tipo (Android) y prefijo de sus métricas. */
const CONEXIONES = [
  { prefijo: "red_wifi", nombre: "Wi-Fi", tipo: 1, icono: <Wifi size={14} aria-hidden="true" /> },
  { prefijo: "red_movil", nombre: "Datos móviles", tipo: 2, icono: <Signal size={14} aria-hidden="true" /> },
  { prefijo: "red_utp", nombre: "Cable UTP", tipo: 3, icono: <Cable size={14} aria-hidden="true" /> },
] as const;

/**
 * Consumo de red por tipo de conexión (bajada y subida acumuladas). Solo muestra las conexiones que el equipo
 * ha usado; marca la activa (la que informa el celular o, en Windows, la que tiene tráfico en este momento).
 */
function ConsumoRed({ host }: { host: Host }) {
  const l = host.lecturas;
  const filas = CONEXIONES
    .map((c) => ({
      ...c,
      bajada: l[`${c.prefijo}_bajada_mb`] ?? 0,
      subida: l[`${c.prefijo}_subida_mb`] ?? 0,
      tasa: (l[`${c.prefijo}_bajada_mbs`] ?? 0) + (l[`${c.prefijo}_subida_mbs`] ?? 0),
    }))
    .filter((c) => c.bajada + c.subida >= 0.05 || c.tasa > 0);
  if (filas.length === 0) return null;

  const porTasa = [...filas].sort((a, b) => b.tasa - a.tasa)[0];
  const activa = l.red_tipo !== undefined ? filas.find((c) => c.tipo === Math.round(l.red_tipo))?.prefijo : porTasa.tasa > 0 ? porTasa.prefijo : undefined;
  const mb = describir("red_wifi_bajada_mb").formato;

  return (
    <div className={s.consumoRed}>
      <p className={s.consumoTitulo}>Consumo de red</p>
      <ul>
        {filas.map((c) => (
          <li key={c.prefijo} data-activa={c.prefijo === activa || undefined}>
            <span className={s.consumoNombre}>{c.icono}{c.nombre}{c.prefijo === activa && <span className={s.consumoActiva}>activa</span>}</span>
            <span className="num">↓ {mb(c.bajada)}  ↑ {mb(c.subida)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function Indicador({ icono, valor, texto, tono }: { icono: ReactNode; valor: string; texto: string; tono: string }) {
  return (
    <div className={s.indicador} data-tono={tono}>
      <span className={s.indicadorIcono} aria-hidden="true">{icono}</span>
      <div>
        <p className={`${s.indicadorValor} num`}>{valor}</p>
        <p className={s.indicadorTexto}>{texto}</p>
      </div>
    </div>
  );
}

