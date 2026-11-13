import { useEffect, useRef, type ButtonHTMLAttributes, type PointerEvent, type ReactNode } from "react";
import { AlertCircle, AlertTriangle, CheckCircle2, Info } from "lucide-react";
import s from "./ui.module.css";

type Variante = "primario" | "secundario" | "fantasma";

const reducir = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Onda desde el punto exacto del toque (150 ms, regla 8). */
function onda(e: PointerEvent<HTMLButtonElement>) {
  const b = e.currentTarget;
  if (b.disabled || reducir()) return;
  const r = b.getBoundingClientRect();
  const o = document.createElement("span");
  o.className = s.onda;
  o.style.setProperty("--x", `${e.clientX - r.left}px`);
  o.style.setProperty("--y", `${e.clientY - r.top}px`);
  o.style.setProperty("--d", `${Math.max(r.width, r.height) * 2.2}px`);
  b.appendChild(o);
  o.addEventListener("animationend", () => o.remove());
}

export function Boton({
  variante = "primario",
  cargando = false,
  ancho = false,
  children,
  disabled,
  className,
  onPointerDown,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variante?: Variante; cargando?: boolean; ancho?: boolean }) {
  return (
    <button
      className={`${s.boton} ${s[variante]} ${ancho ? s.ancho : ""} ${className ?? ""}`}
      disabled={disabled || cargando}
      aria-busy={cargando || undefined}
      onPointerDown={(e) => { onda(e); onPointerDown?.(e); }}
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
  const caja = useRef<HTMLDivElement>(null);
  // Un error hace temblar el botón de envío de su formulario (el mensaje siempre va en texto).
  useEffect(() => {
    if (tipo !== "error" || reducir()) return;
    const boton = caja.current?.closest("form")?.querySelector<HTMLButtonElement>('button[type="submit"]');
    if (!boton) return;
    boton.classList.remove(s.temblor);
    void boton.offsetWidth;
    boton.classList.add(s.temblor);
  }, [tipo, children]);
  return (
    <div ref={caja} className={s.alerta} data-tipo={tipo} role={tipo === "error" ? "alert" : "status"}>
      <Icono size={18} aria-hidden="true" />
      <div>{children}</div>
    </div>
  );
}
