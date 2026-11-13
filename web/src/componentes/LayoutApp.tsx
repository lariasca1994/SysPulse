import { useCallback, useEffect, useLayoutEffect, useRef, useState, type MouseEvent } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router";
import { Activity, AlertTriangle, BarChart3, ChevronsLeft, Download, LogOut, MessagesSquare, Monitor, MoreHorizontal, Settings } from "lucide-react";
import { useInstalacion } from "../pwa";
import { useSesion } from "../sesionContexto";
import { BotonTema } from "./BotonTema";
import { LimiteError } from "./LimiteError";
import { TransicionRutas } from "./efectos/Transicion";
import s from "./LayoutApp.module.css";

import { API_BASE as API_URL } from "../api/base";

const NAVEGACION = [
  { ruta: "/turno", icono: Activity, texto: "Mi turno" },
  { ruta: "/incidentes", icono: AlertTriangle, texto: "Incidentes" },
  { ruta: "/hosts", icono: Monitor, texto: "Salud de hosts" },
  { ruta: "/salas", icono: MessagesSquare, texto: "Salas" },
  { ruta: "/analitica", icono: BarChart3, texto: "Analítica" },
];
const ADMIN = { ruta: "/admin", icono: Settings, texto: "Administración" };
/** Orden del menú: define hacia dónde entra la pantalla nueva. */
const ORDEN = [...NAVEGACION.map((n) => n.ruta), "/admin", "/perfil"];

const ROL: Record<string, string> = { N1: "N1", N2: "N2", N3: "N3", gestor: "Gestor", admin: "Admin" };

const reducir = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const leerCompacto = () => { try { return localStorage.getItem("syspulse-menu") === "compacto"; } catch { return false; } };

/**
 * Estructura de las pantallas internas: menú lateral (barra de vidrio inferior en
 * móvil), cabecera y contenido. El menú se compacta a solo íconos (preferencia
 * guardada en este equipo; bajo 1100 px siempre va compacto), la opción activa
 * lleva un haz de luz cian que se desliza al cambiar de sección, un fondo suave
 * sigue al ratón y, compacto, una etiqueta muestra el nombre. Todo en 150 ms o
 * menos (regla 8 de la especificación).
 */
