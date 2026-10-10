import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type RefObject } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import { ArrowLeft, ExternalLink, Hash, Lock, MessagesSquare, Send, Undo2, Wifi, WifiOff } from "lucide-react";
import { crearConexion, MAX_TEXTO, salas as apiSalas, vigilarVisibilidad, type Mensaje, type Persona, type Sala } from "../../api/chat";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta } from "../../componentes/ui/Basicos";
import { InsigniaPrioridad } from "../incidentes/Piezas";
import { NOMBRE_ESTADO, type EstadoIncidente, type Prioridad } from "../../api/incidentes";
import { useAhora } from "../hosts/useAhora";
import s from "./salas.module.css";

type EstadoConexion = "conectando" | "conectado" | "reconectando" | "desconectado";

const PLAZO_RETIRAR_MS = 15 * 60_000;
const ROL: Record<string, string> = { N1: "N1", N2: "N2", N3: "N3", gestor: "Gestor", admin: "Admin" };

const iniciales = (n: string | null) => (n ?? "?").split(" ").filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();
const hora = (iso: string) => new Date(iso).toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });

function dia(iso: string): string {
  const f = new Date(iso);
  const hoy = new Date();
  const ayer = new Date(Date.now() - 86_400_000);
  if (f.toDateString() === hoy.toDateString()) return "Hoy";
  if (f.toDateString() === ayer.toDateString()) return "Ayer";
  return f.toLocaleDateString("es-CO", { weekday: "long", day: "numeric", month: "long" });
}

