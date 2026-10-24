import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { LogIn } from "lucide-react";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoPassword } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

export default function Login() {
  const navegar = useNavigate();
  const aviso = (useLocation().state as { aviso?: string } | null)?.aviso;
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = await auth.login(email, password);
      const destino = r.paso === "cambiar-password" ? "/cambiar-password" : r.paso === "configurar-mfa" ? "/mfa/configurar" : "/mfa";
      navegar(destino, { state: { token: r.token, metodos: r.paso === "mfa" ? r.metodos : undefined } });
    } catch (err) {
      if (!esFalla(err)) throw err;
      const paso = err.detalle.datos?.paso;
      if (paso === "verificar-telegram") {
        navegar("/registro/completar", { state: { usuarioId: err.detalle.datos?.usuarioId, desdeLogin: true } });
        return;
      }
      setError(err.detalle.mensaje);
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso titulo="Inicia sesión" subtitulo="Ingresa con tu correo y contraseña. Luego te pediremos el código de tu app autenticadora.">
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {aviso && <Alerta tipo="exito">{aviso}</Alerta>}
        {error && <Alerta tipo="error">{error}</Alerta>}
        <Campo etiqueta="Correo" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} placeholder="nombre@empresa.com" />
        <CampoPassword etiqueta="Contraseña" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        <Link to="/olvide" className={s.olvide}>¿Olvidaste tu contraseña?</Link>
        <Boton type="submit" ancho cargando={cargando} disabled={!email || !password}>
          {!cargando && <LogIn size={18} aria-hidden="true" />} Continuar
        </Boton>
      </form>
      <p className={s.alterno}>
        ¿No tienes cuenta? <Link to="/registro">Regístrate</Link>
      </p>
    </LayoutAcceso>
  );
}
