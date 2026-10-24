import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoPassword } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

/** Primer ingreso del admin semilla: reemplazar la contraseña temporal. */
export default function CambiarPassword() {
  const navegar = useNavigate();
  const token = (useLocation().state as { token?: string } | null)?.token;
  const [nueva, setNueva] = useState("");
  const [confirmacion, setConfirmacion] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [errorCampo, setErrorCampo] = useState<string | undefined>();
  const [cargando, setCargando] = useState(false);

  if (!token) return <Navigate to="/login" replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setErrorCampo(undefined);
    if (nueva !== confirmacion) {
      setErrorCampo("Las contraseñas no coinciden.");
      return;
    }
    setCargando(true);
    try {
      const r = await auth.cambiarPassword(token!, nueva);
      navegar(r.paso === "mfa" ? "/mfa" : "/mfa/configurar", { state: { token: r.token, metodos: r.paso === "mfa" ? r.metodos : undefined }, replace: true });
    } catch (err) {
      if (!esFalla(err)) throw err;
      if (err.detalle.campos.nueva) setErrorCampo(err.detalle.campos.nueva);
      else setError(err.detalle.mensaje);
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso paso="Seguridad" titulo="Crea tu contraseña" subtitulo="Ingresaste con una contraseña temporal. Reemplázala por una propia antes de continuar.">
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {error && <Alerta tipo="error">{error}</Alerta>}
        <CampoPassword etiqueta="Nueva contraseña" autoComplete="new-password" medidor value={nueva} onChange={(e) => setNueva(e.target.value)} error={errorCampo} ayuda="Mínimo 12 caracteres." />
        <CampoPassword etiqueta="Confirmar contraseña" autoComplete="new-password" value={confirmacion} onChange={(e) => setConfirmacion(e.target.value)} />
        <Boton type="submit" ancho cargando={cargando} disabled={!nueva || !confirmacion}>Guardar y continuar</Boton>
      </form>
    </LayoutAcceso>
  );
}
