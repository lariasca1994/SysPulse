import type { ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Cable, CheckCircle2, ChevronRight, Clock, RefreshCw, ServerCrash, Signal, Wifi } from "lucide-react";
import { hosts, type Host } from "../../api/hosts";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { puedeAgregarEquipos } from "../../api/admin";
import { AltaEquipo } from "../admin/AltaEquipo";
import { Alerta } from "../../componentes/ui/Basicos";
import { describir, destacadas, duracion, hace, NOMBRE_CLASE, claseHost } from "./metricas";
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
        <section className={s.grid} aria-label="Hosts monitoreados">
          {lista.map((h) => <TarjetaHost key={h.id} host={h} ahora={ahora} />)}
        </section>
      )}
    </>
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
    <Link to={`/hosts/${encodeURIComponent(host.id)}`} className={s.tarjeta} data-salud={host.salud}>
      <div className={s.tarjetaCabeza}>
        <IconoHost host={host} />
        <div className={s.identidad}>
          <h2 className={s.nombre}>{host.nombre}</h2>
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

