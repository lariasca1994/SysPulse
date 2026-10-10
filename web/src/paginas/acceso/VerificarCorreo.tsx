import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { Alerta } from "../../componentes/ui/Basicos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";
import ui from "../../componentes/ui/ui.module.css";

export default function VerificarCorreo() {
  const [params] = useSearchParams();
  const token = params.get("token");
  const [estado, setEstado] = useState<"verificando" | "ok" | "error">(token ? "verificando" : "error");
  const [mensaje, setMensaje] = useState(token ? "" : "El enlace está incompleto.");
  const hecho = useRef(false);

  useEffect(() => {
    if (!token || hecho.current) return;
    hecho.current = true;
    auth
      .verificarCorreo(token)
      .then(() => setEstado("ok"))
      .catch((e) => {
        setEstado("error");
        setMensaje(esFalla(e) ? e.detalle.mensaje : "No se pudo verificar el correo.");
      });
  }, [token]);

  return (
    <LayoutAcceso paso="Correo" titulo={estado === "ok" ? "Correo confirmado" : estado === "error" ? "No pudimos confirmar el correo" : "Confirmando tu correo…"}>
      <div className={s.formulario}>
        {estado === "verificando" && <Alerta tipo="info">Un momento…</Alerta>}
        {estado === "ok" && <Alerta tipo="exito">Tu correo quedó confirmado. Si ya verificaste tu Telegram, puedes iniciar sesión.</Alerta>}
        {estado === "error" && <Alerta tipo="error">{mensaje} Si ya lo confirmaste antes, simplemente inicia sesión.</Alerta>}
        {estado !== "verificando" && (
          <Link to="/login" className={`${ui.boton} ${ui.primario} ${ui.ancho}`}>Ir a iniciar sesión</Link>
        )}
      </div>
    </LayoutAcceso>
  );
}
