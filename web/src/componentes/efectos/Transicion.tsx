import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { useLocation, useOutlet } from "react-router";
import s from "./Transicion.module.css";

const reducir = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Transición en cada cambio de pantalla, con el límite de SysPulse (transiciones
 * de 150 ms como máximo, sección 7 de la especificación): la pantalla que sale se
 * desvanece y se corre (120 ms) y la nueva entra completa desde el lado hacia donde
 * se navega (150 ms), sin escalonado. Reemplaza al <Outlet /> de un layout y,
 * mientras sale, sigue mostrando la pantalla anterior.
 *
 * - orden: rutas en el orden del menú (define hacia dónde entra la nueva).
 * - envolver: envoltura de cada pantalla (por ejemplo, el límite de errores), que
 *   se reinicia en cada cambio.
 */
export function TransicionRutas({ orden = [], envolver, className = "" }: { orden?: string[]; envolver?: (contenido: ReactNode) => ReactNode; className?: string }) {
  const { pathname } = useLocation();
  const outlet = useOutlet();
  const anterior = useRef<ReactNode>(outlet);
  const [estado, setEstado] = useState({ clave: pathname, fase: "entra" as "entra" | "sale", dir: 1 });

  // Mientras la ruta no cambia, se guarda la pantalla actual (la que saldrá).
  if (estado.clave === pathname) anterior.current = outlet;

  useEffect(() => {
    if (pathname === estado.clave) return undefined;
    const indice = (ruta: string) => {
      const i = orden.findIndex((r) => ruta === r || ruta.startsWith(`${r}/`));
      return i < 0 ? orden.length : i;
    };
    const dir = indice(pathname) >= indice(estado.clave) ? 1 : -1;
    if (reducir()) {
      setEstado({ clave: pathname, fase: "entra", dir });
      return undefined;
    }
    setEstado((e) => ({ ...e, fase: "sale", dir }));
    const t = window.setTimeout(() => setEstado({ clave: pathname, fase: "entra", dir }), 120);
    return () => window.clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pathname]);

  const contenido = estado.clave === pathname ? outlet : anterior.current;
  return (
    <div key={estado.clave} className={`${estado.fase === "sale" ? s.sale : s.entra} ${className}`} style={{ "--dir": estado.dir } as CSSProperties}>
      {envolver ? envolver(contenido) : contenido}
    </div>
  );
}
