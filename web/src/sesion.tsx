import { useCallback, useMemo, useState, type ReactNode } from "react";
import type { Sesion } from "./api/auth";
import { Contexto } from "./sesionContexto";

// Sesión en sessionStorage: se borra al cerrar la pestaña/app. El JWT dura 8 h y la API lo revoca
// si cambian contraseña, MFA o rol.
const CLAVE = "syspulse.sesion";

function leer(): Sesion | null {
  try {
    const crudo = sessionStorage.getItem(CLAVE);
    if (!crudo) return null;
    const s = JSON.parse(crudo) as Sesion;
    return new Date(s.expira) > new Date() ? s : null;
  } catch {
    return null;
  }
}

export function ProveedorSesion({ children }: { children: ReactNode }) {
  const [sesion, setSesion] = useState<Sesion | null>(leer);

  const iniciar = useCallback((s: Sesion) => {
    try {
      sessionStorage.setItem(CLAVE, JSON.stringify(s));
    } catch {
      // Sin almacenamiento: la sesión vive solo en memoria.
    }
    setSesion(s);
  }, []);

  const cerrar = useCallback(() => {
    try {
      sessionStorage.removeItem(CLAVE);
    } catch {
      // ignorado
    }
    setSesion(null);
  }, []);

  const valor = useMemo(() => ({ sesion, usuario: sesion?.usuario ?? null, iniciar, cerrar }), [sesion, iniciar, cerrar]);
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}
