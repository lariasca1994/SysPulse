import { get, post } from "./cliente";

export type EstadoIncidente = "Nuevo" | "Asignado" | "EnProgreso" | "EnEspera" | "Resuelto" | "Cerrado" | "Reabierto";
export type Prioridad = "P1" | "P2" | "P3" | "P4" | "P5";
export type Accion =
  | "Tomar" | "Asignar" | "Iniciar" | "Pausar" | "Reanudar" | "Escalar"
  | "CambiarPrioridad" | "Resolver" | "Cerrar" | "Reabrir" | "Comentar";

export interface Persona {
  id: string;
  nombre: string;
  rol: string;
}

export interface Sla {
  porcentaje: number;
  vence: string;
  cumplido: boolean;
  /** Ya no corre: respondido/resuelto, o pausado (En espera). */
  detenido: boolean;
  minutosConsumidos: number;
}

export interface EntradaHistorial {
  ts: string;
  tipo: "estado" | "escalamiento" | "prioridad" | "asignacion" | "comentario" | "sistema" | "sla";
  usuario: string | null;
  de: string | null;
  a: string | null;
  texto: string | null;
}

export interface Incidente {
  id: string;
  codigo: string;
  titulo: string;
  descripcion: string | null;
  origen: "evento" | "manual";
  hostId: string | null;
  hostNombre: string | null;
  metrica: string | null;
  ultimoValor: number | null;
  metricaRecuperada: boolean;
  impacto: string;
  urgencia: string;
  prioridad: Prioridad;
  estado: EstadoIncidente;
  nivel: "N1" | "N2" | "N3";
  asignado: Persona | null;
  creado: string;
  primeraRespuesta: string | null;
  resuelto: string | null;
  cerrado: string | null;
  slaRespuesta: Sla;
  slaResolucion: Sla;
  codigoCierre: string | null;
  resolucion: string | null;
  /** Acciones que el usuario actual puede hacer ahora (las valida el dominio en la API). */
  acciones: Accion[];
  historial: EntradaHistorial[] | null;
}

export interface DatosAccion {
  texto?: string;
  prioridad?: Prioridad;
  usuarioId?: string;
  codigoCierre?: string;
}

export interface NuevoIncidente {
  titulo: string;
  descripcion: string;
  impacto: "Alto" | "Medio" | "Bajo";
  urgencia: "Alta" | "Media" | "Baja";
  hostId?: string;
}

const RUTA: Record<Accion, string> = {
  Tomar: "tomar", Asignar: "asignar", Iniciar: "iniciar", Pausar: "pausar", Reanudar: "reanudar", Escalar: "escalar",
  CambiarPrioridad: "prioridad", Resolver: "resolver", Cerrar: "cerrar", Reabrir: "reabrir", Comentar: "comentar",
};

export const incidentes = {
  listar: (token: string, todos = false) => get<Incidente[]>(`/api/incidentes${todos ? "?todos=true" : ""}`, token),
  obtener: (token: string, id: string) => get<Incidente>(`/api/incidentes/${encodeURIComponent(id)}`, token),
  crear: (token: string, n: NuevoIncidente) => post<Incidente>("/api/incidentes", n, token),
  accion: (token: string, id: string, accion: Accion, datos: DatosAccion = {}) =>
    post<Incidente>(`/api/incidentes/${encodeURIComponent(id)}/${RUTA[accion]}`, datos, token),
  tecnicos: (token: string) => get<Persona[]>("/api/usuarios/tecnicos", token),
};

export const NOMBRE_ESTADO: Record<EstadoIncidente, string> = {
  Nuevo: "Nuevo",
  Asignado: "Asignado",
  EnProgreso: "En progreso",
  EnEspera: "En espera",
  Resuelto: "Resuelto",
  Cerrado: "Cerrado",
  Reabierto: "Reabierto",
};

export const CODIGOS_CIERRE = [
  "Solución definitiva",
  "Solución temporal (workaround)",
  "Se normalizó sin intervención",
  "Falso positivo",
  "Duplicado de otro incidente",
];

export function abierto(i: Incidente): boolean {
  return i.estado !== "Resuelto" && i.estado !== "Cerrado";
}

/** SLA que importa ahora: la respuesta mientras nadie lo ha tomado; luego la resolución. */
export function slaVigente(i: Incidente): { tipo: "respuesta" | "resolucion"; sla: Sla } {
  return i.primeraRespuesta || !abierto(i)
    ? { tipo: "resolucion", sla: i.slaResolucion }
    : { tipo: "respuesta", sla: i.slaRespuesta };
}
