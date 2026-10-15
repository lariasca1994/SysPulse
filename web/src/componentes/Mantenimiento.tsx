// Pantalla de mantenimiento a pantalla completa. No reintenta acciones del usuario
// (podría duplicarlas): consulta /health cada 15 s y recarga la página sola cuando
// la API vuelve.

import { useEffect } from "react";
import { API_BASE } from "../api/base";
import { useMantenimiento } from "../api/mantenimiento";
import estilos from "./Mantenimiento.module.css";

export function Mantenimiento() {
  const activo = useMantenimiento();

  useEffect(() => {
    if (!activo) return undefined;
    document.title = "En mantenimiento · SysPulse";
    const intervalo = window.setInterval(async () => {
      try {
        const respuesta = await fetch(`${API_BASE}/health`, { cache: "no-store" });
        if (respuesta.ok) window.location.reload();
      } catch {
        // sigue caída
      }
    }, 15000);
    return () => window.clearInterval(intervalo);
  }, [activo]);

  if (!activo) return null;

  return (
    <div className={estilos.fondo}>
      <section className={estilos.tarjeta} role="status" aria-live="polite">
        <div className={estilos.marca}>
          <span className={estilos.pulso} aria-hidden="true" />
          SysPulse
        </div>
        <p className={estilos.etiqueta}>Servicio en pausa</p>
        <h1 className={estilos.titulo}>Estamos en mantenimiento</h1>
        <p className={estilos.texto}>
          El servicio no está respondiendo en este momento, así que no se pueden consultar equipos, incidentes ni
          salas.
        </p>
        <ul className={estilos.lista}>
          <li>Tus equipos, incidentes y mensajes siguen guardados.</li>
          <li>Ninguna acción quedó a medias: si no viste la confirmación, no se guardó.</li>
          <li>Esta página se recarga sola cuando el servicio vuelva.</li>
        </ul>
        <button type="button" className={estilos.boton} onClick={() => window.location.reload()}>
          Reintentar ahora
        </button>
      </section>
    </div>
  );
}
