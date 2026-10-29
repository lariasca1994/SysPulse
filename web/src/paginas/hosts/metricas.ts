// Catálogo de métricas de los agentes: nombre legible, grupo y formato.
// Las claves vienen de Atlas con '_' (cpu_total, disco_C_pct, red_ping_8_8_8_8_rtt_ms…).

import type { Host } from "../../api/hosts";

export type Grupo = "Procesador" | "Memoria" | "Almacenamiento" | "Red" | "Batería y temperatura" | "Sistema";
export const GRUPOS: Grupo[] = ["Procesador", "Memoria", "Almacenamiento", "Red", "Batería y temperatura", "Sistema"];

export interface Descripcion {
  etiqueta: string;
  grupo: Grupo;
  formato: (v: number) => string;
  /** Las métricas en % se dibujan como barra. */
  porcentaje?: boolean;
}

const num = (v: number, dec = 0) => v.toLocaleString("es-CO", { maximumFractionDigits: dec, minimumFractionDigits: 0 });
export const pct = (v: number) => `${num(v)}%`;
const mb = (v: number) => (v >= 1024 ? `${num(v / 1024, 1)} GB` : `${num(v)} MB`);
const gb = (v: number) => `${num(v, v < 10 ? 1 : 0)} GB`;
const mbs = (v: number) => (v < 1 ? `${num(v * 1024)} KB/s` : `${num(v, 1)} MB/s`);
const ms = (v: number) => `${num(v)} ms`;
const siNo = (v: number) => (v >= 0.5 ? "Sí" : "No");
const entero = (v: number) => num(v);

export function duracion(horas: number): string {
  const min = Math.round(horas * 60);
  if (min < 60) return `${min} min`;
  const d = Math.floor(min / 1440);
  const h = Math.floor((min % 1440) / 60);
  const m = min % 60;
  return d > 0 ? `${d} d ${h} h` : `${h} h ${m} min`;
}

const TERMICO = ["Normal", "Leve", "Moderado", "Grave", "Crítico", "Emergencia", "Apagado"];
const TIPO_RED = ["Sin red", "Wi-Fi", "Datos móviles", "Ethernet", "Otra"];

