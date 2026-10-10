import type { ReactNode } from "react";
import { Link } from "react-router";
import { CheckCircle2, Clock, Hand, UserCheck } from "lucide-react";
import { incidentes, slaVigente, type Incidente } from "../../api/incidentes";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { Alerta } from "../../componentes/ui/Basicos";
import { useAhora } from "../hosts/useAhora";
import { FilaIncidente } from "./Piezas";
import h from "../hosts/hosts.module.css";
import s from "./incidentes.module.css";

/** Lo que vence primero, arriba. */
const porVencimiento = (a: Incidente, b: Incidente) =>
  new Date(slaVigente(a).sla.vence).getTime() - new Date(slaVigente(b).sla.vence).getTime();

export default function MiTurno() {
  const { usuario } = useSesion();
  const ahora = useAhora();
  const { datos, error, cargando } = useConsulta((t) => incidentes.listar(t), [], 30_000);
  const lista = datos ?? [];
  const esGestor = usuario?.rol === "gestor";

  const mios = lista.filter((i) => i.asignado?.id === usuario?.id).sort(porVencimiento);
  const disponibles = lista.filter((i) => i.acciones.includes("Tomar")).sort(porVencimiento);
  const enRiesgo = lista.filter((i) => slaVigente(i).sla.porcentaje >= 75 || i.prioridad === "P1").sort(porVencimiento);

  const nombre = usuario?.nombre.split(" ")[0] ?? "";

  return (
    <>
      <div className={h.encabezado}>
        <div>
          <h1 className={h.titulo}>Mi turno</h1>
          <p className={h.subtitulo}>
            {esGestor
              ? `Hola, ${nombre}. Estos son los incidentes que requieren una decisión: SLA en riesgo, vencidos o P1.`
              : `Hola, ${nombre}. Lo que tienes asignado y lo que puedes tomar en tu nivel (${usuario?.rol === "admin" ? "todos" : usuario?.rol}), ordenado por lo que vence primero.`}
          </p>
        </div>
      </div>

      {error && <div className={h.bloque}><Alerta tipo="error">{error}</Alerta></div>}

      {!datos && cargando ? (
        <div className={s.lista} aria-busy="true">
          {[0, 1].map((i) => <div key={i} className={`${s.fila} ${s.filaCargando} ${h.esqueleto}`} />)}
        </div>
      ) : esGestor ? (
        <Seccion icono={<Clock size={18} />} titulo="Requieren tu atención" vacio="Ningún incidente con SLA en riesgo ni P1 abierto." lista={enRiesgo} ahora={ahora} />
      ) : (
        <>
          <Seccion icono={<UserCheck size={18} />} titulo="Asignados a mí" vacio="No tienes incidentes asignados." lista={mios} ahora={ahora} />
          <Seccion icono={<Hand size={18} />} titulo="Disponibles para tomar" vacio="No hay incidentes pendientes en tu nivel." lista={disponibles} ahora={ahora} />
        </>
      )}
    </>
  );
}

function Seccion({ icono, titulo, vacio, lista, ahora }: { icono: ReactNode; titulo: string; vacio: string; lista: Incidente[]; ahora: number }) {
  return (
    <section className={s.seccion} aria-label={titulo}>
      <h2 className={s.tituloSeccion}>
        <span aria-hidden="true">{icono}</span> {titulo} <span className={s.contador}>{lista.length}</span>
      </h2>
      {lista.length === 0 ? (
        <p className={s.todoBien}><CheckCircle2 size={16} aria-hidden="true" /> {vacio} <Link to="/incidentes">Ver todos</Link></p>
      ) : (
        <div className={s.lista}>{lista.map((i) => <FilaIncidente key={i.id} inc={i} ahora={ahora} />)}</div>
      )}
    </section>
  );
}
