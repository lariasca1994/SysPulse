import { useEffect, useState, type FormEvent } from "react";
import QRCode from "qrcode";
import { Mail, ShieldCheck, Smartphone } from "lucide-react";
import { auth, type MetodosMfa } from "../api/auth";
import { esFalla } from "../api/cliente";
import { useSesion } from "../sesionContexto";
import { Alerta, Boton } from "../componentes/ui/Basicos";
import { CampoCodigo } from "../componentes/ui/Campos";
import { CambiarAutenticador } from "./CambiarAutenticador";
import a from "./acceso/acceso.module.css";
import s from "./Perfil.module.css";

type Agregando = "app" | "correo" | null;

/**
 * Verificación en dos pasos (obligatoria) en Mi cuenta: métodos activos, agregar el que falta (app autenticadora o
 * código por correo) y elegir cuál se pide por defecto. Con la app activa, permite pasarla a otro teléfono.
 */
export function MetodosVerificacion() {
  const { sesion } = useSesion();
  const [metodos, setMetodos] = useState<MetodosMfa | null>(null);
  const [agregando, setAgregando] = useState<Agregando>(null);
  const [secreto, setSecreto] = useState("");
  const [qr, setQr] = useState("");
  const [codigo, setCodigo] = useState("");
  const [aviso, setAviso] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  useEffect(() => {
    if (!sesion) return;
    auth.metodosMfa(sesion.token).then(setMetodos).catch(() => setError("No se pudieron cargar tus métodos de verificación."));
  }, [sesion]);

  if (!sesion) return null;

  function reiniciar() {
    setAgregando(null);
    setSecreto("");
    setQr("");
    setCodigo("");
    setAviso(null);
    setError(null);
  }

  async function empezar(metodo: "app" | "correo") {
    reiniciar();
    setExito(null);
    setAgregando(metodo);
    try {
      if (metodo === "app") {
        const r = await auth.agregarAppIniciar(sesion!.token);
        setSecreto(r.secreto);
        setQr(await QRCode.toDataURL(r.uri, { margin: 1, width: 368, color: { dark: "#0B1F3A", light: "#FFFFFF" } }));
      } else {
        setAviso((await auth.agregarCorreoEnviar(sesion!.token)).mensaje);
      }
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo iniciar.");
    }
  }

  async function activar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = agregando === "app"
        ? await auth.agregarAppActivar(sesion!.token, codigo)
        : await auth.agregarCorreoActivar(sesion!.token, codigo);
      setMetodos(r);
      setExito(agregando === "app" ? "La app autenticadora quedó activa." : "El código por correo quedó activo.");
      reiniciar();
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo activar.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  async function preferir(metodo: "app" | "correo") {
    setError(null);
    try {
      setMetodos(await auth.preferirMfa(sesion!.token, metodo));
      setExito(metodo === "app" ? "Ahora se pide la app autenticadora por defecto." : "Ahora se pide el código por correo por defecto.");
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo guardar.");
    }
  }

  return (
    <>
      <section className={s.panel} aria-labelledby="dos-pasos">
        <h2 id="dos-pasos" className={s.tituloPanel}><ShieldCheck size={18} aria-hidden="true" /> Verificación en dos pasos</h2>
        <p className={s.nota}>Obligatoria en cada ingreso. Tener los dos métodos te permite entrar aunque no tengas el teléfono a mano.</p>
        {exito && <Alerta tipo="exito">{exito}</Alerta>}
        {error && !agregando && <Alerta tipo="error">{error}</Alerta>}

        {metodos && agregando === null && (
          <div className={s.formulario}>
            <p className={s.nota}>
              <Smartphone size={16} aria-hidden="true" /> <strong>App autenticadora:</strong> {metodos.app ? "activa" : "sin configurar"}
              {metodos.preferido === "app" && " · se pide por defecto"}
            </p>
            <p className={s.nota}>
              <Mail size={16} aria-hidden="true" /> <strong>Código por correo:</strong> {metodos.correo ? "activo" : "sin configurar"}
              {metodos.preferido === "correo" && " · se pide por defecto"}
            </p>
            <div className={s.acciones}>
              {!metodos.app && <Boton variante="secundario" onClick={() => void empezar("app")}>Agregar app autenticadora</Boton>}
              {!metodos.correo && <Boton variante="secundario" onClick={() => void empezar("correo")}>Agregar código por correo</Boton>}
              {metodos.app && metodos.correo && metodos.preferido !== "app" && (
                <Boton variante="secundario" onClick={() => void preferir("app")}>Pedir la app por defecto</Boton>
              )}
              {metodos.app && metodos.correo && metodos.preferido !== "correo" && (
                <Boton variante="secundario" onClick={() => void preferir("correo")}>Pedir el correo por defecto</Boton>
              )}
            </div>
          </div>
        )}

        {agregando && (
          <form className={s.formulario} onSubmit={activar} noValidate>
            {agregando === "app" ? (
              <>
                <p className={s.nota}>Abre tu app → agregar cuenta → escanear código QR, y escribe el código que muestra.</p>
                {qr && <div className={a.qr}><img src={qr} alt="Código QR para la app autenticadora" /></div>}
                {secreto && <div className={a.secreto}><span aria-label="Clave para ingresar manualmente">{secreto.match(/.{1,4}/g)?.join(" ")}</span></div>}
              </>
            ) : (
              aviso && <Alerta tipo="info">{aviso}</Alerta>
            )}
            {error && <Alerta tipo="error">{error}</Alerta>}
            <CampoCodigo etiqueta={agregando === "app" ? "Código de la app" : "Código del correo"} value={codigo} onChange={setCodigo} />
            <div className={s.acciones}>
              <Boton type="button" variante="fantasma" onClick={reiniciar}>Cancelar</Boton>
              {agregando === "correo" && (
                <Boton type="button" variante="fantasma" onClick={() => void empezar("correo")}>Enviar un código nuevo</Boton>
              )}
              <Boton type="submit" cargando={cargando} disabled={codigo.length !== 6 || (agregando === "app" && !secreto)}>Activar</Boton>
            </div>
          </form>
        )}
      </section>
      {metodos?.app && <CambiarAutenticador />}
    </>
  );
}
