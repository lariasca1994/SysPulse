import { get } from "./cliente";

export interface IndicadorSla {
  cumplidos: number;
  incumplidos: number;
  porcentaje: number | null;
}

export interface Analitica {
  dias: number;
  desde: string;
  incidentesTotal: number;
  abiertos: number;
  mttaMinutos: number | null;
  mttrMinutos: number | null;
  slaRespuesta: IndicadorSla;
  slaResolucion: IndicadorSla;
  tasaEscalamiento: number | null;
  resueltosEnN1: number | null;
  reaperturas: number;
  porOrigen: Record<string, number>;
  porPrioridad: { prioridad: string; total: number; resolucion: IndicadorSla }[];
  porDia: { dia: string; creados: number; resueltos: number }[];
  eventosTotal: number;
  eventosExcepcion: number;
  eventosAdvertencia: number;
  candidatosProblema: { hostNombre: string; metrica: string; excepciones: number; ultima: string; incidenteAbiertoId: string | null }[];
  hosts: { id: string; nombre: string; tipo: string; disponibilidad: number; cpuPromedio: number | null; cpuMaximo: number | null; ramPromedio: number | null; ramMaximo: number | null; ventanas: number }[];
  cambios: { id: string; tipo: string; titulo: string; version: string | null; estado: string; creado: string }[];
}

export const analitica = {
  obtener: (token: string, dias: number) => get<Analitica>(`/api/analitica?dias=${dias}`, token),
};
