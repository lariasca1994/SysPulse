import { useState, type FormEvent } from "react";
import { KeyRound, Mail, Send, ShieldCheck, UserRound } from "lucide-react";
import { auth } from "../api/auth";
import { esFalla } from "../api/cliente";
import { useSesion } from "../sesionContexto";
import { Alerta, Boton } from "../componentes/ui/Basicos";
import { CampoPassword } from "../componentes/ui/Campos";
import h from "./hosts/hosts.module.css";
import { CambiarAutenticador } from "./CambiarAutenticador";
import s from "./Perfil.module.css";

const ROL: Record<string, string> = {
  N1: "Técnico N1", N2: "Técnico N2", N3: "Técnico N3", gestor: "Gestor del servicio", admin: "Administrador",
};

export default function Perfil() {
  const { sesion, usuario, iniciar } = useSesion();
  const [actual, setActual] = useState("");
  const [nueva, setNueva] = useState("");
  const [confirmacion, setConfirmacion] = useState("");
  const [errores, setErrores] = useState<{ actual?: string; nueva?: string; confirmacion?: string }>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [exito, setExito] = useState(false);
  const [cargando, setCargando] = useState(false);

  if (!usuario || !sesion) return null;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setGeneral(null);
    setExito(false);
    if (nueva !== confirmacion) {
      setErrores({ confirmacion: "Las contraseñas no coinciden." });
      return;
    }
    setErrores({});
    setCargando(true);
    try {
      const r = await auth.cambiarPasswordConSesion(sesion!.token, actual, nueva);
      // La API cerró las demás sesiones: esta continúa con el token nuevo.
      iniciar(r.sesion);
      setActual("");
      setNueva("");
      setConfirmacion("");
      setExito(true);
    } catch (err) {
      if (!esFalla(err)) throw err;
      const campos = err.detalle.campos;
      if (campos.actual || campos.nueva) setErrores({ actual: campos.actual, nueva: campos.nueva });
      else setGeneral(err.detalle.mensaje);
    } finally {
      setCargando(false);
    }
  }

  const iniciales = usuario.nombre.split(" ").filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();

  return (
    <>
      <div className={h.encabezado}>
        <div>
          <h1 className={h.titulo}>Mi cuenta</h1>
          <p className={h.subtitulo}>Tus datos y la seguridad de tu acceso.</p>
        </div>
      </div>

      <div className={s.rejilla}>
        <section className={s.panel} aria-labelledby="datos">
          <div className={s.identidad}>
            <span className={s.avatar} aria-hidden="true">{iniciales}</span>
            <div>
              <h2 id="datos" className={s.nombre}>{usuario.nombre}</h2>
              <p className={s.rol} data-rol={usuario.rol}>{ROL[usuario.rol] ?? usuario.rol}</p>
            </div>
          </div>
          <dl className={s.datos}>
            <div><dt><Mail size={15} aria-hidden="true" /> Correo</dt><dd>{usuario.email}</dd></div>
            <div><dt><Send size={15} aria-hidden="true" /> Telegram</dt><dd>{usuario.telegram ?? "—"} {usuario.telegramVerificado && <span className={s.verificado}>verificado</span>}</dd></div>
            <div><dt><ShieldCheck size={15} aria-hidden="true" /> Verificación en dos pasos</dt><dd><span className={s.verificado}>activa</span></dd></div>
            <div><dt><UserRound size={15} aria-hidden="true" /> Nivel</dt><dd>{ROL[usuario.rol] ?? usuario.rol}</dd></div>
          </dl>
          {usuario.rol !== "admin" && usuario.rol !== "gestor" && (
            <p className={s.nota}>El nivel se elige al registrarse. El rol de gestor lo aprueba el administrador.</p>
          )}
        </section>

        <section className={s.panel} aria-labelledby="password">
          <h2 id="password" className={s.tituloPanel}><KeyRound size={18} aria-hidden="true" /> Cambiar contraseña</h2>
          <p className={s.nota}>Al cambiarla se cierran tus sesiones en otros dispositivos y recibes un aviso por Telegram.</p>
          <form className={s.formulario} onSubmit={enviar} noValidate>
            {exito && <Alerta tipo="exito">Contraseña actualizada. Las demás sesiones quedaron cerradas.</Alerta>}
            {general && <Alerta tipo="error">{general}</Alerta>}
            <CampoPassword etiqueta="Contraseña actual" autoComplete="current-password" value={actual}
              onChange={(e) => { setActual(e.target.value); setErrores((x) => ({ ...x, actual: undefined })); }} error={errores.actual} />
            <CampoPassword etiqueta="Nueva contraseña" autoComplete="new-password" medidor value={nueva}
              onChange={(e) => { setNueva(e.target.value); setErrores((x) => ({ ...x, nueva: undefined })); }} error={errores.nueva}
              ayuda="Mínimo 12 caracteres. Mejor una frase larga." />
            <CampoPassword etiqueta="Confirmar nueva contraseña" autoComplete="new-password" value={confirmacion}
              onChange={(e) => { setConfirmacion(e.target.value); setErrores((x) => ({ ...x, confirmacion: undefined })); }} error={errores.confirmacion} />
            <div className={s.acciones}>
              <Boton type="submit" cargando={cargando} disabled={!actual || !nueva || !confirmacion}>Guardar contraseña</Boton>
            </div>
          </form>
        </section>
        <CambiarAutenticador />
      </div>
    </>
  );
}
