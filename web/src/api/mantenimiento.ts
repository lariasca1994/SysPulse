// Estado global de "mantenimiento": se activa cuando la API no responde (sin conexión)
// o responde 502/503/504 (Render caído o MongoDB Atlas sin conexión → 503 de la API).
// Lo activa cliente.ts y lo lee <Mantenimiento /> para mostrar su pantalla.

import { useSyncExternalStore } from "react";

let activo = false;
const oyentes = new Set<() => void>();

export function activarMantenimiento(): void {
  if (activo) return;
  activo = true;
  oyentes.forEach((oyente) => oyente());
}

export function useMantenimiento(): boolean {
  return useSyncExternalStore(
    (oyente) => {
      oyentes.add(oyente);
      return () => oyentes.delete(oyente);
    },
    () => activo,
  );
}

export const ESTADOS_MANTENIMIENTO = [502, 503, 504];
