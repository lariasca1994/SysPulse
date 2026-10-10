import { useEffect, useState } from "react";
import { Monitor, Moon, Sun } from "lucide-react";
import { aplicarTema, escucharSistema, leerPreferencia, type Preferencia } from "../theme";
import s from "./BotonTema.module.css";

const SIGUIENTE: Record<Preferencia, Preferencia> = { sistema: "claro", claro: "oscuro", oscuro: "sistema" };
const ETIQUETA: Record<Preferencia, string> = { sistema: "Tema del sistema", claro: "Tema claro", oscuro: "Tema oscuro" };

/** Alterna sistema → claro → oscuro; la preferencia se recuerda en este navegador. */
export function BotonTema() {
  const [tema, setTema] = useState<Preferencia>(leerPreferencia);

  useEffect(() => {
    aplicarTema(tema);
    if (tema !== "sistema") return;
    return escucharSistema(() => aplicarTema("sistema"));
  }, [tema]);

  const Icono = tema === "oscuro" ? Moon : tema === "claro" ? Sun : Monitor;
  return (
    <button
      type="button"
      className={s.boton}
      onClick={() => setTema(SIGUIENTE[tema])}
      aria-label={`${ETIQUETA[tema]}. Cambiar tema`}
      title={ETIQUETA[tema]}
    >
      <Icono size={18} aria-hidden="true" />
    </button>
  );
}
