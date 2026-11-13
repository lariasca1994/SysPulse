import { Link, useLocation } from "react-router";
import { AlertTriangle, Home } from "lucide-react";
import { useSesion } from "../sesionContexto";
import s from "./NoEncontrada.module.css";

/**
 * 404 propio de SysPulse: un monitor donde el pulso late y luego se aplana
 * ("Último latido: nunca"). La línea va en cian y el rojo queda solo en el punto
 * final (regla del color). Se muestra ya dibujada: nada pasa de 150 ms.
 */
export default function NoEncontrada() {
  const { pathname } = useLocation();
  const { sesion } = useSesion();

  return (
    <main className={s.pantalla}>
      <div className={s.contenido}>
        <div className={s.monitor} aria-hidden="true">
          <div className={s.cabecera}>
            <span>GET {pathname}</span>
            <b>404</b>
          </div>
          <div className={s.pantallaMonitor}>
            <svg viewBox="0 0 280 64" className={s.ecg}>
              <path d="M0 38H34l7-20 9 36 7-26 5 10H96l7-20 9 36 7-26 5 10H156l5-7 5 7H272" />
              <circle cx="274" cy="38" r="3.2" />
            </svg>
          </div>
          <div className={s.pie}>
            <span>Último latido</span>
            <span>nunca</span>
          </div>
        </div>

        <div className={s.texto}>
          <h1>Esta página no reporta</h1>
          <p>
            Buscamos <code>{pathname}</code> y ningún equipo ni incidente responde en esa dirección.
            {sesion ? " Tu sesión sigue abierta." : ""}
          </p>
          <div className={s.acciones}>
            {sesion ? (
              <>
                <Link to="/incidentes" className={s.primario}><AlertTriangle size={18} aria-hidden="true" /> Ir a Incidentes</Link>
                <Link to="/" className={s.secundario}><Home size={18} aria-hidden="true" /> Volver al inicio</Link>
              </>
            ) : (
              <Link to="/login" className={s.primario}>Iniciar sesión</Link>
            )}
          </div>
        </div>
      </div>
    </main>
  );
}
