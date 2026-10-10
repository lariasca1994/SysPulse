import type { Sla } from "../../api/incidentes";

/** "en 25 min", "hace 3 h", con días cuando corresponde. */
export function relativo(iso: string, ahora: number): string {
  const min = Math.round((new Date(iso).getTime() - ahora) / 60_000);
  const abs = Math.abs(min);
  const t = abs < 60
    ? `${abs} min`
    : abs < 1440
      ? `${Math.floor(abs / 60)} h ${abs % 60 ? `${abs % 60} min` : ""}`.trim()
      : `${Math.floor(abs / 1440)} d ${Math.floor((abs % 1440) / 60)} h`;
  return min >= 0 ? `en ${t}` : `hace ${t}`;
}

export function fechaHora(iso: string): string {
  return new Date(iso).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
}

export function tonoSla(sla: Sla): "ok" | "advertencia" | "excepcion" | "cumplido" {
  if (sla.detenido) return sla.cumplido ? "cumplido" : sla.porcentaje >= 100 ? "excepcion" : "ok";
  return sla.porcentaje >= 100 ? "excepcion" : sla.porcentaje >= 75 ? "advertencia" : "ok";
}

/** Para el historial (siempre en pasado): una marca del servidor apenas por delante del reloj local se ve como "hace un momento". */
export function hace(iso: string, ahora: number): string {
  return ahora - new Date(iso).getTime() < 60_000 ? "hace un momento" : relativo(iso, ahora);
}
