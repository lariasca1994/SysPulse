import { useState, type FormEvent } from "react";
import QRCode from "qrcode";
import { Check, Copy, Download, Smartphone } from "lucide-react";
import { auth } from "../api/auth";
import { esFalla } from "../api/cliente";
import { useSesion } from "../sesionContexto";
import { Alerta, Boton } from "../componentes/ui/Basicos";
import { Campo, CampoCodigo, CampoPassword } from "../componentes/ui/Campos";
import a from "./acceso/acceso.module.css";
import s from "./Perfil.module.css";

type Paso = "inicio" | "identidad" | "escanear" | "codigos";

/**
 * Configurar la app autenticadora en otro teléfono. La app actual sigue funcionando hasta confirmar la nueva;
 * al confirmar se generan códigos de recuperación nuevos y se cierran las demás sesiones.
 */
export function CambiarAutenticador() {
  const { sesion, iniciar } = useSesion();
  const [paso, setPaso] = useState<Paso>("inicio");
  const [password, setPassword] = useState("");
  const [codigo, setCodigo] = useState("");
  const [usarRecuperacion, setUsarRecuperacion] = useState(false);
  const [secreto, setSecreto] = useState("");
  const [qr, setQr] = useState("");
  const [codigoNuevo, setCodigoNuevo] = useState("");
  const [codigos, setCodigos] = useState<string[]>([]);
  const [guardados, setGuardados] = useState(false);
  const [copiado, setCopiado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  function reiniciar() {
    setPaso("inicio");
    setPassword("");
    setCodigo("");
    setCodigoNuevo("");
    setSecreto("");
    setQr("");
    setError(null);
    setGuardados(false);
  }

  async function verificar(e: FormEvent) {
    e.preventDefault();
    if (!sesion) return;
    setError(null);
    setCargando(true);
    try {
      const r = await auth.cambiarMfaIniciar(sesion.token, password, codigo);
      setSecreto(r.secreto);
      setQr(await QRCode.toDataURL(r.uri, { margin: 1, width: 368, color: { dark: "#0B1F3A", light: "#FFFFFF" } }));
      setPaso("escanear");
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo verificar.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  async function confirmar(e: FormEvent) {
    e.preventDefault();
    if (!sesion) return;
    setError(null);
    setCargando(true);
    try {
      const r = await auth.cambiarMfaConfirmar(sesion.token, codigoNuevo);
      setCodigos(r.codigosRecuperacion);
      // Las demás sesiones quedaron cerradas; esta sigue con el token nuevo.
      iniciar(r.sesion);
      setPaso("codigos");
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo confirmar.");
      setCodigoNuevo("");
    } finally {
      setCargando(false);
    }
  }

  async function copiar(texto: string) {
    try {
      await navigator.clipboard.writeText(texto);
      setCopiado(true);
      window.setTimeout(() => setCopiado(false), 2000);
    } catch {
      setError("No se pudo copiar. Selecciona el texto y cópialo manualmente.");
    }
  }

  function descargar() {
    const contenido = `Códigos de recuperación de SysPulse\nCada código sirve una sola vez si pierdes acceso a tu app autenticadora.\n\n${codigos.join("\n")}\n`;
    const url = URL.createObjectURL(new Blob([contenido], { type: "text/plain;charset=utf-8" }));
    const enlace = Object.assign(document.createElement("a"), { href: url, download: "syspulse-codigos-recuperacion.txt" });
    enlace.click();
    URL.revokeObjectURL(url);
  }

  return (
    <section className={s.panel} aria-labelledby="mfa">
      <h2 id="mfa" className={s.tituloPanel}><Smartphone size={18} aria-hidden="true" /> App autenticadora</h2>

      {paso === "inicio" && (
        <>
          <p className={s.nota}>¿Cambiaste de teléfono? Configura la app en el nuevo. La actual sigue funcionando hasta que confirmes.</p>
          <div className={s.acciones}>
            <Boton variante="secundario" onClick={() => setPaso("identidad")}>Configurar en otro teléfono</Boton>
          </div>
        </>
      )}

      {paso === "identidad" && (
        <form className={s.formulario} onSubmit={verificar} noValidate>
          <p className={s.nota}>Paso 1 de 3 · Confirma que eres tú.</p>
          {error && <Alerta tipo="error">{error}</Alerta>}
          <CampoPassword etiqueta="Contraseña" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          {usarRecuperacion ? (
            <Campo etiqueta="Código de recuperación" value={codigo} onChange={(e) => setCodigo(e.target.value)} autoComplete="off"
              placeholder="XXXX-XXXX" ayuda="Uno de los códigos que guardaste. Úsalo si ya no tienes el teléfono anterior." />
          ) : (
            <CampoCodigo etiqueta="Código de tu app actual" value={codigo} onChange={setCodigo} />
          )}
          <button type="button" className={a.alternarCodigo} onClick={() => { setUsarRecuperacion(!usarRecuperacion); setCodigo(""); }}>
            {usarRecuperacion ? "Usar el código de la app actual" : "Ya no tengo el teléfono anterior: usar un código de recuperación"}
          </button>
          <div className={s.acciones}>
            <Boton type="button" variante="fantasma" onClick={reiniciar}>Cancelar</Boton>
            <Boton type="submit" cargando={cargando} disabled={!password || codigo.trim().length < 6}>Continuar</Boton>
          </div>
        </form>
      )}

      {paso === "escanear" && (
        <form className={s.formulario} onSubmit={confirmar} noValidate>
          <p className={s.nota}>Paso 2 de 3 · En el teléfono nuevo, abre tu app → agregar cuenta → escanear código QR.</p>
          {qr && <div className={a.qr}><img src={qr} alt="Código QR para la app autenticadora nueva" /></div>}
          {secreto && (
            <div className={a.secreto}>
              <span aria-label="Clave para ingresar manualmente">{secreto.match(/.{1,4}/g)?.join(" ")}</span>
              <Boton type="button" variante="fantasma" onClick={() => void copiar(secreto)} aria-label="Copiar clave">
                {copiado ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
              </Boton>
            </div>
          )}
          {error && <Alerta tipo="error">{error}</Alerta>}
          <CampoCodigo etiqueta="Código de la app nueva" value={codigoNuevo} onChange={setCodigoNuevo} />
          <p className={s.nota}>Tienes 10 minutos. Hasta confirmar, tu app anterior sigue siendo la válida.</p>
          <div className={s.acciones}>
            <Boton type="button" variante="fantasma" onClick={reiniciar}>Cancelar</Boton>
            <Boton type="submit" cargando={cargando} disabled={codigoNuevo.length !== 6}>Confirmar app nueva</Boton>
          </div>
        </form>
      )}

      {paso === "codigos" && (
        <div className={s.formulario}>
          <Alerta tipo="exito">App nueva activada. La anterior ya no sirve y se cerraron tus demás sesiones.</Alerta>
          <p className={s.nota}>Paso 3 de 3 · Guarda tus códigos de recuperación nuevos (los anteriores dejaron de servir). Solo se muestran ahora.</p>
          <ul className={a.codigos} aria-label="Códigos de recuperación">
            {codigos.map((c) => <li key={c}>{c}</li>)}
          </ul>
          <div className={a.acciones}>
            <Boton type="button" variante="secundario" onClick={() => void copiar(codigos.join("\n"))}>
              {copiado ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />} {copiado ? "Copiados" : "Copiar"}
            </Boton>
            <Boton type="button" variante="secundario" onClick={descargar}>
              <Download size={16} aria-hidden="true" /> Descargar .txt
            </Boton>
          </div>
          <label className={a.check}>
            <input type="checkbox" checked={guardados} onChange={(e) => setGuardados(e.target.checked)} />
            Guardé mis códigos en un lugar seguro.
          </label>
          <div className={s.acciones}>
            <Boton disabled={!guardados} onClick={reiniciar}>Listo</Boton>
          </div>
        </div>
      )}
    </section>
  );
}
