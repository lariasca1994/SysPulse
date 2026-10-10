import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import {
  ArrowLeft, ArrowUpCircle, CheckCircle2, Flag, Hand, MessageSquare, Monitor, Pause, Play, RotateCcw, Send, ShieldAlert, UserPlus, XCircle,
} from "lucide-react";
import { CODIGOS_CIERRE, incidentes, type Accion, type DatosAccion, type EntradaHistorial, type Incidente, type Prioridad } from "../../api/incidentes";
import { esFalla } from "../../api/cliente";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { AreaTexto, Selector } from "../../componentes/ui/Campos";
import { Dialogo } from "../../componentes/ui/Dialogo";
import { useAhora } from "../hosts/useAhora";
import { BarraSla, ChipEstado, InsigniaPrioridad, TextoSla } from "./Piezas";
import { fechaHora, hace } from "./formato";
import s from "./incidentes.module.css";
import d from "./detalle.module.css";

/** Acciones que piden datos antes de ejecutarse (se abren en un diálogo). */
type AccionConDatos = "Pausar" | "Escalar" | "CambiarPrioridad" | "Asignar" | "Resolver" | "Reabrir";

const DIALOGOS: Record<AccionConDatos, { titulo: string; descripcion: string; etiqueta: string; boton: string }> = {
  Pausar: { titulo: "Poner en espera", descripcion: "El SLA de resolución se pausa hasta que reanudes.", etiqueta: "¿Qué se está esperando?", boton: "Poner en espera" },
  Escalar: { titulo: "Escalar al siguiente nivel", descripcion: "Se avisa a todo el nivel siguiente y el incidente queda libre para que lo tomen.", etiqueta: "Motivo del escalamiento", boton: "Escalar" },
  CambiarPrioridad: { titulo: "Cambiar prioridad", descripcion: "El SLA se recalcula desde ahora. Queda en la auditoría.", etiqueta: "Justificación", boton: "Cambiar prioridad" },
  Asignar: { titulo: "Asignar a una persona", descripcion: "Se le avisa directamente.", etiqueta: "", boton: "Asignar" },
  Resolver: { titulo: "Resolver incidente", descripcion: "Se avisa a quienes recibieron avisos de este incidente.", etiqueta: "Solución aplicada", boton: "Marcar como resuelto" },
  Reabrir: { titulo: "Reabrir incidente", descripcion: "Vuelve a su nivel y se avisa de nuevo.", etiqueta: "¿Por qué se reabre?", boton: "Reabrir" },
};