const FIJAS: Record<string, Descripcion> = {
  cpu_total: { etiqueta: "Uso de CPU", grupo: "Procesador", formato: pct, porcentaje: true },
  cpu_cola: { etiqueta: "Cola del procesador", grupo: "Procesador", formato: (v) => `${entero(v)} hilos` },

  ram_pct: { etiqueta: "Uso de RAM", grupo: "Memoria", formato: pct, porcentaje: true },
  ram_usada_mb: { etiqueta: "RAM usada", grupo: "Memoria", formato: mb },
  ram_disponible_mb: { etiqueta: "RAM disponible", grupo: "Memoria", formato: mb },
  ram_paginas_seg: { etiqueta: "Páginas por segundo", grupo: "Memoria", formato: entero },
  ram_baja: { etiqueta: "Memoria baja", grupo: "Memoria", formato: siNo },
  paginacion_pct: { etiqueta: "Uso del archivo de paginación", grupo: "Memoria", formato: pct, porcentaje: true },
  paginacion_usada_mb: { etiqueta: "Paginación usada", grupo: "Memoria", formato: mb },

  almacenamiento_pct: { etiqueta: "Almacenamiento usado", grupo: "Almacenamiento", formato: pct, porcentaje: true },
  almacenamiento_libre_gb: { etiqueta: "Almacenamiento libre", grupo: "Almacenamiento", formato: gb },
  disco_activo_pct: { etiqueta: "Actividad de disco", grupo: "Almacenamiento", formato: pct, porcentaje: true },
  disco_lectura_mbs: { etiqueta: "Lectura de disco", grupo: "Almacenamiento", formato: mbs },
  disco_escritura_mbs: { etiqueta: "Escritura de disco", grupo: "Almacenamiento", formato: mbs },
  disco_cola: { etiqueta: "Cola de disco", grupo: "Almacenamiento", formato: (v) => `${entero(v)} ops` },

  red_conectada: { etiqueta: "Conectado a la red", grupo: "Red", formato: siNo },
  red_tipo: { etiqueta: "Tipo de conexión", grupo: "Red", formato: (v) => TIPO_RED[v] ?? "Otra" },
  red_senal_dbm: { etiqueta: "Intensidad de señal", grupo: "Red", formato: (v) => `${entero(v)} dBm` },
  red_bajada_estimada_mbps: { etiqueta: "Bajada estimada", grupo: "Red", formato: (v) => `${num(v)} Mbps` },
  red_subida_estimada_mbps: { etiqueta: "Subida estimada", grupo: "Red", formato: (v) => `${num(v)} Mbps` },
  red_entrada_mbs: { etiqueta: "Tráfico de entrada", grupo: "Red", formato: mbs },
  red_salida_mbs: { etiqueta: "Tráfico de salida", grupo: "Red", formato: mbs },
  red_wifi_bajada_mbs: { etiqueta: "Wi-Fi: bajada", grupo: "Red", formato: mbs },
  red_wifi_subida_mbs: { etiqueta: "Wi-Fi: subida", grupo: "Red", formato: mbs },
  red_wifi_bajada_mb: { etiqueta: "Wi-Fi: consumo bajada", grupo: "Red", formato: mb },
  red_wifi_subida_mb: { etiqueta: "Wi-Fi: consumo subida", grupo: "Red", formato: mb },
  red_utp_bajada_mbs: { etiqueta: "UTP: bajada", grupo: "Red", formato: mbs },
  red_utp_subida_mbs: { etiqueta: "UTP: subida", grupo: "Red", formato: mbs },
  red_utp_bajada_mb: { etiqueta: "UTP: consumo bajada", grupo: "Red", formato: mb },
  red_utp_subida_mb: { etiqueta: "UTP: consumo subida", grupo: "Red", formato: mb },
  red_movil_bajada_mbs: { etiqueta: "Datos móviles: bajada", grupo: "Red", formato: mbs },
  red_movil_subida_mbs: { etiqueta: "Datos móviles: subida", grupo: "Red", formato: mbs },
  red_movil_bajada_mb: { etiqueta: "Datos móviles: consumo bajada", grupo: "Red", formato: mb },
  red_movil_subida_mb: { etiqueta: "Datos móviles: consumo subida", grupo: "Red", formato: mb },
  red_errores: { etiqueta: "Errores de red", grupo: "Red", formato: entero },
  red_descartes: { etiqueta: "Paquetes descartados", grupo: "Red", formato: entero },
  red_interfaces_activas: { etiqueta: "Interfaces activas", grupo: "Red", formato: entero },
  red_tcp_establecidas: { etiqueta: "Conexiones TCP", grupo: "Red", formato: entero },
  red_puertos_escucha: { etiqueta: "Puertos en escucha", grupo: "Red", formato: entero },
  red_ping_perdida_pct: { etiqueta: "Pérdida de paquetes (promedio)", grupo: "Red", formato: pct },

  bateria_pct: { etiqueta: "Batería", grupo: "Batería y temperatura", formato: pct, porcentaje: true },
  bateria_cargando: { etiqueta: "Cargando", grupo: "Batería y temperatura", formato: siNo },
  bateria_baja_sin_cargar: { etiqueta: "Batería baja sin cargar", grupo: "Batería y temperatura", formato: (v) => (v >= 2 ? "Crítica" : v >= 1 ? "Sí" : "No") },
  bateria_temp_c: { etiqueta: "Temperatura de batería", grupo: "Batería y temperatura", formato: (v) => `${num(v, 1)} °C` },
  bateria_voltaje_v: { etiqueta: "Voltaje", grupo: "Batería y temperatura", formato: (v) => `${num(v, 2)} V` },
  bateria_salud_ok: { etiqueta: "Salud de la batería", grupo: "Batería y temperatura", formato: (v) => (v >= 0.5 ? "Buena" : "Revisar") },
  termico_estado: { etiqueta: "Estado térmico", grupo: "Batería y temperatura", formato: (v) => TERMICO[v] ?? `Nivel ${v}` },

  sistema_uptime_h: { etiqueta: "Tiempo encendido", grupo: "Sistema", formato: duracion },
  sistema_procesos: { etiqueta: "Procesos", grupo: "Sistema", formato: entero },
  sistema_hilos: { etiqueta: "Hilos", grupo: "Sistema", formato: entero },
  sistema_reinicio_pendiente: { etiqueta: "Reinicio pendiente", grupo: "Sistema", formato: siNo },
  sistema_reinicios_inesperados_24h: { etiqueta: "Reinicios inesperados (24 h)", grupo: "Sistema", formato: entero },
  eventlog_errores_5m: { etiqueta: "Errores en el visor de eventos (5 min)", grupo: "Sistema", formato: entero },
};

