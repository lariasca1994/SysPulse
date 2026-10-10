import { Link } from "react-router";
import { ChevronRight, Monitor, Pause, User } from "lucide-react";
import { NOMBRE_ESTADO, slaVigente, type EstadoIncidente, type Incidente, type Prioridad, type Sla } from "../../api/incidentes";
import { relativo, tonoSla } from "./formato";
import s from "./incidentes.module.css";

export function InsigniaPrioridad({ prioridad }: { prioridad: Prioridad }) {
  return <span className={s.prioridad} data-p={prioridad}>{prioridad}</span>;
}

export function ChipEstado({ estado }: { estado: EstadoIncidente }) {
  return <span className={s.estado} data-estado={estado}>{NOMBRE_ESTADO[estado]}</span>;
}

export function TextoSla({ sla, ahora, tipo }: { sla: Sla; ahora: number; tipo: "respuesta" | "resolucion" }) {
  const nombre = tipo === "respuesta" ? "Respuesta" : "Resolución";
  if (sla.detenido && sla.cumplido) return <>{nombre}: cumplida</>;
  if (sla.detenido && sla.porcentaje >= 100) return <>{nombre}: incumplida</>;
  if (sla.detenido) return <>{nombre}: en pausa</>;
  return sla.porcentaje >= 100
    ? <>{nombre}: <strong>venció {relativo(sla.vence, ahora)}</strong></>
    : <>{nombre}: vence {relativo(sla.vence, ahora)}</>;
}

export function BarraSla({ sla, compacta = false }: { sla: Sla; compacta?: boolean }) {
  const tono = tonoSla(sla);
  return (
    <div className={`${s.barraSla} ${compacta ? s.compacta : ""}`} role="meter" aria-label="Tiempo de SLA consumido"
      aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.min(100, Math.round(sla.porcentaje))}>
      <span data-tono={tono} style={{ width: `${Math.min(100, sla.porcentaje)}%` }} />
      <i className={s.marca75} aria-hidden="true" />
    </div>
  );
}

export function FilaIncidente({ inc, ahora }: { inc: Incidente; ahora: number }) {
  const { tipo, sla } = slaVigente(inc);
  return (
    <Link to={`/incidentes/${inc.id}`} className={s.fila} data-tono={tonoSla(sla)}>
      <div className={s.filaIdentidad}>
        <InsigniaPrioridad prioridad={inc.prioridad} />
        <div className={s.filaTexto}>
          <span className={s.codigo}>{inc.codigo}</span>
          <strong className={s.filaTitulo}>{inc.titulo}</strong>
          <span className={s.filaMeta}>
            {inc.hostNombre && <span><Monitor size={13} aria-hidden="true" /> {inc.hostNombre}</span>}
            <span>{inc.nivel}</span>
            <span><User size={13} aria-hidden="true" /> {inc.asignado?.nombre ?? "Sin asignar"}</span>
            {inc.metricaRecuperada && <span className={s.recuperada}>Métrica normalizada</span>}
          </span>
        </div>
      </div>
      <ChipEstado estado={inc.estado} />
      <div className={s.filaSla}>
        <span className={s.filaSlaTexto}>
          {inc.estado === "EnEspera" && <Pause size={12} aria-hidden="true" />} <TextoSla sla={sla} ahora={ahora} tipo={tipo} />
        </span>
        <BarraSla sla={sla} compacta />
      </div>
      <ChevronRight size={16} aria-hidden="true" className={s.flecha} />
    </Link>
  );
}
