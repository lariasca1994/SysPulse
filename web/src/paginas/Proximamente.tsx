import { Link } from "react-router";
import { ArrowRight, BarChart3, Check, MessagesSquare, type LucideIcon } from "lucide-react";
import s from "./Proximamente.module.css";

interface Seccion {
  icono: LucideIcon;
  titulo: string;
  descripcion: string;
  incluye: string[];
}

const SECCIONES: Record<string, Seccion> = {
  salas: {
    icono: MessagesSquare,
    titulo: "Salas",
    descripcion: "Chat en tiempo real por incidente para coordinar al equipo sin salir de SysPulse.",
    incluye: [
      "Una sala por incidente abierto",
      "Mensajes en vivo y registro para el informe posterior",
    ],
  },
  analitica: {
    icono: BarChart3,
    titulo: "Analítica",
    descripcion: "Tendencias de los hosts y cumplimiento del SLA en el tiempo.",
    incluye: [
      "Cumplimiento de SLA por prioridad y nivel",
      "Hosts con más alertas y métricas con más incidencias",
      "Uso de recursos a 7, 30 y 90 días",
    ],
  },
};

export default function Proximamente({ seccion }: { seccion: keyof typeof SECCIONES }) {
  const { icono: Icono, titulo, descripcion, incluye } = SECCIONES[seccion];
  return (
    <>
      <h1 className={s.titulo}>{titulo}</h1>
      <section className={s.panel}>
        <span className={s.icono} aria-hidden="true">
          <Icono size={28} />
        </span>
        <span className={s.etiqueta}>Próxima fase</span>
        <p className={s.descripcion}>{descripcion}</p>
        <ul className={s.lista}>
          {incluye.map((i) => (
            <li key={i}>
              <Check size={16} aria-hidden="true" /> {i}
            </li>
          ))}
        </ul>
        <Link to="/hosts" className={s.enlace}>
          Ver la salud de los hosts <ArrowRight size={16} aria-hidden="true" />
        </Link>
      </section>
    </>
  );
}
