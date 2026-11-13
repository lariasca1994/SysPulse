import s from "./Candado.module.css";

export type EstadoCandado = "normal" | "verificando" | "abierto" | "error";

/** Candado pequeño sobre el título del paso del código: con el código correcto el arco
 *  sube y se abre; con uno equivocado se sacude. Decorativo: el resultado siempre va
 *  también en texto. Cada movimiento dura 150 ms como máximo. */
export function Candado({ estado }: { estado: EstadoCandado }) {
  return (
    <div className={s.candado} data-estado={estado} aria-hidden="true">
      <svg viewBox="0 0 48 48">
        <path className={s.arco} d="M15 22v-7a9 9 0 0 1 18 0v7" />
        <rect className={s.cuerpo} x="9" y="21" width="30" height="21" rx="6" />
        <circle className={s.ojo} cx="24" cy="31.5" r="3.4" />
      </svg>
    </div>
  );
}
