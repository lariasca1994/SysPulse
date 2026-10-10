import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { KeyRound } from "lucide-react";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoCodigo, CampoPassword } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

/** Enlace del correo + código de la app autenticadora (o de recuperación) + contraseña nueva. */
export default function Restablecer() {
  const navegar = useNavigate();
  const [parametros] = useSearchParams();
  const token = parametros.get("token");
  const [nueva, setNueva] = useState("");
  const [confirmacion, setConfirmacion] = useState("");
  const [codigo, setCodigo] = useState("");
  const [usarRecuperacion, setUsarRecuperacion] = useState(false);
  const [errorConfirmacion, setErrorConfirmacion] = useState<string | undefined>();
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  if (!token) return <Navigate to="/olvide" replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setErrorConfirmacion(undefined);
    if (nueva !== confirmacion) {
      setErrorConfirmacion("Las contraseñas no coinciden.");
      return;
    }
    setCargando(true);
    try {
      const r = await auth.restablecerPassword(token!, nueva, codigo);
      navegar("/login", { replace: true, state: { aviso: r.mensaje } });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo restablecer la contraseña.");
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso paso="Recuperar acceso" titulo="Crea una contraseña nueva"
      subtitulo="Al guardarla se cerrarán las sesiones abiertas en otros dispositivos.">
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {error && <Alerta tipo="error">{error}{error.includes("enlace") && <> <Link to="/olvide">Solicitar otro</Link></>}</Alerta>}
        <CampoPassword etiqueta="Nueva contraseña" autoComplete="new-password" medidor value={nueva}
          onChange={(e) => setNueva(e.target.value)} ayuda="Mínimo 12 caracteres. Mejor una frase larga." />
        <CampoPassword etiqueta="Confirmar contraseña" autoComplete="new-password" value={confirmacion}
          onChange={(e) => { setConfirmacion(e.target.value); setErrorConfirmacion(undefined); }} error={errorConfirmacion} />
        {usarRecuperacion ? (
          <Campo etiqueta="Código de recuperación" value={codigo} onChange={(e) => setCodigo(e.target.value)}
            autoComplete="off" placeholder="XXXX-XXXX" ayuda="Uno de los 10 códigos que guardaste al activar el MFA. Cada uno sirve una sola vez." />
        ) : (
          <CampoCodigo etiqueta="Código de tu app autenticadora" value={codigo} onChange={setCodigo} />
        )}
        <button type="button" className={s.alternarCodigo} onClick={() => { setUsarRecuperacion(!usarRecuperacion); setCodigo(""); }}>
          {usarRecuperacion ? "Usar el código de la app autenticadora" : "No tengo la app: usar un código de recuperación"}
        </button>
        <Boton type="submit" ancho cargando={cargando} disabled={!nueva || !confirmacion || codigo.trim().length < 6}>
          {!cargando && <KeyRound size={18} aria-hidden="true" />} Guardar contraseña
        </Boton>
      </form>
      <p className={s.alterno}>
        <Link to="/login">Volver a iniciar sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
