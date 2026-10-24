import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router";
import { ShieldCheck } from "lucide-react";
import { auth, type MetodosMfa } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

type Modo = "app" | "correo" | "recuperacion";

const SUBTITULOS: Record<Modo, string> = {
  app: "Abre tu app autenticadora y escribe el código de SysPulse.",
  correo: "Escribe el código de 6 dígitos que te enviamos por correo.",
  recuperacion: "Escribe uno de tus códigos de recuperación. Cada uno sirve una sola vez.",
};

export default function VerificarMfa() {
  const navegar = useNavigate();
  const { iniciar } = useSesion();
  const estado = useLocation().state as { token?: string; metodos?: MetodosMfa } | null;
  const token = estado?.token;
  // Sin datos de métodos (enlace antiguo) se asume la app, como antes.
  const metodos: MetodosMfa = estado?.metodos ?? { app: true, correo: false, preferido: "app" };
  const [modo, setModo] = useState<Modo>(metodos.preferido ?? "app");
  const [codigo, setCodigo] = useState("");
  const [aviso, setAviso] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const enviado = useRef(false);

  async function enviarCorreo() {
    setError(null);
    try {
      setAviso((await auth.enviarCodigoCorreo(token!)).mensaje);
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo enviar el código.");
    }
  }

  // Con el correo, el código se envía al abrir este paso (una vez; luego con "Enviar un código nuevo").
  useEffect(() => {
    if (!token || modo !== "correo" || enviado.current) return;
    enviado.current = true;
    void enviarCorreo();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [modo, token]);

  if (!token) return <Navigate to="/login" replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = await auth.verificarMfa(token!, codigo, modo === "recuperacion" ? undefined : modo);
      iniciar(r.sesion);
      navegar("/", { replace: true });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo verificar.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  function cambiar(nuevo: Modo) {
    setModo(nuevo);
    setCodigo("");
    setError(null);
    setAviso(null);
  }

  const listo = modo === "recuperacion" ? codigo.trim().length >= 9 : codigo.length === 6;

  return (
    <LayoutAcceso paso="Segundo factor" titulo="Ingresa tu código" subtitulo={SUBTITULOS[modo]}>
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {aviso && modo === "correo" && <Alerta tipo="info">{aviso}</Alerta>}
        {error && <Alerta tipo="error">{error}</Alerta>}
        {modo === "recuperacion" ? (
          <Campo etiqueta="Código de recuperación" value={codigo} onChange={(e) => setCodigo(e.target.value.toUpperCase())} placeholder="ABCD-EFGH" autoComplete="off" autoFocus />
        ) : (
          <CampoCodigo etiqueta="Código de 6 dígitos" value={codigo} onChange={setCodigo} autoFocus />
        )}
        <Boton type="submit" ancho cargando={cargando} disabled={!listo}>
          {!cargando && <ShieldCheck size={18} aria-hidden="true" />} Verificar
        </Boton>
        {modo === "correo" && (
          <Boton type="button" variante="fantasma" onClick={() => void enviarCorreo()}>Enviar un código nuevo</Boton>
        )}
        {modo !== "app" && metodos.app && (
          <Boton type="button" variante="fantasma" onClick={() => cambiar("app")}>Usar el código de la app</Boton>
        )}
        {modo !== "correo" && metodos.correo && (
          <Boton type="button" variante="fantasma" onClick={() => { enviado.current = false; cambiar("correo"); }}>Usar un código por correo</Boton>
        )}
        {modo !== "recuperacion" && (
          <Boton type="button" variante="fantasma" onClick={() => cambiar("recuperacion")}>
            No tengo acceso a mi segundo factor: usar un código de recuperación
          </Boton>
        )}
      </form>
      <p className={s.alterno}>
        <Link to="/login">Volver al inicio de sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
