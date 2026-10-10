import type { ButtonHTMLAttributes, ReactNode } from "react";
import { AlertCircle, AlertTriangle, CheckCircle2, Info } from "lucide-react";
import s from "./ui.module.css";

type Variante = "primario" | "secundario" | "fantasma";

export function Boton({
  variante = "primario",
  cargando = false,
  ancho = false,
  children,
  disabled,
  className,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variante?: Variante; cargando?: boolean; ancho?: boolean }) {
  return (
    <button
      className={`${s.boton} ${s[variante]} ${ancho ? s.ancho : ""} ${className ?? ""}`}
      disabled={disabled || cargando}
      aria-busy={cargando || undefined}
      {...props}
    >
      {cargando && <span className={s.giro} aria-hidden="true" />}
      {children}
    </button>
  );
}

type TipoAlerta = "error" | "exito" | "info" | "aviso";
const ICONOS = { error: AlertCircle, exito: CheckCircle2, info: Info, aviso: AlertTriangle };

export function Alerta({ tipo, children }: { tipo: TipoAlerta; children: ReactNode }) {
  const Icono = ICONOS[tipo];
  return (
    <div className={s.alerta} data-tipo={tipo} role={tipo === "error" ? "alert" : "status"}>
      <Icono size={18} aria-hidden="true" />
      <div>{children}</div>
    </div>
  );
}