export default function DetalleIncidente() {
  const { id = "" } = useParams();
  const { sesion } = useSesion();
  const ahora = useAhora();
  const consulta = useConsulta((t) => incidentes.obtener(t, id), [id], 30_000);
  const [actual, setActual] = useState<Incidente | null>(null);
  const [dialogo, setDialogo] = useState<AccionConDatos | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState<Accion | null>(null);
  const [marca, setMarca] = useState(0);
  // Tras una acción se muestra su respuesta hasta que llegue una consulta posterior.
  const inc = actual && (consulta.actualizado?.getTime() ?? 0) < marca ? actual : consulta.datos ?? actual;

  async function ejecutar(accion: Accion, datos: DatosAccion = {}) {
    if (!sesion || !inc) return false;
    setError(null);
    setOcupado(accion);
    try {
      const r = await incidentes.accion(sesion.token, inc.id, accion, datos);
      setMarca(Date.now());
      setActual(r);
      void consulta.recargar();
      return true;
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo completar la acción.");
      return false;
    } finally {
      setOcupado(null);
    }
  }

  if (!inc) {
    return (
      <>
        <Volver />
        {consulta.error ? <Alerta tipo="error">{consulta.error}</Alerta> : <div className={`${d.panel} ${d.cargando}`} aria-busy="true" />}
      </>
    );
  }

  const puede = (a: Accion) => inc.acciones.includes(a);

  return (
    <>
      <Volver />

      <header className={d.cabecera} data-p={inc.prioridad}>
        <div className={d.titulares}>
          <div className={d.chips}>
            <InsigniaPrioridad prioridad={inc.prioridad} />
            <span className={s.codigo}>{inc.codigo}</span>
            <ChipEstado estado={inc.estado} />
            <span className={d.nivel}>Nivel {inc.nivel}</span>
            {inc.metricaRecuperada && <span className={s.recuperada}>Métrica normalizada</span>}
          </div>
          <h1 className={d.titulo}>{inc.titulo}</h1>
          {inc.descripcion && <p className={d.descripcion}>{inc.descripcion}</p>}
        </div>

        <div className={d.acciones}>
          {puede("Tomar") && <Boton cargando={ocupado === "Tomar"} onClick={() => void ejecutar("Tomar")}><Hand size={16} aria-hidden="true" /> Tomar</Boton>}
          {puede("Iniciar") && <Boton cargando={ocupado === "Iniciar"} onClick={() => void ejecutar("Iniciar")}><Play size={16} aria-hidden="true" /> Iniciar trabajo</Boton>}
          {puede("Reanudar") && <Boton cargando={ocupado === "Reanudar"} onClick={() => void ejecutar("Reanudar")}><Play size={16} aria-hidden="true" /> Reanudar</Boton>}
          {puede("Resolver") && <Boton onClick={() => setDialogo("Resolver")}><CheckCircle2 size={16} aria-hidden="true" /> Resolver</Boton>}
          {puede("Cerrar") && <Boton cargando={ocupado === "Cerrar"} onClick={() => void ejecutar("Cerrar")}><XCircle size={16} aria-hidden="true" /> Cerrar</Boton>}
          {puede("Pausar") && <Boton variante="secundario" onClick={() => setDialogo("Pausar")}><Pause size={16} aria-hidden="true" /> En espera</Boton>}
          {puede("Escalar") && <Boton variante="secundario" onClick={() => setDialogo("Escalar")}><ArrowUpCircle size={16} aria-hidden="true" /> Escalar</Boton>}
          {puede("Asignar") && <Boton variante="secundario" onClick={() => setDialogo("Asignar")}><UserPlus size={16} aria-hidden="true" /> Asignar</Boton>}
          {puede("CambiarPrioridad") && <Boton variante="secundario" onClick={() => setDialogo("CambiarPrioridad")}><Flag size={16} aria-hidden="true" /> Prioridad</Boton>}
          {puede("Reabrir") && <Boton variante="secundario" onClick={() => setDialogo("Reabrir")}><RotateCcw size={16} aria-hidden="true" /> Reabrir</Boton>}
        </div>
      </header>

      {error && <div className={d.bloque}><Alerta tipo="error">{error}</Alerta></div>}

      <div className={d.rejilla}>
        <div className={d.principal}>
          {inc.resolucion && (
            <section className={`${d.panel} ${d.resolucion}`}>
              <h2><CheckCircle2 size={16} aria-hidden="true" /> Resolución · {inc.codigoCierre}</h2>
              <p>{inc.resolucion}</p>
            </section>
          )}

          <section className={d.panel} aria-labelledby="historial">
            <h2 id="historial" className={d.tituloPanel}>Historial</h2>
            <ol className={d.linea}>
              {[...(inc.historial ?? [])].reverse().map((e, i) => <Entrada key={`${e.ts}-${i}`} e={e} ahora={ahora} />)}
            </ol>
            <Comentario enviando={ocupado === "Comentar"} alEnviar={(texto) => ejecutar("Comentar", { texto })} />
          </section>
        </div>

        <aside className={d.lateral}>
          <section className={d.panel} aria-labelledby="sla">
            <h2 id="sla" className={d.tituloPanel}>SLA · {inc.prioridad}</h2>
            <div className={d.sla}>
              <p><TextoSla sla={inc.slaRespuesta} ahora={ahora} tipo="respuesta" /></p>
              <BarraSla sla={inc.slaRespuesta} />
              <p className={d.slaNota}>Objetivo: {fechaHora(inc.slaRespuesta.vence)}</p>
            </div>
            <div className={d.sla}>
              <p><TextoSla sla={inc.slaResolucion} ahora={ahora} tipo="resolucion" /></p>
              <BarraSla sla={inc.slaResolucion} />
              <p className={d.slaNota}>Objetivo: {fechaHora(inc.slaResolucion.vence)}</p>
            </div>
          </section>

          <section className={d.panel} aria-labelledby="datos">
            <h2 id="datos" className={d.tituloPanel}>Detalles</h2>
            <dl className={d.datos}>
              <div><dt>Asignado</dt><dd>{inc.asignado?.nombre ?? "Sin asignar"}</dd></div>
              <div><dt>Equipo</dt><dd>{inc.hostId ? <Link to={`/hosts/${encodeURIComponent(inc.hostId)}`}><Monitor size={13} aria-hidden="true" /> {inc.hostNombre}</Link> : "—"}</dd></div>
              <div><dt>Impacto · urgencia</dt><dd>{inc.impacto} · {inc.urgencia}</dd></div>
              <div><dt>Origen</dt><dd>{inc.origen === "evento" ? "Monitoreo automático" : "Registro manual"}</dd></div>
              <div><dt>Creado</dt><dd>{fechaHora(inc.creado)}</dd></div>
              {inc.primeraRespuesta && <div><dt>Tomado</dt><dd>{fechaHora(inc.primeraRespuesta)}</dd></div>}
              {inc.resuelto && <div><dt>Resuelto</dt><dd>{fechaHora(inc.resuelto)}</dd></div>}
            </dl>
          </section>
        </aside>
      </div>

      {dialogo && (
        <Dialogo abierto titulo={DIALOGOS[dialogo].titulo} descripcion={DIALOGOS[dialogo].descripcion} alCerrar={() => setDialogo(null)}>
          <FormularioAccion accion={dialogo} inc={inc} enviando={ocupado === dialogo}
            alEnviar={async (datos) => { if (await ejecutar(dialogo, datos)) setDialogo(null); }} alCancelar={() => setDialogo(null)} error={error} />
        </Dialogo>
      )}
    </>
  );
}

