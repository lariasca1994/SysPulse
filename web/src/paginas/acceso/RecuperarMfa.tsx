import { useState, type FormEvent } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router";
import { ArrowLeft, Mail, ShieldCheck } from "lucide-react";
import { auth, type MetodosMfa } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import { esCodigoSeisDigitos } from "./recuperacion";
import s from "./acceso.module.css";

/**
 * "Perdí mi autenticador": envía un código al correo de la cuenta (aunque el código por correo no esté activado).
 * Con el código correcto se pasa a configurar el autenticador nuevo (QR) y se entregan códigos de recuperación nuevos.
 */
export default function RecuperarMfa() {
  const navegar = useNavigate();
  const estado = useLocation().state as { token?: string; metodos?: MetodosMfa } | null;
  const [token, setToken] = useState(estado?.token);
  const [enviado, setEnviado] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  if (!token) return <Navigate to="/login" replace />;

  async function enviarCodigo() {
    setError(null);
    setCargando(true);
    try {
      const r = await auth.recuperarMfaEnviar(token!);
      setToken(r.token);
      setAviso(r.mensaje);
      setEnviado(true);
      setCodigo("");
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo enviar el código.");
    } finally {
      setCargando(false);
    }
  }

  async function verificar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = await auth.recuperarMfaVerificar(token!, codigo);
      navegar("/mfa/configurar", { replace: true, state: { token: r.token, reconfigurar: true } });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo verificar el código.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  const volver = (
    <p className={s.alterno}>
      <Link to="/mfa" state={estado} replace><ArrowLeft size={14} aria-hidden="true" /> Volver: tengo mi autenticador o un código de recuperación</Link>
    </p>
  );

  if (!enviado) {
    return (
      <LayoutAcceso paso="Perdí mi autenticador" titulo="Configura un autenticador nuevo"
        subtitulo="Te enviaremos un código de 6 dígitos al correo de tu cuenta. Con él configurarás tu app autenticadora en el teléfono nuevo.">
        <div className={s.formulario}>
          {error && <Alerta tipo="error">{error}</Alerta>}
          <Alerta tipo="info">
            El código vence en 15 minutos. Al terminar recibirás códigos de recuperación nuevos y se cerrarán tus otras sesiones.
            Si aún tienes un código de recuperación, también puedes usarlo para entrar.
          </Alerta>
          <Boton type="button" ancho cargando={cargando} onClick={() => void enviarCodigo()}>
            {!cargando && <Mail size={18} aria-hidden="true" />} Enviar código a mi correo
          </Boton>
        </div>
        {volver}
      </LayoutAcceso>
    );
  }

  return (
    <LayoutAcceso paso="Perdí mi autenticador" titulo="Revisa tu correo"
      subtitulo="Escribe el código de 6 dígitos que te enviamos. Sirve una sola vez.">
      <form className={s.formulario} onSubmit={verificar} noValidate>
        {aviso && <Alerta tipo="info">{aviso}</Alerta>}
        {error && <Alerta tipo="error">{error}</Alerta>}
        <CampoCodigo etiqueta="Código del correo" value={codigo} onChange={setCodigo} autoFocus />
        <Boton type="submit" ancho cargando={cargando} disabled={!esCodigoSeisDigitos(codigo)}>
          {!cargando && <ShieldCheck size={18} aria-hidden="true" />} Confirmar código
        </Boton>
        <Boton type="button" variante="fantasma" disabled={cargando} onClick={() => void enviarCodigo()}>Enviar un código nuevo</Boton>
      </form>
      {volver}
    </LayoutAcceso>
  );
}
