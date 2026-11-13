// Pantalla de mantenimiento a pantalla completa. No reintenta acciones del usuario
// (podría duplicarlas): consulta /health cada 15 s (con cuenta regresiva visible) y
// recarga la página sola cuando la API vuelve. Es una espera tranquila, distinta del
// 404: el monitor flota entre ondas que respiran (el latido que ya tenía), una barra
// va y viene sin porcentaje y las tres promesas van en fichas.

import { useEffect, useState } from "react";
import { CheckCircle2, Monitor } from "lucide-react";
import { API_BASE } from "../api/base";
import { useMantenimiento } from "../api/mantenimiento";
import estilos from "./Mantenimiento.module.css";

const TOTAL = 15;

export function Mantenimiento() {
  const activo = useMantenimiento();
  const [resto, setResto] = useState(TOTAL);
  const [revisando, setRevisando] = useState(false);

  async function revisar() {
    setRevisando(true);
    try {
      const respuesta = await fetch(`${API_BASE}/health`, { cache: "no-store" });
      if (respuesta.ok) {
        window.location.reload();
        return;
      }
    } catch {
      // sigue caída
    }
    setRevisando(false);
    setResto(TOTAL);
  }

  useEffect(() => {
    if (!activo) return undefined;
    document.title = "En mantenimiento · Monitoreo e incidentes TI";
    const reloj = window.setInterval(() => setResto((r) => (r > 0 ? r - 1 : 0)), 1000);
    return () => window.clearInterval(reloj);
  }, [activo]);

  useEffect(() => {
    if (activo && resto === 0 && !revisando) void revisar();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resto, activo]);

  if (!activo) return null;

  return (
    <div className={estilos.fondo}>
      <section className={estilos.caja} role="status" aria-live="polite">
        <div className={estilos.orbe} aria-hidden="true">
          <span /><span /><span />
          <b><Monitor size={26} /><i className={estilos.pulso} /></b>
        </div>
        <p className={estilos.etiqueta}>Servicio en pausa</p>
        <h1 className={estilos.titulo}>Estamos en mantenimiento</h1>
        <p className={estilos.texto}>
          El servicio no está respondiendo en este momento, así que no se pueden consultar equipos, incidentes ni
          salas.
        </p>
        <div className={estilos.barra} role="progressbar" aria-label="Esperando a que el servicio responda"><i /></div>
        <p className={estilos.nota}>Si el servicio estaba dormido, suele volver en menos de un minuto.</p>
        <ul className={estilos.promesas}>
          {[
            "Tus equipos, incidentes y mensajes siguen guardados.",
            "Ninguna acción quedó a medias: si no viste la confirmación, no se guardó.",
            "Esta página se recarga sola cuando el servicio vuelva.",
          ].map((t) => (
            <li key={t}><CheckCircle2 size={18} aria-hidden="true" /><span>{t}</span></li>
          ))}
        </ul>
        <div className={estilos.pie}>
          <span className={estilos.reintento}>
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <circle className={estilos.cuentaFondo} cx="12" cy="12" r="9" />
              <circle className={estilos.cuentaValor} cx="12" cy="12" r="9" pathLength={1} style={{ strokeDashoffset: revisando ? 0 : 1 - resto / TOTAL }} />
            </svg>
            <span>{revisando ? "Comprobando…" : <>Reintento automático en <span className="num">{resto}</span> s</>}</span>
          </span>
          <button type="button" className={estilos.boton} onClick={() => void revisar()} disabled={revisando}>
            Reintentar ahora
          </button>
        </div>
      </section>
    </div>
  );
}
