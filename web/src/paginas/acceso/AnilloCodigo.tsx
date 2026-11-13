import { useEffect, useState } from "react";
import s from "./Candado.module.css";

const faltan = () => 30 - (Math.floor(Date.now() / 1000) % 30);

/** Cuánto falta para que la app autenticadora muestre otro código (cambian cada 30 s). */
export function AnilloCodigo() {
  const [resto, setResto] = useState(faltan);
  useEffect(() => {
    const t = window.setInterval(() => setResto(faltan()), 1000);
    return () => window.clearInterval(t);
  }, []);
  return (
    <p className={s.anillo} data-urgente={resto <= 5 ? "" : undefined}>
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <circle className={s.anilloFondo} cx="12" cy="12" r="9" />
        <circle className={s.anilloValor} cx="12" cy="12" r="9" pathLength={1} style={{ strokeDashoffset: 1 - resto / 30 }} />
      </svg>
      <span>El código de tu app cambia en <span className="num">{resto}</span> s</span>
    </p>
  );
}
