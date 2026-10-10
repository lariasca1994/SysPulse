import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useSesion } from "../sesionContexto";
import { esFalla } from "./cliente";

interface Estado<T> {
  datos: T | null;
  error: string | null;
  cargando: boolean;
  /** Momento de la última respuesta correcta. */
  actualizado: Date | null;
}

/**
 * Consulta autenticada con refresco periódico. Si la API responde 401 (sesión vencida o revocada),
 * cierra la sesión y vuelve al login. Al refrescar se conservan los datos anteriores: no hay parpadeos.
 */
export function useConsulta<T>(consulta: (token: string) => Promise<T>, claves: unknown[], refrescoMs?: number) {
  const { sesion, cerrar } = useSesion();
  const navegar = useNavigate();
  const [estado, setEstado] = useState<Estado<T>>({ datos: null, error: null, cargando: true, actualizado: null });
  const consultaRef = useRef(consulta);
  consultaRef.current = consulta;
  const token = sesion?.token;

  const cargar = useCallback(async () => {
    if (!token) return;
    setEstado((e) => ({ ...e, cargando: true }));
    try {
      const datos = await consultaRef.current(token);
      setEstado({ datos, error: null, cargando: false, actualizado: new Date() });
    } catch (err) {
      if (esFalla(err) && err.detalle.estado === 401) {
        cerrar();
        navegar("/login", { replace: true });
        return;
      }
      const mensaje = esFalla(err) ? err.detalle.mensaje : "Ocurrió un error inesperado.";
      setEstado((e) => ({ ...e, error: mensaje, cargando: false }));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, cerrar, navegar, ...claves]);

  useEffect(() => {
    void cargar();
    if (!refrescoMs) return;
    const id = window.setInterval(() => {
      if (document.visibilityState === "visible") void cargar();
    }, refrescoMs);
    return () => window.clearInterval(id);
  }, [cargar, refrescoMs]);

  return { ...estado, recargar: cargar };
}
