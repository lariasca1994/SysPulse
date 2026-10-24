import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router";
import QRCode from "qrcode";
import { Check, Copy, Download, Mail, Smartphone } from "lucide-react";
import { auth, type Sesion } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

/**
 * Configurar el segundo factor (obligatorio): se elige app autenticadora o código por correo, se confirma con el
 * primer código y se muestran los códigos de recuperación (una sola vez). El otro método se agrega desde Mi cuenta.
 */
export default function ConfigurarMfa() {
  const navegar = useNavigate();
  const { iniciar } = useSesion();
  const token = (useLocation().state as { token?: string } | null)?.token;
  const [metodo, setMetodo] = useState<"app" | "correo" | null>(null);
  const [secreto, setSecreto] = useState("");
  const [qr, setQr] = useState("");
  const [aviso, setAviso] = useState<string | null>(null);
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const [resultado, setResultado] = useState<{ codigos: string[]; sesion: Sesion } | null>(null);
  const [guardados, setGuardados] = useState(false);
  const [copiado, setCopiado] = useState<"secreto" | "codigos" | null>(null);

  if (!token) return <Navigate to="/login" replace />;

  async function elegirApp() {
    setMetodo("app");
    setError(null);
    try {
      const r = await auth.iniciarMfa(token!);
      setSecreto(r.secreto);
      setQr(await QRCode.toDataURL(r.uri, { margin: 1, width: 368, color: { dark: "#0B1F3A", light: "#FFFFFF" } }));
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo iniciar la configuración.");
    }
  }

  async function enviarCorreo() {
    setMetodo("correo");
    setError(null);
    setAviso(null);
    try {
      setAviso((await auth.enviarCodigoCorreo(token!)).mensaje);
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo enviar el código.");
    }
  }

  async function copiar(texto: string, que: "secreto" | "codigos") {
    try {
      await navigator.clipboard.writeText(texto);
      setCopiado(que);
      window.setTimeout(() => setCopiado(null), 2000);
    } catch {
      setError("No se pudo copiar. Selecciona el texto y cópialo manualmente.");
    }
  }

  function descargar(codigos: string[]) {
    const contenido = `Códigos de recuperación de SysPulse\nCada código sirve una sola vez si pierdes acceso a tu segundo factor.\n\n${codigos.join("\n")}\n`;
    const url = URL.createObjectURL(new Blob([contenido], { type: "text/plain;charset=utf-8" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: "syspulse-codigos-recuperacion.txt" });
    a.click();
    URL.revokeObjectURL(url);
  }

  async function activar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = metodo === "correo" ? await auth.activarCorreo(token!, codigo) : await auth.activarMfa(token!, codigo);
      setResultado({ codigos: r.codigosRecuperacion, sesion: r.sesion });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo activar.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  if (resultado) {
    return (
      <LayoutAcceso paso="Último paso" titulo="Guarda tus códigos de recuperación" subtitulo="Si pierdes el acceso a tu segundo factor, cada código te permite entrar una vez. Solo se muestran ahora.">
        <ul className={s.codigos} aria-label="Códigos de recuperación">
          {resultado.codigos.map((c) => <li key={c}>{c}</li>)}
        </ul>
        <div className={s.formulario}>
          <div className={s.acciones}>
            <Boton type="button" variante="secundario" onClick={() => copiar(resultado.codigos.join("\n"), "codigos")}>
              {copiado === "codigos" ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />} {copiado === "codigos" ? "Copiados" : "Copiar"}
            </Boton>
            <Boton type="button" variante="secundario" onClick={() => descargar(resultado.codigos)}>
              <Download size={16} aria-hidden="true" /> Descargar .txt
            </Boton>
          </div>
          <label className={s.check}>
            <input type="checkbox" checked={guardados} onChange={(e) => setGuardados(e.target.checked)} />
            Guardé mis códigos en un lugar seguro (gestor de contraseñas o papel).
          </label>
          <Boton
            type="button"
            ancho
            disabled={!guardados}
            onClick={() => {
              iniciar(resultado.sesion);
              navegar("/", { replace: true });
            }}
          >
            Entrar a SysPulse
          </Boton>
        </div>
      </LayoutAcceso>
    );
  }

  if (metodo === null) {
    return (
      <LayoutAcceso paso="Segundo factor" titulo="Elige tu verificación en dos pasos" subtitulo="Cada ingreso pedirá, además de la contraseña, un código. Después podrás agregar el otro método desde Mi cuenta.">
        <div className={s.formulario}>
          <Boton type="button" ancho onClick={() => void elegirApp()}>
            <Smartphone size={18} aria-hidden="true" /> App autenticadora (recomendado)
          </Boton>
          <Boton type="button" ancho variante="secundario" onClick={() => void enviarCorreo()}>
            <Mail size={18} aria-hidden="true" /> Código por correo
          </Boton>
        </div>
      </LayoutAcceso>
    );
  }

  const volver = (
    <Boton type="button" variante="fantasma" onClick={() => { setMetodo(null); setCodigo(""); setError(null); setAviso(null); }}>
      Elegir otro método
    </Boton>
  );

  if (metodo === "correo") {
    return (
      <LayoutAcceso paso="Segundo factor" titulo="Activa el código por correo" subtitulo="Escribe el código de 6 dígitos que te enviamos. Vence en 10 minutos y sirve una sola vez.">
        <form className={s.formulario} onSubmit={activar} noValidate>
          {aviso && <Alerta tipo="info">{aviso}</Alerta>}
          {error && <Alerta tipo="error">{error}</Alerta>}
          <CampoCodigo etiqueta="Código del correo" value={codigo} onChange={setCodigo} autoFocus />
          <Boton type="submit" ancho cargando={cargando} disabled={codigo.length !== 6}>Activar</Boton>
          <Boton type="button" variante="fantasma" onClick={() => void enviarCorreo()}>Enviar un código nuevo</Boton>
          {volver}
        </form>
      </LayoutAcceso>
    );
  }

  return (
    <LayoutAcceso paso="Segundo factor" titulo="Activa tu app autenticadora" subtitulo="Usa Google Authenticator, Microsoft Authenticator, Authy u otra app compatible.">
      <ol className={s.pasos}>
        <li><span>Abre tu app y elige <strong>agregar cuenta → escanear código QR</strong>.</span></li>
        <li><span>Escribe el <strong>código de 6 dígitos</strong> que muestra la app.</span></li>
      </ol>
      {qr ? (
        <div className={s.qr}>
          <img src={qr} alt="Código QR para configurar la app autenticadora" />
        </div>
      ) : (
        !error && <Alerta tipo="info">Generando el código QR…</Alerta>
      )}
      {secreto && (
        <div className={s.secreto}>
          <span aria-label="Clave para ingresar manualmente">{secreto.match(/.{1,4}/g)?.join(" ")}</span>
          <Boton type="button" variante="fantasma" onClick={() => copiar(secreto, "secreto")} aria-label="Copiar clave">
            {copiado === "secreto" ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
          </Boton>
        </div>
      )}
      <form className={s.formulario} onSubmit={activar} noValidate>
        {error && <Alerta tipo="error">{error}</Alerta>}
        <CampoCodigo etiqueta="Código de la app" value={codigo} onChange={setCodigo} />
        <Boton type="submit" ancho cargando={cargando} disabled={codigo.length !== 6 || !secreto}>Activar</Boton>
        {volver}
      </form>
    </LayoutAcceso>
  );
}
