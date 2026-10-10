import { get } from "./cliente";

export type Conexion = "en-linea" | "retrasado" | "sin-reporte";
export type Salud = "ok" | "advertencia" | "excepcion" | "desconocida";

export interface Umbral {
  advertencia: number;
  excepcion: number;
}

export interface AlertaHost {
  metrica: string;
  valor: number;
  umbral: number;
  severidad: "advertencia" | "excepcion";
}

/** Claves de lecturas y umbrales con '_' (cpu_total, disco_C_pct…), tal como las guarda el agente en Atlas. */
export interface Host {
  id: string;
  nombre: string;
  tipo: "windows" | "android" | string;
  versionAgente: string | null;
  ultimoArranque: string | null;
  ultimoReporte: string | null;
  conexion: Conexion;
  salud: Salud;
  lecturas: Record<string, number>;
  umbrales: Record<string, Umbral>;
  alertas: AlertaHost[];
}

/** Punto: [ts en ms, promedio, máximo] de una ventana de 5 min. */
export type Punto = [number, number, number];

export interface Historial {
  desde: string;
  hasta: string;
  series: Record<string, { unidad: string | null; puntos: Punto[] }>;
}

export const hosts = {
  listar: (token: string) => get<Host[]>("/api/hosts", token),
  obtener: (token: string, id: string) => get<Host>(`/api/hosts/${encodeURIComponent(id)}`, token),
  historial: (token: string, id: string, horas: number, metricas: string[]) =>
    get<Historial>(`/api/hosts/${encodeURIComponent(id)}/metricas?horas=${horas}&m=${metricas.join(",")}`, token),
};
