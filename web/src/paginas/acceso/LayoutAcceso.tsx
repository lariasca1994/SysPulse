import { Suspense, type ReactNode } from "react";
import { Activity, BellRing, ShieldCheck } from "lucide-react";
import { BotonTema } from "../../componentes/BotonTema";
import { TransicionRutas } from "../../componentes/efectos/Transicion";
import { Candado, type EstadoCandado } from "./Candado";
import s from "./acceso.module.css";

const ANIO = new Date().getFullYear();

/** Orden de los pasos del acceso: define hacia dónde entra la tarjeta nueva. */
const ORDEN_ACCESO = ["/login", "/mfa", "/mfa/configurar", "/mfa/recuperar", "/olvide", "/restablecer", "/cambiar-password", "/registro", "/verificar-correo"];

/**
 * Portada común de inicio de sesión, registro y verificaciones: el panel de marca
 * queda fijo y solo cambian las tarjetas de cada paso, con la transición de la
 * plataforma (150 ms como máximo). El diseño es el de siempre.
 */
export function MarcoAcceso() {
  return (
    <div className={s.pantalla}>
      <div className={s.tema}>
        <BotonTema />
      </div>
      <aside className={s.marca}>
        <div className={s.logo}>
          <span className={s.punto} aria-hidden="true" />
          Monitoreo e incidentes TI
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

        <p className={s.pie}>© {ANIO} Monitoreo e incidentes TI · Plataforma de operaciones TI</p>

        <svg className={s.linea} viewBox="0 0 900 200" fill="none" aria-hidden="true">
          <path d="M0 120 H260 L300 40 L350 180 L395 80 L425 120 H900" stroke="var(--accent)" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </aside>

      <main className={s.lado}>
        <TransicionRutas orden={ORDEN_ACCESO} className={s.envoltura} envolver={(c) => <Suspense fallback={null}>{c}</Suspense>} />
      </main>
    </div>
  );
}

/** Tarjeta de un paso del acceso. En el paso del código lleva el candado pequeño sobre el título. */
export function LayoutAcceso({ paso, titulo, subtitulo, children, candado }: { paso?: string; titulo: string; subtitulo?: ReactNode; children: ReactNode; candado?: EstadoCandado }) {
  return (
    <section className={s.tarjeta} aria-labelledby="titulo-acceso">
      <header className={s.encabezado}>
        {candado && <Candado estado={candado} />}
        {paso && <p className={s.paso}>{paso}</p>}
        <h1 id="titulo-acceso" className={s.tituloForm}>{titulo}</h1>
        {subtitulo && <p className={s.subtitulo}>{subtitulo}</p>}
      </header>
      {children}
    </section>
  );
}
