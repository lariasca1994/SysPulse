import { useId, useState, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from "react";
import { AlertCircle, Eye, EyeOff } from "lucide-react";
import { ETIQUETA_FUERZA, fuerzaPassword } from "./fuerza";
import s from "./ui.module.css";

interface PropsBase {
  etiqueta: string;
  error?: string;
  ayuda?: ReactNode;
  opcional?: boolean;
}

function Contenedor({ id, etiqueta, error, ayuda, opcional, children }: PropsBase & { id: string; children: ReactNode }) {
  return (
    <div className={s.campo}>
      <label htmlFor={id} className={s.etiqueta}>
        {etiqueta} {opcional && <span className={s.opcional}>(opcional)</span>}
      </label>
      {children}
      {error ? (
        <p id={`${id}-error`} className={s.error} role="alert">
          <AlertCircle size={14} aria-hidden="true" /> {error}
        </p>
      ) : (
        ayuda && <p id={`${id}-ayuda`} className={s.ayuda}>{ayuda}</p>
      )}
    </div>
  );
}

export function Campo({ etiqueta, error, ayuda, opcional, className, ...input }: PropsBase & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  return (
    <Contenedor id={id} etiqueta={etiqueta} error={error} ayuda={ayuda} opcional={opcional}>
      <input
        id={id}
        className={`${s.control} ${className ?? ""}`}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : ayuda ? `${id}-ayuda` : undefined}
        {...input}
      />
    </Contenedor>
  );
}

export function Selector({ etiqueta, error, ayuda, children, ...select }: PropsBase & SelectHTMLAttributes<HTMLSelectElement>) {
  const id = useId();
  return (
    <Contenedor id={id} etiqueta={etiqueta} error={error} ayuda={ayuda}>
      <select id={id} className={s.control} aria-invalid={error ? true : undefined} {...select}>
        {children}
      </select>
    </Contenedor>
  );
}

/** Texto de varias líneas (motivos, comentarios, soluciones). */
export function AreaTexto({ etiqueta, error, ayuda, opcional, className, ...area }: PropsBase & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const id = useId();
  return (
    <Contenedor id={id} etiqueta={etiqueta} error={error} ayuda={ayuda} opcional={opcional}>
      <textarea
        id={id}
        className={`${s.control} ${s.area} ${className ?? ""}`}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : ayuda ? `${id}-ayuda` : undefined}
        {...area}
      />
    </Contenedor>
  );
}

/** Mensaje de error suelto (para grupos como radios, que no usan <Campo>). */
export function MensajeError({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <p id={id} className={s.error} role="alert">
      <AlertCircle size={14} aria-hidden="true" /> {children}
    </p>
  );
}


export function CampoPassword({
  etiqueta,
  error,
  ayuda,
  medidor = false,
  value,
  ...input
}: PropsBase & InputHTMLAttributes<HTMLInputElement> & { medidor?: boolean }) {
  const id = useId();
  const [visible, setVisible] = useState(false);
  const nivel = medidor ? fuerzaPassword(String(value ?? "")) : 0;
  return (
    <Contenedor
      id={id}
      etiqueta={etiqueta}
      error={error}
      ayuda={medidor && nivel > 0 ? `Seguridad: ${ETIQUETA_FUERZA[nivel]}. ${typeof ayuda === "string" ? ayuda : ""}` : ayuda}
    >
      <div className={s.envoltura}>
        <input
          id={id}
          type={visible ? "text" : "password"}
          className={`${s.control} ${s.conBoton}`}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${id}-error` : `${id}-ayuda`}
          value={value}
          {...input}
        />
        <button
          type="button"
          className={s.botonInterno}
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? "Ocultar contraseña" : "Mostrar contraseña"}
          aria-pressed={visible}
        >
          {visible ? <EyeOff size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}
        </button>
      </div>
      {medidor && (
        <div className={s.fuerza} data-nivel={nivel} aria-hidden="true">
          <span className={s.segmento} />
          <span className={s.segmento} />
          <span className={s.segmento} />
          <span className={s.segmento} />
        </div>
      )}
    </Contenedor>
  );
}

/** Código de 6 dígitos (Telegram o app autenticadora). Pega/autocompleta el código de un solo uso. */
export function CampoCodigo({ etiqueta, error, ayuda, value, onChange, ...input }: PropsBase & Omit<InputHTMLAttributes<HTMLInputElement>, "onChange"> & { onChange: (v: string) => void }) {
  const id = useId();
  return (
    <Contenedor id={id} etiqueta={etiqueta} error={error} ayuda={ayuda}>
      <input
        id={id}
        className={`${s.control} ${s.codigo}`}
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={6}
        placeholder="••••••"
        aria-invalid={error ? true : undefined}
        value={value}
        onChange={(e) => onChange(e.target.value.replace(/\D/g, "").slice(0, 6))}
        {...input}
      />
    </Contenedor>
  );
}
