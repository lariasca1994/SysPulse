import { Laptop, Monitor, Smartphone } from "lucide-react";
import type { Conexion, Host, Salud } from "../../api/hosts";
import { claseHost, describir, nivel } from "./metricas";
import s from "./hosts.module.css";

const TEXTO_CONEXION: Record<Conexion, string> = {
  "en-linea": "En línea",
  retrasado: "Retrasado",
  "sin-reporte": "Sin reporte",
};

export function PildoraConexion({ conexion }: { conexion: Conexion }) {
  return (
    <span className={s.pildora} data-conexion={conexion}>
      <span className={s.punto} aria-hidden="true" />
      {TEXTO_CONEXION[conexion]}
    </span>
  );
}

const TEXTO_SALUD: Record<Salud, string> = {
  ok: "Saludable",
  advertencia: "Advertencia",
  excepcion: "Excepción",
  desconocida: "Sin datos recientes",
};

export function EtiquetaSalud({ salud }: { salud: Salud }) {
  return (
    <span className={s.salud} data-salud={salud}>
      {TEXTO_SALUD[salud]}
    </span>
  );
}

export function IconoHost({ host, size = 20 }: { host: Host; size?: number }) {
  const clase = claseHost(host);
  const Icono = clase === "movil" ? Smartphone : clase === "portatil" ? Laptop : Monitor;
  return (
    <span className={s.iconoHost} data-salud={host.salud}>
      <Icono size={size} aria-hidden="true" />
    </span>
  );
}

/** Barra de porcentaje con marcas de advertencia y excepción si la métrica tiene umbral. */
export function Medidor({ host, clave }: { host: Host; clave: string }) {
  const d = describir(clave);
  const valor = host.lecturas[clave];
  const u = host.umbrales[clave];
  const n = nivel(host, clave);
  const ancho = Math.max(0, Math.min(100, valor));
  // En batería, menos es peor: el color depende de la carga, no de un umbral "mayor es peor".
  const tono = clave === "bateria_pct" ? (valor <= 15 ? "excepcion" : valor <= 30 ? "advertencia" : "ok") : n;
  return (
    <div className={s.medidor}>
      <div className={s.medidorFila}>
        <span className={s.medidorEtiqueta}>{d.etiqueta.replace(/^Uso de /, "")}</span>
        <span className={`${s.medidorValor} num`} data-nivel={tono}>
          {d.formato(valor)}
          {clave === "bateria_pct" && host.lecturas.bateria_cargando >= 0.5 && <span className={s.cargando}> · cargando</span>}
        </span>
      </div>
      <div
        className={s.pista}
        role="meter"
        aria-label={d.etiqueta}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(valor)}
      >
        <span className={s.relleno} data-nivel={tono} style={{ width: `${ancho}%` }} />
        {u && u.advertencia <= 100 && <span className={s.marca} data-tipo="advertencia" style={{ left: `${u.advertencia}%` }} />}
        {u && u.excepcion <= 100 && <span className={s.marca} data-tipo="excepcion" style={{ left: `${u.excepcion}%` }} />}
      </div>
    </div>
  );
}
