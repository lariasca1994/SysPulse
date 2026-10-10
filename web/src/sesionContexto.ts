import { createContext, useContext } from "react";
import type { Perfil, Sesion } from "./api/auth";

export interface ContextoSesion {
  sesion: Sesion | null;
  usuario: Perfil | null;
  iniciar: (s: Sesion) => void;
  cerrar: () => void;
}

export const Contexto = createContext<ContextoSesion | null>(null);

export function useSesion(): ContextoSesion {
  const c = useContext(Contexto);
  if (!c) throw new Error("useSesion fuera de ProveedorSesion");
  return c;
}