export default function Salas() {
  const { id: salaParam } = useParams();
  const salaId = salaParam ?? "operaciones";
  const navegar = useNavigate();
  const { sesion, usuario } = useSesion();
  const token = sesion?.token ?? "";

  const conexion = useRef<HubConnection | null>(null);
  const salaActual = useRef(salaId);
  useEffect(() => {
    salaActual.current = salaId;
  }, [salaId]);

  const [estado, setEstado] = useState<EstadoConexion>("conectando");
  const [lista, setLista] = useState<Sala[]>([]);
  // Los mensajes se guardan junto con su sala: al cambiar de sala no se muestran los de la anterior.
  const [hilo, setHilo] = useState<{ sala: string; items: Mensaje[] }>({ sala: "", items: [] });
  const mensajes = hilo.sala === salaId ? hilo.items : [];
  const ahora = useAhora();
  const [hayMas, setHayMas] = useState(false);
  const [presentes, setPresentes] = useState<Persona[]>([]);
  const [escribiendo, setEscribiendo] = useState<Record<string, { nombre: string; hasta: number }>>({});
  const [error, setError] = useState<string | null>(null);

  const marcarLeido = useCallback((sala: string, mensajeId: string) => {
    const c = conexion.current;
    if (c?.state === HubConnectionState.Connected) void c.invoke("MarcarLeido", sala, mensajeId).catch(() => undefined);
    setLista((l) => l.map((x) => (x.id === sala ? { ...x, noLeidos: 0 } : x)));
  }, []);

  // ───── Conexión en tiempo real ─────
  useEffect(() => {
    if (!token) return;
    const c = crearConexion(token);
    conexion.current = c;

    c.on("mensaje", (m: Mensaje) => {
      setLista((l) => {
        const existe = l.some((x) => x.id === m.salaId);
        if (!existe) void apiSalas.listar(token).then(setLista).catch(() => undefined);
        return l
          .map((x) => x.id !== m.salaId ? x : {
            ...x, ultimo: m, ultimaActividad: m.ts,
            noLeidos: m.salaId === salaActual.current || m.autorId === usuario?.id ? 0 : x.noLeidos + 1,
          })
          .sort((a, b) => (a.tipo === "general" ? -1 : b.tipo === "general" ? 1 : b.ultimaActividad.localeCompare(a.ultimaActividad)));
      });
      if (m.salaId === salaActual.current) {
        setHilo((h) => (h.sala !== m.salaId || h.items.some((x) => x.id === m.id) ? h : { ...h, items: [...h.items, m] }));
        if (document.visibilityState === "visible") marcarLeido(m.salaId, m.id);
        setEscribiendo((e) => { const n = { ...e }; delete n[m.salaId]; return n; });
      }
    });
    c.on("mensajeActualizado", (m: Mensaje) => setHilo((h) => ({ ...h, items: h.items.map((x) => (x.id === m.id ? m : x)) })));
    c.on("presencia", (p: Persona[]) => setPresentes(p));
    c.on("escribiendo", (sala: string, nombre: string) =>
      setEscribiendo((e) => ({ ...e, [sala]: { nombre, hasta: Date.now() + 4000 } })));
    c.onreconnecting(() => setEstado("reconectando"));
    c.onreconnected(() => {
      setEstado("conectado");
      // Al volver se recuperan los mensajes que pudieron perderse durante el corte.
      const sala = salaActual.current;
      void apiSalas.mensajes(token, sala).then((items) => setHilo({ sala, items })).catch(() => undefined);
    });
    c.onclose(() => setEstado("desconectado"));

    const arrancar = async () => {
      setEstado("conectando");
      try {
        await c.start();
        setEstado("conectado");
        setPresentes(await c.invoke<Persona[]>("Presentes"));
      } catch {
        setEstado("desconectado");
      }
    };
    void arrancar();
    const dejarDeVigilar = vigilarVisibilidad(c, arrancar);
    return () => {
      dejarDeVigilar();
      void c.stop();
      conexion.current = null;
    };
  }, [token, usuario?.id, marcarLeido]);

  // ───── Lista de salas ─────
  useEffect(() => {
    if (!token) return;
    apiSalas.listar(token).then(setLista).catch((e) => setError(esFalla(e) ? e.detalle.mensaje : "No se pudieron cargar las salas."));
  }, [token]);

  // ───── Mensajes de la sala abierta ─────
  useEffect(() => {
    if (!token) return;
    apiSalas.mensajes(token, salaId)
      .then((ms) => {
        setHilo({ sala: salaId, items: ms });
        setHayMas(ms.length === 50);
        if (ms.length) marcarLeido(salaId, ms[ms.length - 1].id);
      })
      .catch((e) => setError(esFalla(e) ? e.detalle.mensaje : "No se pudieron cargar los mensajes."));
  }, [token, salaId, marcarLeido]);

  // Limpia los indicadores de "escribiendo" vencidos.
  useEffect(() => {
    const t = window.setInterval(() => setEscribiendo((e) => {
      const ahora = Date.now();
      const vigentes = Object.fromEntries(Object.entries(e).filter(([, v]) => v.hasta > ahora));
      return Object.keys(vigentes).length === Object.keys(e).length ? e : vigentes;
    }), 1500);
    return () => window.clearInterval(t);
  }, []);

  async function cargarAnteriores() {
    if (!mensajes.length) return;
    const previos = await apiSalas.mensajes(token, salaId, mensajes[0].id);
    setHayMas(previos.length === 50);
    setHilo((h) => ({ ...h, items: [...previos, ...h.items] }));
  }

  const sala = lista.find((x) => x.id === salaId);
  const conectado = estado === "conectado";

  return (
    <div className={s.contenedor} data-vista={salaParam ? "sala" : "lista"}>
      <aside className={s.lista} aria-label="Salas">
        <header className={s.listaCabecera}>
          <h1 className={s.titulo}>Salas</h1>
          <EstadoChip estado={estado} />
        </header>
        <nav className={s.salas}>
          {lista.map((x) => (
            <Link key={x.id} to={`/salas/${x.id}`} className={s.sala} aria-current={x.id === salaId ? "page" : undefined}>
              <span className={s.salaIcono} aria-hidden="true">
                {x.tipo === "general" ? <Hash size={16} /> : <InsigniaPrioridad prioridad={(x.prioridad ?? "P5") as Prioridad} />}
              </span>
              <span className={s.salaTexto}>
                <span className={s.salaNombre}>
                  {x.tipo === "general" ? "operaciones" : x.codigo}
                  {x.soloLectura && <Lock size={12} aria-label="Solo lectura" />}
                </span>
                <span className={s.salaUltimo}>
                  {x.tipo === "incidente" ? x.titulo : x.ultimo ? (x.ultimo.tipo === "sistema" ? x.ultimo.texto : `${x.ultimo.autorNombre?.split(" ")[0]}: ${x.ultimo.retirado ? "mensaje retirado" : x.ultimo.texto}`) : "Canal general del equipo"}
                </span>
              </span>
              {x.noLeidos > 0 && <span className={s.noLeidos} aria-label={`${x.noLeidos} sin leer`}>{x.noLeidos > 98 ? "99+" : x.noLeidos}</span>}
            </Link>
          ))}
        </nav>
        <footer className={s.presentes}>
          <span className={s.presentesTitulo}><span className={s.puntoVerde} aria-hidden="true" /> En línea ({presentes.length})</span>
          <ul>
            {presentes.map((p) => (
              <li key={p.id} title={`${p.nombre} · ${ROL[p.rol] ?? p.rol}`}>
                <span className={s.avatarMini} aria-hidden="true">{iniciales(p.nombre)}</span>
                <span>{p.id === usuario?.id ? `${p.nombre} (tú)` : p.nombre}</span>
              </li>
            ))}
          </ul>
        </footer>
      </aside>

      <section className={s.hilo} aria-label="Conversación">
        <header className={s.hiloCabecera}>
          <button type="button" className={s.volver} onClick={() => navegar("/salas")} aria-label="Volver a las salas">
            <ArrowLeft size={18} />
          </button>
          {sala?.tipo === "incidente" ? (
            <div className={s.hiloTitulo}>
              <span className={s.hiloCodigo}>
                <InsigniaPrioridad prioridad={(sala.prioridad ?? "P5") as Prioridad} /> {sala.codigo}
                {sala.estado && <span className={s.estado}>{NOMBRE_ESTADO[sala.estado as EstadoIncidente] ?? sala.estado}</span>}
              </span>
              <span className={s.hiloSub}>{sala.titulo}</span>
            </div>
          ) : (
            <div className={s.hiloTitulo}>
              <span className={s.hiloCodigo}><Hash size={18} aria-hidden="true" /> operaciones</span>
              <span className={s.hiloSub}>Canal general: nuevos incidentes, escalamientos y resoluciones.</span>
            </div>
          )}
          {sala?.incidenteId && (
            <Link to={`/incidentes/${sala.incidenteId}`} className={s.verIncidente}>
              Ver incidente <ExternalLink size={14} aria-hidden="true" />
            </Link>
          )}
        </header>

        {error && <div className={s.aviso}><Alerta tipo="error">{error}</Alerta></div>}

        <Hilo mensajes={mensajes} ahora={ahora} hayMas={hayMas} alCargarAnteriores={cargarAnteriores} miId={usuario?.id}
          alRetirar={(m) => conexion.current?.invoke("Retirar", m.id).catch((e: Error) => setError(limpiarError(e)))} />

        <div className={s.escribiendo} aria-live="polite">
          {escribiendo[salaId] && <><span className={s.puntos} aria-hidden="true"><i /><i /><i /></span> {escribiendo[salaId].nombre} está escribiendo…</>}
        </div>

        {sala?.soloLectura ? (
          <p className={s.soloLectura}><Lock size={14} aria-hidden="true" /> El incidente está cerrado: esta sala quedó de solo lectura como registro.</p>
        ) : (
          <Redactor salaId={salaId} deshabilitado={!conectado} conexion={conexion} alError={setError} />
        )}
      </section>
    </div>
  );
}

