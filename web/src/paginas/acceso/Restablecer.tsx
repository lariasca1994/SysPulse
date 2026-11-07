import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { KeyRound } from "lucide-react";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoPassword } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import { errorConfirmacion, puedeGuardarPassword } from "./recuperacion";
import s from "./acceso.module.css";

/** Paso 2 de "Olvidé mi contraseña": el enlace del correo (un solo uso) + contraseña nueva y su confirmación. */
export default function Restablecer() {
  const navegar = useNavigate();
  const [parametros] = useSearchParams();
  const token = parametros.get("token");
  const [nueva, setNueva] = useState("");
  const [confirmacion, setConfirmacion] = useState("");
  const [mostrarError, setMostrarError] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  if (!token) return <Navigate to="/olvide" replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setMostrarError(true);
    if (!puedeGuardarPassword(nueva, confirmacion)) return;
    setCargando(true);
    try {
      const r = await auth.restablecerPassword(token!, nueva, confirmacion);
      navegar("/login", { replace: true, state: { aviso: r.mensaje } });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo restablecer la contraseña.");
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso paso="Recuperar acceso" titulo="Crea una contraseña nueva"
      subtitulo="Al guardarla se cerrarán todas tus sesiones abiertas. Tu verificación en dos pasos sigue activa.">
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {error && <Alerta tipo="error">{error}{error.includes("enlace") && <> <Link to="/olvide">Solicitar otro</Link></>}</Alerta>}
        <CampoPassword etiqueta="Nueva contraseña" autoComplete="new-password" medidor value={nueva}
          onChange={(e) => setNueva(e.target.value)} ayuda="Mínimo 12 caracteres. Mejor una frase larga." />
        <CampoPassword etiqueta="Confirmar contraseña" autoComplete="new-password" value={confirmacion}
          onChange={(e) => { setConfirmacion(e.target.value); setMostrarError(false); }}
          error={mostrarError ? errorConfirmacion(nueva, confirmacion) : undefined} />
        <Boton type="submit" ancho cargando={cargando} disabled={!nueva || !confirmacion}>
          {!cargando && <KeyRound size={18} aria-hidden="true" />} Guardar contraseña
        </Boton>
      </form>
      <p className={s.alterno}>
        <Link to="/login">Volver a iniciar sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
