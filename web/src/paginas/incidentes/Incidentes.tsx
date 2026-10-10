import { useState, type FormEvent, type ReactNode } from "react";
import { useNavigate } from "react-router";
import { AlertTriangle, Clock, Hand, Inbox, Plus, RefreshCw, TimerOff } from "lucide-react";
import { abierto, incidentes, slaVigente, type Incidente, type NuevoIncidente } from "../../api/incidentes";
import { hosts } from "../../api/hosts";
import { esFalla } from "../../api/cliente";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { AreaTexto, Campo, Selector } from "../../componentes/ui/Campos";
import { Dialogo } from "../../componentes/ui/Dialogo";
import { useAhora } from "../hosts/useAhora";
import { FilaIncidente } from "./Piezas";
import h from "../hosts/hosts.module.css";
import s from "./incidentes.module.css";

const PRIORIDAD_POR = {
  Alto: { Alta: "P1", Media: "P2", Baja: "P3" },
  Medio: { Alta: "P2", Media: "P3", Baja: "P4" },
  Bajo: { Alta: "P3", Media: "P4", Baja: "P5" },
} as const;

export default function Incidentes() {
  const [todos, setTodos] = useState(false);
  const [creando, setCreando] = useState(false);
  const ahora = useAhora();
  const { datos, error, cargando, recargar } = useConsulta((t) => incidentes.listar(t, todos), [todos], 30_000);
  const lista = datos ?? [];
  const abiertos = lista.filter(abierto);

  const sinTomar = abiertos.filter((i) => !i.primeraRespuesta).length;
  const enRiesgo = abiertos.filter((i) => { const v = slaVigente(i).sla; return !v.detenido && v.porcentaje >= 75 && v.porcentaje < 100; }).length;
  const vencidos = abiertos.filter((i) => slaVigente(i).sla.porcentaje >= 100).length;

  return (
    <>
      <div className={h.encabezado}>
        <div>
          <h1 className={h.titulo}>Incidentes</h1>
          <p className={h.subtitulo}>Los crea el monitoreo con cada excepción o cualquier persona del equipo. El SLA corre desde que se registran.</p>
        </div>
        <div className={h.refresco}>
          <button type="button" className={h.botonIcono} onClick={() => void recargar()} disabled={cargando} aria-label="Actualizar" title="Actualizar">
            <RefreshCw size={16} aria-hidden="true" className={cargando ? h.girando : undefined} />
          </button>
          <Boton onClick={() => setCreando(true)}>
            <Plus size={16} aria-hidden="true" /> Registrar incidente
          </Boton>
        </div>
      </div>

      {error && <div className={h.bloque}><Alerta tipo="error">{error}</Alerta></div>}

      <section className={h.resumen} aria-label="Resumen">
        <Indicador icono={<Inbox size={18} />} tono="info" valor={datos ? abiertos.length : null} texto="Abiertos" />
        <Indicador icono={<Hand size={18} />} tono="advertencia" valor={datos ? sinTomar : null} texto="Sin tomar" />
        <Indicador icono={<Clock size={18} />} tono="advertencia" valor={datos ? enRiesgo : null} texto="SLA en riesgo (≥ 75 %)" />
        <Indicador icono={<TimerOff size={18} />} tono="excepcion" valor={datos ? vencidos : null} texto="SLA vencido" />
      </section>

      <div className={s.barraFiltros}>
        <div className={s.segmentado} role="radiogroup" aria-label="Mostrar">
          <button type="button" role="radio" aria-checked={!todos} onClick={() => setTodos(false)}>Abiertos</button>
          <button type="button" role="radio" aria-checked={todos} onClick={() => setTodos(true)}>Todos</button>
        </div>
        <span className={s.conteo}>{datos ? `${lista.length} incidente${lista.length === 1 ? "" : "s"}` : ""}</span>
      </div>

      {!datos && cargando ? (
        <div className={s.lista} aria-busy="true">
          {[0, 1, 2].map((i) => <div key={i} className={`${s.fila} ${s.filaCargando} ${h.esqueleto}`} />)}
        </div>
      ) : lista.length === 0 ? (
        <div className={h.vacio}>
          <AlertTriangle size={28} aria-hidden="true" />
          <h2>{todos ? "Aún no hay incidentes" : "No hay incidentes abiertos"}</h2>
          <p>Cuando un equipo supere un umbral de excepción, el incidente aparecerá aquí y se avisará al nivel N1.</p>
        </div>
      ) : (
        <div className={s.lista}>
          {lista.map((i) => <FilaIncidente key={i.id} inc={i} ahora={ahora} />)}
        </div>
      )}

      <Dialogo abierto={creando} alCerrar={() => setCreando(false)} titulo="Registrar incidente"
        descripcion="La prioridad sale de impacto × urgencia. Se avisa al nivel N1 por Telegram y correo.">
        <FormularioNuevo alCrear={(i) => { setCreando(false); void recargar(); return i; }} alCancelar={() => setCreando(false)} />
      </Dialogo>
    </>
  );
}

