import type { ReactNode } from "react";
import { Activity, BellRing, ShieldCheck } from "lucide-react";
import { BotonTema } from "../../componentes/BotonTema";
import s from "./acceso.module.css";

const ANIO = new Date().getFullYear();

/** Estructura común de inicio de sesión, registro y verificaciones. */
export function LayoutAcceso({ paso, titulo, subtitulo, children }: { paso?: string; titulo: string; subtitulo?: ReactNode; children: ReactNode }) {
  return (
    <div className={s.pantalla}>
      <div className={s.tema}>
        <BotonTema />
      </div>
      <aside className={s.marca}>
        <div className={s.logo}>
          <span className={s.punto} aria-hidden="true" />
          SysPulse
        </div>

        <div className={s.mensaje}>
          <h2 className={s.titular}>
            Operaciones de TI, <span>en un solo pulso</span>.
          </h2>
          <p className={s.bajada}>
            Monitoreo de tus equipos, incidentes con SLA y coordinación del equipo en tiempo real, con prácticas ITIL v4.
          </p>
          <ul className={s.puntos}>
            <li>
              <span className={s.icono}><Activity size={18} aria-hidden="true" /></span>
              <span>Monitoreo continuo<small>Todos tus equipos, Windows y Android, reportando cada minuto.</small></span>
            </li>
            <li>
              <span className={s.icono}><BellRing size={18} aria-hidden="true" /></span>
              <span>Avisos por nivel<small>Telegram y correo según N1, N2, N3 o gestor.</small></span>
            </li>
            <li>
              <span className={s.icono}><ShieldCheck size={18} aria-hidden="true" /></span>
              <span>Acceso protegido<small>Contraseña robusta y segundo factor obligatorio.</small></span>
            </li>
          </ul>
        </div>

        <p className={s.pie}>© {ANIO} SysPulse · Plataforma de operaciones TI</p>

        <svg className={s.linea} viewBox="0 0 900 200" fill="none" aria-hidden="true">
          <path d="M0 120 H260 L300 40 L350 180 L395 80 L425 120 H900" stroke="var(--accent)" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </aside>

      <main className={s.lado}>
        <section className={s.tarjeta} aria-labelledby="titulo-acceso">
          <header className={s.encabezado}>
            {paso && <p className={s.paso}>{paso}</p>}
            <h1 id="titulo-acceso" className={s.tituloForm}>{titulo}</h1>
            {subtitulo && <p className={s.subtitulo}>{subtitulo}</p>}
          </header>
          {children}
        </section>
      </main>
    </div>
  );
}
