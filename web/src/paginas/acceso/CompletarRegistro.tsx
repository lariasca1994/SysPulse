import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, Navigate, useLocation } from "react-router";
import { ExternalLink, RefreshCw, Send } from "lucide-react";
import { auth, type ResultadoRegistro } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";
import ui from "../../componentes/ui/ui.module.css";

type Estado = Partial<ResultadoRegistro> & { usuarioId?: string; desdeLogin?: boolean };

/** Paso 2 del registro: vincular Telegram con código (y confirmar el correo). */
export default function CompletarRegistro() {
  const estado = (useLocation().state as Estado | null) ?? {};
  const [enlace, setEnlace] = useState(estado.enlaceTelegram ?? "");
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [listo, setListo] = useState(false);
  const [cargando, setCargando] = useState(false);
  const pedido = useRef(false);

  // Si viene del login (registro anterior sin terminar), se genera un enlace nuevo.
  useEffect(() => {
    if (enlace || !estado.usuarioId || pedido.current) return;
    pedido.current = true;
    auth.nuevoEnlaceTelegram(estado.usuarioId).then((r) => setEnlace(r.enlaceTelegram)).catch(() => setError("No se pudo generar el enlace de Telegram."));
  }, [enlace, estado.usuarioId]);

  if (!estado.usuarioId) return <Navigate to="/registro" replace />;

  async function nuevoEnlace() {
    setError(null);
    try {
      setEnlace((await auth.nuevoEnlaceTelegram(estado.usuarioId!)).enlaceTelegram);
      setCodigo("");
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo generar el enlace.");
    }
  }

  async function confirmar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      await auth.confirmarTelegram(estado.usuarioId!, codigo);
      setListo(true);
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo confirmar el código.");
    } finally {
      setCargando(false);
    }
  }

  if (listo) {
    return (
      <LayoutAcceso paso="Cuenta verificada" titulo="¡Telegram vinculado!" subtitulo="Desde ahora recibirás por Telegram los avisos de tu nivel.">
        <div className={s.formulario}>
          <Alerta tipo="exito">Tu Telegram quedó verificado. El bot te envió un mensaje de confirmación.</Alerta>
          {estado.enlaceCorreoDesarrollo && <Alerta tipo="aviso">Recuerda confirmar también tu correo (enlace de abajo) antes de iniciar sesión.</Alerta>}
          <Link to="/login" state={{ aviso: "Cuenta lista. Inicia sesión para configurar tu segundo factor." }} className={`${ui.boton} ${ui.primario} ${ui.ancho}`}>
            Ir a iniciar sesión
          </Link>
        </div>
      </LayoutAcceso>
    );
  }

  return (
    <LayoutAcceso
      paso={estado.desdeLogin ? "Falta un paso" : "Paso 2 de 2"}
      titulo="Verifica tu Telegram"
      subtitulo={estado.telegramMostrado ? <>Confirmaremos que <strong>{estado.telegramMostrado}</strong> es tu cuenta.</> : "Confirmaremos que la cuenta de Telegram es tuya."}
    >
      <ol className={s.pasos}>
        <li><span>Abre <strong>@sysplusbot</strong> con el botón y pulsa <strong>Iniciar</strong>.</span></li>
        <li><span>Si registraste tu número, toca <strong>«Compartir mi número»</strong> en el chat.</span></li>
        <li><span>Escribe aquí el <strong>código de 6 dígitos</strong> que te envía el bot.</span></li>
      </ol>

      <form className={s.formulario} onSubmit={confirmar} noValidate>
        {error && <Alerta tipo="error">{error}</Alerta>}
        <a className={`${ui.boton} ${ui.secundario} ${ui.ancho}`} href={enlace || undefined} target="_blank" rel="noopener noreferrer" aria-disabled={!enlace}>
          <Send size={18} aria-hidden="true" /> Abrir @sysplusbot en Telegram <ExternalLink size={14} aria-hidden="true" />
        </a>
        <CampoCodigo etiqueta="Código de Telegram" value={codigo} onChange={setCodigo} ayuda="Vence a los 10 minutos." />
        <Boton type="submit" ancho cargando={cargando} disabled={codigo.length !== 6}>Verificar código</Boton>
        <Boton type="button" variante="fantasma" onClick={nuevoEnlace}>
          <RefreshCw size={16} aria-hidden="true" /> El enlace venció: generar uno nuevo
        </Boton>
      </form>

      {estado.enlaceCorreoDesarrollo ? (
        <div className={`${s.formulario} ${s.seccion}`}>
          <Alerta tipo="aviso">
            <strong>Modo desarrollo:</strong> el correo no se envía (para no consumir la cuota). Confírmalo aquí:{" "}
            <Link className={s.enlaceDev} to={new URL(estado.enlaceCorreoDesarrollo).pathname + new URL(estado.enlaceCorreoDesarrollo).search} target="_blank">
              confirmar correo
            </Link>
          </Alerta>
        </div>
      ) : (
        !estado.desdeLogin && (
          <div className={`${s.formulario} ${s.seccion}`}>
            <Alerta tipo="info">También te enviamos un correo para confirmar tu dirección (revisa spam).</Alerta>
          </div>
        )
      )}
    </LayoutAcceso>
  );
}