function FormularioNuevo({ alCrear, alCancelar }: { alCrear: (i: Incidente) => void; alCancelar: () => void }) {
  const { sesion, usuario } = useSesion();
  const navegar = useNavigate();
  const equipos = useConsulta((t) => hosts.listar(t), []);
  const [datos, setDatos] = useState<NuevoIncidente>({ titulo: "", descripcion: "", impacto: "Medio", urgencia: "Media", hostId: "" });
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const prioridad = PRIORIDAD_POR[datos.impacto][datos.urgencia];
  const puedeP1 = usuario?.rol === "gestor" || usuario?.rol === "admin";

  async function enviar(e: FormEvent) {
    e.preventDefault();
    if (!sesion) return;
    setError(null);
    setEnviando(true);
    try {
      const creado = await incidentes.crear(sesion.token, { ...datos, hostId: datos.hostId || undefined });
      alCrear(creado);
      navegar(`/incidentes/${creado.id}`);
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo registrar.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <form className={s.formulario} onSubmit={enviar} noValidate>
      {error && <Alerta tipo="error">{error}</Alerta>}
      <Campo etiqueta="Título" value={datos.titulo} maxLength={140} required onChange={(e) => setDatos({ ...datos, titulo: e.target.value })}
        placeholder="Ej.: El portátil no conecta a la red" />
      <AreaTexto etiqueta="Descripción" opcional value={datos.descripcion} maxLength={4000} onChange={(e) => setDatos({ ...datos, descripcion: e.target.value })}
        placeholder="Qué pasa, desde cuándo y a quién afecta." />
      <Selector etiqueta="Equipo afectado" value={datos.hostId} onChange={(e) => setDatos({ ...datos, hostId: e.target.value })}>
        <option value="">Ninguno en particular</option>
        {(equipos.datos ?? []).map((h) => <option key={h.id} value={h.id}>{h.nombre}</option>)}
      </Selector>
      <div className={s.dosColumnas}>
        <Selector etiqueta="Impacto" value={datos.impacto} onChange={(e) => setDatos({ ...datos, impacto: e.target.value as NuevoIncidente["impacto"] })}>
          <option value="Alto">Alto: muchos afectados</option>
          <option value="Medio">Medio: un equipo o grupo</option>
          <option value="Bajo">Bajo: una persona</option>
        </Selector>
        <Selector etiqueta="Urgencia" value={datos.urgencia} onChange={(e) => setDatos({ ...datos, urgencia: e.target.value as NuevoIncidente["urgencia"] })}>
          <option value="Alta">Alta: detenido ahora</option>
          <option value="Media">Media: degradado</option>
          <option value="Baja">Baja: puede esperar</option>
        </Selector>
      </div>
      <p className={s.prioridadCalculada}>
        Prioridad resultante: <strong data-p={prioridad}>{prioridad}</strong>
        {prioridad === "P1" && !puedeP1 && <span> · Un P1 solo lo declara el gestor; baja el impacto o la urgencia.</span>}
      </p>
      <div className={s.accionesForm}>
        <Boton type="button" variante="secundario" onClick={alCancelar}>Cancelar</Boton>
        <Boton type="submit" cargando={enviando} disabled={datos.titulo.trim().length < 5 || (prioridad === "P1" && !puedeP1)}>Registrar</Boton>
      </div>
    </form>
  );
}

function Indicador({ icono, valor, texto, tono }: { icono: ReactNode; valor: number | null; texto: string; tono: string }) {
  return (
    <div className={h.indicador} data-tono={tono}>
      <span className={h.indicadorIcono} aria-hidden="true">{icono}</span>
      <div>
        <p className={`${h.indicadorValor} num`}>{valor ?? "—"}</p>
        <p className={h.indicadorTexto}>{texto}</p>
      </div>
    </div>
  );
}