function FormularioAccion({ accion, inc, enviando, alEnviar, alCancelar, error }: {
  accion: AccionConDatos;
  inc: Incidente;
  enviando: boolean;
  alEnviar: (d: DatosAccion) => Promise<void>;
  alCancelar: () => void;
  error: string | null;
}) {
  const conf = DIALOGOS[accion];
  const [texto, setTexto] = useState("");
  const [prioridad, setPrioridad] = useState<Prioridad>(inc.prioridad === "P3" ? "P2" : "P3");
  const [codigo, setCodigo] = useState(CODIGOS_CIERRE[0]);
  const [usuarioId, setUsuarioId] = useState("");
  const tecnicos = useConsulta((t) => (accion === "Asignar" ? incidentes.tecnicos(t) : Promise.resolve([])), [accion]);

  const valido = accion === "Asignar" ? usuarioId !== "" : texto.trim().length >= (accion === "Resolver" ? 10 : 3);

  function enviar(e: FormEvent) {
    e.preventDefault();
    void alEnviar({ texto: texto.trim() || undefined, prioridad: accion === "CambiarPrioridad" ? prioridad : undefined, codigoCierre: accion === "Resolver" ? codigo : undefined, usuarioId: usuarioId || undefined });
  }

  return (
    <form className={s.formulario} onSubmit={enviar} noValidate>
      {error && <Alerta tipo="error">{error}</Alerta>}
      {accion === "Escalar" && <p className={s.prioridadCalculada}>Pasa de <strong>{inc.nivel}</strong> a <strong>{inc.nivel === "N1" ? "N2" : "N3"}</strong>.</p>}
      {accion === "CambiarPrioridad" && (
        <Selector etiqueta="Nueva prioridad" value={prioridad} onChange={(e) => setPrioridad(e.target.value as Prioridad)}>
          {(["P1", "P2", "P3", "P4", "P5"] as Prioridad[]).filter((p) => p !== inc.prioridad).map((p) => <option key={p} value={p}>{p}</option>)}
        </Selector>
      )}
      {accion === "Asignar" && (
        <Selector etiqueta="Persona" value={usuarioId} onChange={(e) => setUsuarioId(e.target.value)}>
          <option value="">Elige un técnico…</option>
          {(tecnicos.datos ?? []).map((p) => <option key={p.id} value={p.id}>{p.nombre} ({p.rol === "admin" ? "Admin" : p.rol})</option>)}
        </Selector>
      )}
      {accion === "Resolver" && (
        <Selector etiqueta="Código de cierre" value={codigo} onChange={(e) => setCodigo(e.target.value)}>
          {CODIGOS_CIERRE.map((c) => <option key={c} value={c}>{c}</option>)}
        </Selector>
      )}
      {accion !== "Asignar" && (
        <AreaTexto etiqueta={conf.etiqueta} value={texto} maxLength={2000} onChange={(e) => setTexto(e.target.value)} autoFocus
          ayuda={accion === "Resolver" ? "Mínimo 10 caracteres. Quedará como referencia para futuros incidentes." : undefined} />
      )}
      <div className={s.accionesForm}>
        <Boton type="button" variante="secundario" onClick={alCancelar}>Cancelar</Boton>
        <Boton type="submit" cargando={enviando} disabled={!valido}>{conf.boton}</Boton>
      </div>
    </form>
  );
}