function limpiarError(e: Error): string {
  // HubException llega como "An unexpected error occurred invoking 'X' on the server. HubException: mensaje".
  const m = /HubException: (.*)$/.exec(e.message);
  return m ? m[1] : "No se pudo completar la acción. Revisa tu conexión.";
}

function EstadoChip({ estado }: { estado: EstadoConexion }) {
  const textos: Record<EstadoConexion, string> = { conectado: "En vivo", conectando: "Conectando…", reconectando: "Reconectando…", desconectado: "Sin conexión" };
  return (
    <span className={s.conexion} data-estado={estado}>
      {estado === "desconectado" ? <WifiOff size={13} aria-hidden="true" /> : <Wifi size={13} aria-hidden="true" />} {textos[estado]}
    </span>
  );
}

function Hilo({ mensajes, ahora, hayMas, alCargarAnteriores, miId, alRetirar }: {
  mensajes: Mensaje[]; ahora: number; hayMas: boolean; alCargarAnteriores: () => Promise<void>; miId?: string; alRetirar: (m: Mensaje) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const pegadoAbajo = useRef(true);
  const ultimoId = mensajes[mensajes.length - 1]?.id;

  // Baja al último mensaje solo si la persona ya estaba abajo (no interrumpe si está leyendo lo anterior).
  useLayoutEffect(() => {
    const el = ref.current;
    if (el && pegadoAbajo.current) el.scrollTop = el.scrollHeight;
  }, [ultimoId]);

  const grupos = useMemo(() => {
    const salida: { dia: string; items: { m: Mensaje; compacto: boolean }[] }[] = [];
    let previo: Mensaje | undefined;
    for (const m of mensajes) {
      const d = dia(m.ts);
      if (!salida.length || salida[salida.length - 1].dia !== d) { salida.push({ dia: d, items: [] }); previo = undefined; }
      const compacto = !!previo && previo.tipo === "humano" && m.tipo === "humano" && previo.autorId === m.autorId
        && new Date(m.ts).getTime() - new Date(previo.ts).getTime() < 5 * 60_000;
      salida[salida.length - 1].items.push({ m, compacto });
      previo = m;
    }
    return salida;
  }, [mensajes]);

  return (
    <div className={s.mensajes} ref={ref} onScroll={(e) => {
      const el = e.currentTarget;
      pegadoAbajo.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
    }}>
      {hayMas && <button type="button" className={s.anteriores} onClick={() => void alCargarAnteriores()}>Ver mensajes anteriores</button>}
      {mensajes.length === 0 && (
        <div className={s.vacio}>
          <MessagesSquare size={28} aria-hidden="true" />
          <p>Todavía no hay mensajes. Escribe el primero.</p>
        </div>
      )}
      {grupos.map((g) => (
        <div key={g.dia} role="list" aria-label={g.dia}>
          <div className={s.separador}><span>{g.dia}</span></div>
          {g.items.map(({ m, compacto }) => m.tipo === "sistema" ? (
            <div key={m.id} role="listitem" className={s.sistema}>
              <span>{m.texto}</span>
              <time dateTime={m.ts}>{hora(m.ts)}</time>
            </div>
          ) : (
            <div key={m.id} role="listitem" className={`${s.mensaje} ${compacto ? s.compacto : ""}`} data-propio={m.autorId === miId || undefined}>
              {!compacto ? <span className={s.avatar} aria-hidden="true">{iniciales(m.autorNombre)}</span> : <time className={s.horaLateral} dateTime={m.ts}>{hora(m.ts)}</time>}
              <div className={s.cuerpo}>
                {!compacto && (
                  <p className={s.autor}>
                    <strong>{m.autorNombre}</strong>
                    <span className={s.rol} data-rol={m.autorRol}>{ROL[m.autorRol ?? ""] ?? m.autorRol}</span>
                    <time dateTime={m.ts}>{hora(m.ts)}</time>
                  </p>
                )}
                {m.retirado
                  ? <p className={s.retirado}>Mensaje retirado por {m.autorNombre?.split(" ")[0]}</p>
                  : <p className={s.texto}>{m.texto}</p>}
              </div>
              {m.autorId === miId && !m.retirado && ahora - new Date(m.ts).getTime() < PLAZO_RETIRAR_MS && (
                <button type="button" className={s.retirar} onClick={() => alRetirar(m)} title="Retirar mensaje (hasta 15 min)" aria-label="Retirar mensaje">
                  <Undo2 size={14} />
                </button>
              )}
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

function Redactor({ salaId, deshabilitado, conexion, alError }: {
  salaId: string; deshabilitado: boolean; conexion: RefObject<HubConnection | null>; alError: (e: string | null) => void;
}) {
  const [texto, setTexto] = useState("");
  const [enviando, setEnviando] = useState(false);
  const ultimoAviso = useRef(0);
  const area = useRef<HTMLTextAreaElement>(null);

  // Altura automática (hasta 6 líneas).
  useLayoutEffect(() => {
    const el = area.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }, [texto]);

  async function enviar() {
    const limpio = texto.trim();
    if (!limpio || enviando || !conexion.current) return;
    setEnviando(true);
    alError(null);
    try {
      await conexion.current.invoke("Enviar", salaId, limpio);
      setTexto("");
    } catch (e) {
      alError(limpiarError(e as Error));
    } finally {
      setEnviando(false);
      area.current?.focus();
    }
  }

  function alTeclear(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      void enviar();
    }
  }

  return (
    <form className={s.redactor} onSubmit={(e) => { e.preventDefault(); void enviar(); }}>
      <label htmlFor="redactor" className={s.oculto}>Mensaje</label>
      <textarea
        id="redactor"
        ref={area}
        rows={1}
        value={texto}
        maxLength={MAX_TEXTO}
        disabled={deshabilitado}
        placeholder={deshabilitado ? "Conectando al chat…" : salaId === "operaciones" ? "Mensaje para #operaciones" : "Mensaje para la sala del incidente"}
        onKeyDown={alTeclear}
        onChange={(e) => {
          setTexto(e.target.value);
          // "Escribiendo…" como máximo cada 3 s.
          if (Date.now() - ultimoAviso.current > 3000 && conexion.current?.state === HubConnectionState.Connected) {
            ultimoAviso.current = Date.now();
            void conexion.current.invoke("Escribiendo", salaId).catch(() => undefined);
          }
        }}
      />
      {texto.length > MAX_TEXTO - 200 && <span className={s.contador}>{MAX_TEXTO - texto.length}</span>}
      <button type="submit" className={s.enviar} disabled={deshabilitado || enviando || !texto.trim()} aria-label="Enviar">
        <Send size={18} aria-hidden="true" />
      </button>
      <p className={s.ayuda}>Enter para enviar · Shift + Enter para una línea nueva</p>
    </form>
  );
}
