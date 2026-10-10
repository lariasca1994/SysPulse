import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { ArrowLeft, Mail } from "lucide-react";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

/** Solicitud de restablecimiento: la respuesta es la misma exista o no la cuenta (no revela correos registrados). */
export default function OlvidePassword() {
  const [email, setEmail] = useState("");
  const [resultado, setResultado] = useState<{ mensaje: string; enlaceDesarrollo: string | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      setResultado(await auth.olvidePassword(email));
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo enviar la solicitud.");
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso paso="Recuperar acceso" titulo="¿Olvidaste tu contraseña?"
      subtitulo="Te enviaremos un enlace a tu correo. Para tu seguridad, también te pediremos el código de tu app autenticadora.">
      {resultado ? (
        <div className={s.formulario}>
          <Alerta tipo="exito">{resultado.mensaje} El enlace vence en 1 hora.</Alerta>
          {resultado.enlaceDesarrollo && (
            <Alerta tipo="aviso">
              Modo desarrollo (correo simulado): <a className={s.enlaceDev} href={resultado.enlaceDesarrollo}>abrir el enlace</a>
            </Alerta>
          )}
          <p className={s.nota}>¿No llegó? Revisa la carpeta de spam o vuelve a intentarlo en unos minutos.</p>
        </div>
      ) : (
        <form className={s.formulario} onSubmit={enviar} noValidate>
          {error && <Alerta tipo="error">{error}</Alerta>}
          <Campo etiqueta="Correo de tu cuenta" type="email" autoComplete="username" required value={email}
            onChange={(e) => setEmail(e.target.value)} placeholder="nombre@empresa.com" />
          <Boton type="submit" ancho cargando={cargando} disabled={!email.includes("@")}>
            {!cargando && <Mail size={18} aria-hidden="true" />} Enviar enlace
          </Boton>
        </form>
      )}
      <p className={s.alterno}>
        <Link to="/login"><ArrowLeft size={14} aria-hidden="true" /> Volver a iniciar sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
