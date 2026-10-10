/** Fuerza estimada 1–4 de una contraseña (solo orientativa: la regla real la aplica la API). */
export function fuerzaPassword(p: string): number {
  if (!p) return 0;
  let puntos = 0;
  if (p.length >= 12) puntos++;
  if (p.length >= 16) puntos++;
  if (/[a-z]/.test(p) && /[A-Z]/.test(p)) puntos++;
  if (/\d/.test(p) && /[^A-Za-z0-9]/.test(p)) puntos++;
  return Math.max(1, Math.min(4, puntos));
}

export const ETIQUETA_FUERZA = ["", "Débil", "Aceptable", "Buena", "Fuerte"];
