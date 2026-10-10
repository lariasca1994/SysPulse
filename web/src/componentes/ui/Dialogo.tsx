import { useEffect, useRef, type ReactNode } from "react";
import { X } from "lucide-react";
import s from "./Dialogo.module.css";

/**
 * Ventana modal accesible con el <dialog> nativo: atrapa el foco, se cierra con Esc y devuelve el foco al cerrar.
 */
export function Dialogo({ abierto, titulo, descripcion, alCerrar, children }: {
  abierto: boolean;
  titulo: string;
  descripcion?: string;
  alCerrar: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (abierto && !d.open) d.showModal();
    if (!abierto && d.open) d.close();
  }, [abierto]);

  return (
    <dialog
      ref={ref}
      className={s.dialogo}
      aria-labelledby="dialogo-titulo"
      onClose={alCerrar}
      onClick={(e) => {
        // Clic en el fondo (fuera de la caja) cierra.
        if (e.target === ref.current) alCerrar();
      }}
    >
      <div className={s.caja}>
        <header className={s.cabecera}>
          <div>
            <h2 id="dialogo-titulo">{titulo}</h2>
            {descripcion && <p>{descripcion}</p>}
          </div>
          <button type="button" className={s.cerrar} onClick={alCerrar} aria-label="Cerrar">
            <X size={18} aria-hidden="true" />
          </button>
        </header>
        {abierto && children}
      </div>
    </dialog>
  );
}