function Comentario({ enviando, alEnviar }: { enviando: boolean; alEnviar: (texto: string) => Promise<boolean> }) {
  const [texto, setTexto] = useState("");
  return (
    <form className={d.comentario} onSubmit={async (e) => { e.preventDefault(); if (texto.trim() && (await alEnviar(texto.trim()))) setTexto(""); }}>
      <MessageSquare size={18} aria-hidden="true" className={d.iconoComentario} />
      <label htmlFor="comentario" className={d.oculto}>Agregar comentario</label>
      <textarea id="comentario" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Agrega una nota para el equipo…" maxLength={2000} rows={2} />
      <Boton type="submit" cargando={enviando} disabled={!texto.trim()} aria-label="Enviar comentario"><Send size={16} aria-hidden="true" /></Boton>
    </form>
  );
}

const ICONOS: Record<EntradaHistorial["tipo"], typeof Hand> = {
  estado: Play, escalamiento: ArrowUpCircle, prioridad: Flag, asignacion: UserPlus, comentario: MessageSquare, sistema: Monitor, sla: ShieldAlert,
};

function Entrada({ e, ahora }: { e: EntradaHistorial; ahora: number }) {
  const Icono = ICONOS[e.tipo] ?? Monitor;
  const resumen = e.tipo === "estado" && e.de && e.a ? `${e.de} → ${e.a}`
    : e.tipo === "escalamiento" ? `Escaló de ${e.de} a ${e.a}`
    : e.tipo === "prioridad" ? `Cambió la prioridad de ${e.de} a ${e.a}`
    : e.tipo === "asignacion" ? `Asignó a ${e.a}`
    : null;
  return (
    <li className={d.entrada} data-tipo={e.tipo}>
      <span className={d.icono} aria-hidden="true"><Icono size={14} /></span>
      <div className={d.cuerpo}>
        <p className={d.encabezadoEntrada}>
          <strong>{e.usuario ?? (e.tipo === "sla" ? "SLA" : "SysPulse")}</strong>
          {resumen && <span> · {resumen}</span>}
          <time dateTime={e.ts} title={fechaHora(e.ts)}>{hace(e.ts, ahora)}</time>
        </p>
        {e.texto && <p className={d.textoEntrada}>{e.texto}</p>}
      </div>
    </li>
  );
}

function Volver() {
  return (
    <Link to="/incidentes" className={d.volver}>
      <ArrowLeft size={16} aria-hidden="true" /> Incidentes
    </Link>
  );
}