/** "8_8_8_8" → "8.8.8.8"; "www_google_com" → "www.google.com". */
const destino = (s: string) => s.replace(/_/g, ".");

export function describir(clave: string): Descripcion {
  const fija = FIJAS[clave];
  if (fija) return fija;

  let m = /^disco_([A-Za-z])_(pct|libre_gb)$/.exec(clave);
  if (m) {
    return m[2] === "pct"
      ? { etiqueta: `Disco ${m[1]}: usado`, grupo: "Almacenamiento", formato: pct, porcentaje: true }
      : { etiqueta: `Disco ${m[1]}: libre`, grupo: "Almacenamiento", formato: gb };
  }
  m = /^red_ping_(.+)_(perdida_pct|rtt_ms|jitter_ms)$/.exec(clave);
  if (m) {
    const que = { perdida_pct: "pérdida", rtt_ms: "latencia", jitter_ms: "jitter" }[m[2]];
    return { etiqueta: `Ping ${destino(m[1])}: ${que}`, grupo: "Red", formato: m[2] === "perdida_pct" ? pct : ms };
  }
  m = /^red_tcp_(.+)_(\d+)_(ms|ok)$/.exec(clave);
  if (m) {
    return m[3] === "ms"
      ? { etiqueta: `TCP ${destino(m[1])}:${m[2]}: tiempo`, grupo: "Red", formato: ms }
      : { etiqueta: `TCP ${destino(m[1])}:${m[2]}: responde`, grupo: "Red", formato: siNo };
  }
  m = /^red_dns_(.+)_ms$/.exec(clave);
  if (m) return { etiqueta: `DNS ${destino(m[1])}`, grupo: "Red", formato: ms };

  return { etiqueta: clave.replace(/_/g, " "), grupo: "Sistema", formato: (v) => num(v, 2) };
}

export type Clase = "escritorio" | "portatil" | "movil";

export function claseHost(h: Host): Clase {
  if (h.tipo === "android") return "movil";
  return h.lecturas.bateria_pct !== undefined ? "portatil" : "escritorio";
}

/** "Lenovo Tab M11 · Android" si el agente informa el modelo; si no, el tipo genérico ("Android · celular"). */
export function descripcionHost(h: Host): string {
  if (!h.modelo) return NOMBRE_CLASE[claseHost(h)];
  return `${h.modelo} · ${h.tipo === "android" ? "Android" : "Windows"}`;
}

export const NOMBRE_CLASE: Record<Clase, string> = {
  escritorio: "Windows · escritorio",
  portatil: "Windows · portátil o tablet",
  movil: "Android · celular",
};

/** Severidad de un valor frente a su umbral (para colorear barras y valores). */
export function nivel(h: Host, clave: string): "ok" | "advertencia" | "excepcion" | "neutro" {
  const u = h.umbrales[clave];
  const v = h.lecturas[clave];
  if (!u || v === undefined) return "neutro";
  return v >= u.excepcion ? "excepcion" : v >= u.advertencia ? "advertencia" : "ok";
}

/** Métricas destacadas de la tarjeta, según el tipo de equipo. */
export function destacadas(h: Host): string[] {
  const l = h.lecturas;
  if (h.tipo === "android") {
    return ["bateria_pct", "almacenamiento_pct", "ram_pct"].filter((c) => l[c] !== undefined);
  }
  // Disco: la unidad más llena.
  const discos = Object.keys(l).filter((c) => /^disco_[A-Za-z]_pct$/.test(c)).sort((a, b) => l[b] - l[a]);
  return ["cpu_total", "ram_pct", discos[0], "bateria_pct"].filter((c): c is string => !!c && l[c] !== undefined);
}

const relativo = new Intl.RelativeTimeFormat("es", { numeric: "auto" });

export function hace(iso: string | null, ahora = Date.now()): string {
  if (!iso) return "nunca";
  const seg = Math.round((new Date(iso).getTime() - ahora) / 1000);
  const abs = Math.abs(seg);
  if (abs < 45) return "hace un momento";
  if (abs < 3600) return relativo.format(Math.round(seg / 60), "minute");
  if (abs < 86400) return relativo.format(Math.round(seg / 3600), "hour");
  return relativo.format(Math.round(seg / 86400), "day");
}

