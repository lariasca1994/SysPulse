import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { Activity, AlertTriangle, BarChart3, Download, LogOut, MessagesSquare, Monitor, Settings } from "lucide-react";
import { useInstalacion } from "../pwa";
import { useSesion } from "../sesionContexto";
import { BotonTema } from "./BotonTema";
import { LimiteError } from "./LimiteError";
import s from "./LayoutApp.module.css";

import { API_BASE as API_URL } from "../api/base";

const NAVEGACION = [
  { ruta: "/turno", icono: Activity, texto: "Mi turno" },
  { ruta: "/incidentes", icono: AlertTriangle, texto: "Incidentes" },
  { ruta: "/hosts", icono: Monitor, texto: "Salud de hosts" },
  { ruta: "/salas", icono: MessagesSquare, texto: "Salas" },
  { ruta: "/analitica", icono: BarChart3, texto: "Analítica" },
];

const ROL: Record<string, string> = { N1: "N1", N2: "N2", N3: "N3", gestor: "Gestor", admin: "Admin" };

/** Estructura de las pantallas internas: menú lateral (barra inferior en móvil), cabecera y contenido. */
export function LayoutApp() {
  const { usuario, cerrar } = useSesion();
  const navegar = useNavigate();
  const { modo, instalar } = useInstalacion();
  const { pathname } = useLocation();
  const [despertando, setDespertando] = useState(false);

  useEffect(() => {
    const controlador = new AbortController();
    // Render gratuito tarda ~1 min en despertar: se avisa si /health no responde en 3 s.
    const aviso = window.setTimeout(() => setDespertando(true), 3000);
    fetch(`${API_URL}/health`, { signal: controlador.signal })
      .then((r) => setDespertando(!r.ok))
      .catch(() => setDespertando(true))
      .finally(() => window.clearTimeout(aviso));
    return () => {
      controlador.abort();
      window.clearTimeout(aviso);
    };
  }, []);

  const iniciales = (usuario?.nombre ?? "?").split(" ").filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();

  return (
    <div className={s.layout}>
      <aside className={s.nav}>
        <div className={s.marca}>
          <span className={s.pulso} aria-hidden="true" />
          <span className={s.marcaTexto}>SysPulse</span>
        </div>
        <nav aria-label="Navegación principal">
          <ul className={s.menu}>
            {(usuario?.rol === "admin" ? [...NAVEGACION, { ruta: "/admin", icono: Settings, texto: "Administración" }] : NAVEGACION).map(({ ruta, icono: Icono, texto }) => (
              <li key={ruta}>
                <NavLink to={ruta} className={s.item} title={texto}>
                  <Icono size={18} aria-hidden="true" />
                  <span>{texto}</span>
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <p className={s.version}>Monitoreo · v0.1</p>
      </aside>

      <div className={s.cuerpo}>
        <header className={s.cabecera}>
          <div className={s.marcaMovil}>
            <span className={s.pulso} aria-hidden="true" />
            SysPulse
          </div>
          <div className={s.acciones}>
            {modo === "boton" && (
              <button type="button" className={s.instalar} onClick={instalar}>
                <Download size={16} aria-hidden="true" /> <span>Instalar app</span>
              </button>
            )}
            {modo === "ios" && <span className={s.pista}>Para instalar: Compartir → Agregar a inicio</span>}
            <BotonTema />
            {usuario && (
              <div className={s.usuario}>
                <Link to="/perfil" className={s.cuenta} title="Mi cuenta" aria-label="Mi cuenta">
                  <span className={s.avatar} aria-hidden="true">{iniciales}</span>
                  <span className={s.datos}>
                    <strong>{usuario.nombre}</strong>
                    <span className={s.rol} data-rol={usuario.rol}>{ROL[usuario.rol] ?? usuario.rol}</span>
                  </span>
                </Link>
                <button
                  type="button"
                  className={s.salir}
                  onClick={() => {
                    cerrar();
                    navegar("/login", { replace: true });
                  }}
                  aria-label="Cerrar sesión"
                  title="Cerrar sesión"
                >
                  <LogOut size={18} aria-hidden="true" />
                </button>
              </div>
            )}
          </div>
        </header>

        {despertando && (
          <p className={s.aviso} role="status">
            Iniciando servicio… el servidor gratuito tarda hasta un minuto en despertar.
          </p>
        )}

        <main className={s.contenido}>
          <LimiteError key={pathname}>
            <Outlet />
          </LimiteError>
        </main>
      </div>
    </div>
  );
}