export function LayoutApp() {
  const { usuario, cerrar } = useSesion();
  const navegar = useNavigate();
  const { modo, instalar } = useInstalacion();
  const { pathname } = useLocation();
  const [despertando, setDespertando] = useState(false);
  const [compacto, setCompacto] = useState(leerCompacto);
  const lista = useRef<HTMLUListElement>(null);
  const indicador = useRef<HTMLLIElement>(null);
  const sombra = useRef<HTMLLIElement>(null);
  const etiqueta = useRef<HTMLSpanElement>(null);
  const hoja = useRef<HTMLDialogElement>(null);

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
  const esAdmin = usuario?.rol === "admin";
  const opciones = esAdmin ? [...NAVEGACION, ADMIN] : NAVEGACION;
  const enMas = esAdmin && pathname.startsWith("/admin");

  // El indicador (haz de luz) se ubica sobre la opción activa; en móvil, Administración va en "Más".
  const ubicar = (capa: HTMLElement | null, op: HTMLElement | null) => {
    if (!capa || !op) return;
    capa.style.transform = `translate(${op.offsetLeft}px, ${op.offsetTop}px)`;
    capa.style.width = `${op.offsetWidth}px`;
    capa.style.height = `${op.offsetHeight}px`;
  };
  const colocar = useCallback((sinAnimacion: boolean) => {
    const ul = lista.current;
    const ind = indicador.current;
    if (!ul || !ind) return;
    let activa = ul.querySelector<HTMLElement>('a[aria-current="page"]');
    if ((!activa || activa.offsetParent === null) && enMas) activa = ul.querySelector<HTMLElement>("[data-mas]");
    const op = activa?.closest("li") ?? null;
    if (!op || activa?.offsetParent === null) { ind.style.opacity = "0"; return; }
    ind.style.opacity = "1";
    if (sinAnimacion) ul.dataset.quieto = "";
    ubicar(ind, op);
    if (sinAnimacion) requestAnimationFrame(() => requestAnimationFrame(() => { delete ul.dataset.quieto; }));
  }, [enMas]);

  useLayoutEffect(() => { colocar(false); }, [pathname, compacto, colocar]);
  useEffect(() => {
    const ro = new ResizeObserver(() => colocar(true));
    if (lista.current) ro.observe(lista.current);
    return () => ro.disconnect();
  }, [colocar]);

  function alternarMenu() {
    const nuevo = !compacto;
    setCompacto(nuevo);
    try { localStorage.setItem("syspulse-menu", nuevo ? "compacto" : "extendido"); } catch { /* sin almacenamiento */ }
  }

  // Fondo que sigue al ratón y etiqueta con el nombre cuando el menú está compacto.
  function sobre(e: MouseEvent) {
    const op = (e.target as HTMLElement).closest("li");
    if (!op || !lista.current?.contains(op) || op.getAttribute("aria-hidden")) return;
    const cap = sombra.current;
    if (cap && !cap.dataset.listo) {
      lista.current.dataset.quieto = "";
      ubicar(cap, op);
      void cap.offsetWidth;
      delete lista.current.dataset.quieto;
      cap.dataset.listo = "";
    }
    ubicar(cap, op);
    const et = etiqueta.current;
    const enlace = op.querySelector("a, button");
    if (et && enlace) {
      const r = op.getBoundingClientRect();
      et.textContent = enlace.getAttribute("title") ?? enlace.textContent ?? "";
      et.style.top = `${r.top + r.height / 2 - et.offsetHeight / 2}px`;
      et.dataset.visible = "";
    }
  }
  function fuera() {
    if (sombra.current) delete sombra.current.dataset.listo;
    if (etiqueta.current) delete etiqueta.current.dataset.visible;
  }

  function cerrarHoja() {
    const h = hoja.current;
    if (!h) return;
    if (reducir()) { h.close(); return; }
    h.dataset.cerrando = "";
    window.setTimeout(() => { delete h.dataset.cerrando; h.close(); }, 120);
  }

  return (
    <div className={s.layout} data-compacto={compacto ? "" : undefined}>
      <aside className={s.nav}>
        <div className={s.marca}>
          <span className={s.pulso} aria-hidden="true" />
          <span className={s.marcaTexto}>Monitoreo e incidentes TI</span>
        </div>
        <nav aria-label="Navegación principal">
          <ul className={s.menu} ref={lista} onMouseOver={sobre} onMouseLeave={fuera}>
            <li className={s.sombra} ref={sombra} aria-hidden="true" />
            <li className={s.indicador} ref={indicador} aria-hidden="true" />
            {opciones.map(({ ruta, icono: Icono, texto }) => (
              <li key={ruta} className={ruta === "/admin" ? s.soloAmplio : undefined}>
                <NavLink to={ruta} className={s.item} title={texto}>
                  <Icono size={18} aria-hidden="true" />
                  <span>{texto}</span>
                </NavLink>
              </li>
            ))}
            {esAdmin && (
              <li className={s.soloMovil}>
                <button type="button" className={s.item} data-mas="" title="Más" aria-haspopup="dialog"
                  data-activo={enMas ? "" : undefined} onClick={() => hoja.current?.showModal()}>
                  <MoreHorizontal size={18} aria-hidden="true" />
                  <span>Más</span>
                </button>
              </li>
            )}
          </ul>
        </nav>
        <button type="button" className={s.alternar} onClick={alternarMenu} aria-expanded={!compacto}
          title={compacto ? "Extender menú" : "Compactar menú"}>
          <ChevronsLeft size={18} aria-hidden="true" />
          <span>{compacto ? "Extender menú" : "Compactar menú"}</span>
        </button>
        <p className={s.version}>Monitoreo · v0.1</p>
      </aside>
      <span className={s.etiqueta} ref={etiqueta} aria-hidden="true" />
      {esAdmin && (
        <dialog className={s.hoja} ref={hoja} aria-label="Más opciones"
          onClick={(e) => { if (e.target === hoja.current) cerrarHoja(); }}
          onCancel={(e) => { e.preventDefault(); cerrarHoja(); }}>
          <Link to="/admin" onClick={() => hoja.current?.close()} aria-current={enMas ? "page" : undefined}>
            <Settings size={20} aria-hidden="true" /> Administración
          </Link>
        </dialog>
      )}

      <div className={s.cuerpo}>
        <header className={s.cabecera}>
          <div className={s.marcaMovil}>
            <span className={s.pulso} aria-hidden="true" />
            Monitoreo TI
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
          <TransicionRutas orden={ORDEN} envolver={(c) => <LimiteError>{c}</LimiteError>} />
        </main>
      </div>
    </div>
  );
}
