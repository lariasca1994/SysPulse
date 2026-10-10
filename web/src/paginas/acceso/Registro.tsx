import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { UserPlus } from "lucide-react";
import { auth, type Nivel } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoPassword, MensajeError, Selector } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import { PAISES } from "./paises";
import s from "./acceso.module.css";
import seg from "./segmentado.module.css";

const NIVELES: { valor: Nivel; descripcion: string }[] = [
  { valor: "N1", descripcion: "Primer nivel: atiende y aplica runbooks" },
  { valor: "N2", descripcion: "Diagnóstico avanzado y causa raíz" },
  { valor: "N3", descripcion: "Especialista: cambios y errores conocidos" },
];

export default function Registro() {
  const navegar = useNavigate();
  const [nombre, setNombre] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmacion, setConfirmacion] = useState("");
  const [nivel, setNivel] = useState<Nivel | "">("");
  const [modo, setModo] = useState<"usuario" | "telefono">("usuario");
  const [usuarioTg, setUsuarioTg] = useState("");
  const [pais, setPais] = useState("CO");
  const [telefono, setTelefono] = useState("");
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  /** Al editar un campo, su error anterior desaparece (no se queda marcado hasta reenviar). */
  function limpiar(...campos: string[]) {
    setErrores((actuales) => Object.fromEntries(Object.entries(actuales).filter(([c]) => !campos.includes(c))));
  }

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setGeneral(null);
    const locales: Record<string, string> = {};
    if (password !== confirmacion) locales.confirmacion = "Las contraseñas no coinciden.";
    if (!nivel) locales.nivel = "Elige tu nivel: N1, N2 o N3.";
    if (Object.keys(locales).length) {
      setErrores(locales);
      return;
    }

    const prefijo = PAISES.find((p) => p.iso === pais)?.prefijo ?? "";
    const telegram = modo === "usuario" ? usuarioTg : `${prefijo} ${telefono}`;
    setCargando(true);
    try {
      const r = await auth.registro({ nombre, email, password, nivel: nivel as Nivel, telegram, pais });
      navegar("/registro/completar", { state: r });
    } catch (err) {
      if (!esFalla(err)) throw err;
      setErrores(err.detalle.campos);
      if (!Object.keys(err.detalle.campos).length) setGeneral(err.detalle.mensaje);
    } finally {
      setCargando(false);
    }
  }

  return (
    <LayoutAcceso paso="Crear cuenta" titulo="Únete a SysPulse" subtitulo="Tu cuenta queda activa al confirmar tu correo y tu Telegram.">
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {general && <Alerta tipo="error">{general}</Alerta>}

        <Campo etiqueta="Nombre completo" autoComplete="name" value={nombre} onChange={(e) => { setNombre(e.target.value); limpiar("nombre"); }} error={errores.nombre} />
        <Campo etiqueta="Correo" type="email" autoComplete="email" value={email} onChange={(e) => { setEmail(e.target.value); limpiar("email"); }} error={errores.email} placeholder="nombre@empresa.com" />

        <fieldset className={s.niveles} aria-describedby={errores.nivel ? "nivel-error" : undefined}>
          <legend>Tu nivel</legend>
          {NIVELES.map((n) => (
            <label key={n.valor} className={s.nivel}>
              <input type="radio" name="nivel" value={n.valor} checked={nivel === n.valor} onChange={() => { setNivel(n.valor); limpiar("nivel"); }} />
              <strong>{n.valor}</strong>
              <span>{n.descripcion}</span>
            </label>
          ))}
        </fieldset>
        {errores.nivel && <MensajeError id="nivel-error">{errores.nivel}</MensajeError>}

        <div className={seg.grupo}>
          <span className={seg.etiqueta} id="tg-etiqueta">Telegram (para recibir los avisos de tu nivel)</span>
          <div className={seg.segmentado} role="radiogroup" aria-labelledby="tg-etiqueta">
            <button type="button" role="radio" aria-checked={modo === "usuario"} className={seg.opcion} onClick={() => { setModo("usuario"); limpiar("telegram"); }}>@usuario</button>
            <button type="button" role="radio" aria-checked={modo === "telefono"} className={seg.opcion} onClick={() => { setModo("telefono"); limpiar("telegram"); }}>Número</button>
          </div>
        </div>

        {modo === "usuario" ? (
          <Campo etiqueta="Usuario de Telegram" value={usuarioTg} onChange={(e) => { setUsuarioTg(e.target.value); limpiar("telegram"); }} placeholder="@tu_usuario" autoComplete="off" error={errores.telegram} ayuda="Lo encuentras en Telegram → Ajustes → Nombre de usuario." />
        ) : (
          <div className={s.fila}>
            <Selector etiqueta="País" value={pais} onChange={(e) => setPais(e.target.value)}>
              {PAISES.map((p) => (
                <option key={p.iso} value={p.iso}>{p.nombre} ({p.prefijo})</option>
              ))}
            </Selector>
            <Campo etiqueta="Número" type="tel" inputMode="tel" autoComplete="tel-national" value={telefono} onChange={(e) => { setTelefono(e.target.value); limpiar("telegram"); }} placeholder="300 123 4567" error={errores.telegram} />
          </div>
        )}

        <CampoPassword etiqueta="Contraseña" autoComplete="new-password" medidor value={password} onChange={(e) => { setPassword(e.target.value); limpiar("password"); }} error={errores.password} ayuda="Mínimo 12 caracteres. Mejor una frase larga." />
        <CampoPassword etiqueta="Confirmar contraseña" autoComplete="new-password" value={confirmacion} onChange={(e) => { setConfirmacion(e.target.value); limpiar("confirmacion"); }} error={errores.confirmacion} />

        <Boton type="submit" ancho cargando={cargando}>
          {!cargando && <UserPlus size={18} aria-hidden="true" />} Crear cuenta
        </Boton>
        <p className={s.nota}>
          El rol de gestor no se elige aquí: se solicita después desde tu perfil y lo aprueba el administrador.
        </p>
      </form>
      <p className={s.alterno}>
        ¿Ya tienes cuenta? <Link to="/login">Inicia sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
