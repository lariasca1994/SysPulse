import { useEffect, useRef, useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router";
import QRCode from "qrcode";
import { Check, Copy, Download } from "lucide-react";
import { auth, type Sesion } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

/** Configurar MFA: QR → primer código → códigos de recuperación (se muestran una sola vez). */
export default function ConfigurarMfa() {
  const navegar = useNavigate();
  const { iniciar } = useSesion();
  const token = (useLocation().state as { token?: string } | null)?.token;
  const [secreto, setSecreto] = useState("");
  const [qr, setQr] = useState("");
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const [resultado, setResultado] = useState<{ codigos: string[]; sesion: Sesion } | null>(null);
  const [guardados, setGuardados] = useState(false);
  const [copiado, setCopiado] = useState<"secreto" | "codigos" | null>(null);
  const pedido = useRef(false);

  useEffect(() => {
    if (!token || pedido.current) return;
    pedido.current = true;
    auth
      .iniciarMfa(token)
      .then(async (r) => {
        setSecreto(r.secreto);
        setQr(await QRCode.toDataURL(r.uri, { margin: 1, width: 368, color: { dark: "#0B1F3A", light: "#FFFFFF" } }));
      })
      .catch((e) => setError(esFalla(e) ? e.detalle.mensaje : "No se pudo iniciar la configuración."));
  }, [token]);

  if (!token) return <Navigate to="/login" replace />;

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
    const contenido = `Códigos de recuperación de SysPulse\nCada código sirve una sola vez si pierdes acceso a tu app autenticadora.\n\n${codigos.join("\n")}\n`;
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
      const r = await auth.activarMfa(token!, codigo);
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
      <LayoutAcceso paso="Último paso" titulo="Guarda tus códigos de recuperación" subtitulo="Si pierdes el teléfono, cada código te permite entrar una vez. Solo se muestran ahora.">
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
      </form>
    </LayoutAcceso>
  );
}
