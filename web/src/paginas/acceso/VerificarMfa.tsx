import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router";
import { ShieldCheck, Smartphone } from "lucide-react";
import { auth, type MetodosMfa, type Sesion } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import { AnilloCodigo } from "./AnilloCodigo";
import type { EstadoCandado } from "./Candado";
import { CodigosRecuperacion } from "./CodigosRecuperacion";
import { hayCodigosNuevos } from "./recuperacion";
import s from "./acceso.module.css";

type Modo = "app" | "correo" | "recuperacion";

const SUBTITULOS: Record<Modo, string> = {
  app: "Abre tu app autenticadora y escribe el código de 6 dígitos.",
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
  // Candado sobre el título: se abre con el código correcto y se sacude con uno equivocado.
  const [candado, setCandado] = useState<EstadoCandado>("normal");
  const enviado = useRef(false);
  // Tras "Olvidé mi contraseña" la API entrega aquí, una sola vez, los códigos de recuperación nuevos.
  const [nuevos, setNuevos] = useState<{ sesion: Sesion; codigos: string[] } | null>(null);

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
    setCandado("verificando");
    try {
      const r = await auth.verificarMfa(token!, codigo, modo === "recuperacion" ? undefined : modo);
      setCandado("abierto");
      // El candado se abre (150 ms) antes de seguir.
      await new Promise((listo) => window.setTimeout(listo, window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 150));
      if (hayCodigosNuevos(r.codigosRecuperacion)) {
        setNuevos({ sesion: r.sesion, codigos: r.codigosRecuperacion });
        return;
      }
      iniciar(r.sesion);
      navegar("/", { replace: true });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo verificar.");
      setCodigo("");
      setCandado("error");
      window.setTimeout(() => setCandado("normal"), 700);
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

  if (nuevos) {
    return (
      <LayoutAcceso paso="Contraseña restablecida" titulo="Tus códigos de recuperación nuevos"
        subtitulo="Al restablecer tu contraseña se generaron códigos nuevos. Cada uno te permite entrar una vez si pierdes tu segundo factor.">
        <CodigosRecuperacion codigos={nuevos.codigos} textoContinuar="Entrar a la plataforma"
          alContinuar={() => { iniciar(nuevos.sesion); navegar("/", { replace: true }); }} />
      </LayoutAcceso>
    );
  }

  return (
    <LayoutAcceso paso="Segundo factor" titulo="Ingresa tu código" subtitulo={SUBTITULOS[modo]} candado={candado}>
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {aviso && modo === "correo" && <Alerta tipo="info">{aviso}</Alerta>}
        {error && <Alerta tipo="error">{error}</Alerta>}
        {modo === "recuperacion" ? (
          <Campo etiqueta="Código de recuperación" value={codigo} onChange={(e) => setCodigo(e.target.value.toUpperCase())} placeholder="ABCD-EFGH" autoComplete="off" autoFocus />
        ) : (
          <CampoCodigo etiqueta="Código de 6 dígitos" value={codigo} onChange={setCodigo} autoFocus />
        )}
        {modo === "app" && <AnilloCodigo />}
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
        <Link className={s.perdiAutenticador} to="/mfa/recuperar" state={{ token, metodos }}>
          <Smartphone size={16} aria-hidden="true" /> Perdí mi autenticador
        </Link>
      </form>
      <p className={s.alterno}>
        <Link to="/login">Volver al inicio de sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
