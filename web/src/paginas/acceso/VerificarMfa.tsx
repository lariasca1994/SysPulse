import { useState, type FormEvent } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router";
import { ShieldCheck } from "lucide-react";
import { auth } from "../../api/auth";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo, CampoCodigo } from "../../componentes/ui/Campos";
import { LayoutAcceso } from "./LayoutAcceso";
import s from "./acceso.module.css";

export default function VerificarMfa() {
  const navegar = useNavigate();
  const { iniciar } = useSesion();
  const token = (useLocation().state as { token?: string } | null)?.token;
  const [modo, setModo] = useState<"app" | "recuperacion">("app");
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  if (!token) return <Navigate to="/login" replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setCargando(true);
    try {
      const r = await auth.verificarMfa(token!, codigo);
      iniciar(r.sesion);
      navegar("/", { replace: true });
    } catch (err) {
      setError(esFalla(err) ? err.detalle.mensaje : "No se pudo verificar.");
      setCodigo("");
    } finally {
      setCargando(false);
    }
  }

  const listo = modo === "app" ? codigo.length === 6 : codigo.trim().length >= 9;

  return (
    <LayoutAcceso paso="Segundo factor" titulo="Ingresa tu código" subtitulo={modo === "app" ? "Abre tu app autenticadora y escribe el código de SysPulse." : "Escribe uno de tus códigos de recuperación. Cada uno sirve una sola vez."}>
      <form className={s.formulario} onSubmit={enviar} noValidate>
        {error && <Alerta tipo="error">{error}</Alerta>}
        {modo === "app" ? (
          <CampoCodigo etiqueta="Código de 6 dígitos" value={codigo} onChange={setCodigo} autoFocus />
        ) : (
          <Campo etiqueta="Código de recuperación" value={codigo} onChange={(e) => setCodigo(e.target.value.toUpperCase())} placeholder="ABCD-EFGH" autoComplete="off" autoFocus />
        )}
        <Boton type="submit" ancho cargando={cargando} disabled={!listo}>
          {!cargando && <ShieldCheck size={18} aria-hidden="true" />} Verificar
        </Boton>
        <Boton
          type="button"
          variante="fantasma"
          onClick={() => {
            setModo(modo === "app" ? "recuperacion" : "app");
            setCodigo("");
            setError(null);
          }}
        >
          {modo === "app" ? "No tengo mi teléfono: usar un código de recuperación" : "Usar el código de la app"}
        </Boton>
      </form>
      <p className={s.alterno}>
        <Link to="/login">Volver al inicio de sesión</Link>
      </p>
    </LayoutAcceso>
  );
}
