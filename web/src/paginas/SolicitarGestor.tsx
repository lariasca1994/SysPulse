import { useState } from "react";
import { ShieldCheck } from "lucide-react";
import { cuenta } from "../api/admin";
import { esFalla } from "../api/cliente";
import { useConsulta } from "../api/useConsulta";
import { useSesion } from "../sesionContexto";
import { Alerta, Boton } from "../componentes/ui/Basicos";
import { AreaTexto } from "../componentes/ui/Campos";
import s from "./Perfil.module.css";

/** Solicitar el rol gestor (sección 12): solo técnicos N1/N2/N3; lo aprueba el admin. */
export function SolicitarGestor() {
  const { sesion } = useSesion();
  const { datos, recargar } = useConsulta((t) => cuenta.miSolicitud(t), []);
  const [abierto, setAbierto] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar() {
    if (!sesion) return;
    setError(null);
    setEnviando(true);
    try {
      await cuenta.solicitarGestor(sesion.token, motivo);
      setAbierto(false);
      setMotivo("");
      await recargar();
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo enviar la solicitud.");
    } finally {
      setEnviando(false);
    }
  }

  const pendiente = datos?.estado === "pendiente";

  return (
    <section className={s.panel} aria-labelledby="gestor">
      <h2 id="gestor" className={s.tituloPanel}><ShieldCheck size={18} aria-hidden="true" /> Rol de gestor</h2>
      <p className={s.nota}>El gestor decide sobre riesgo, prioridad y aprobaciones, y recibe los escalamientos por SLA. Lo aprueba el administrador.</p>

      {pendiente && <Alerta tipo="info">Tu solicitud está pendiente de revisión desde el {new Date(datos.creado).toLocaleDateString("es-CO")}.</Alerta>}
      {datos?.estado === "rechazada" && !abierto && (
        <Alerta tipo="aviso">Tu última solicitud fue rechazada{datos.comentario ? `: ${datos.comentario}` : "."} Puedes enviar una nueva.</Alerta>
      )}

      {abierto ? (
        <div className={s.formulario}>
          {error && <Alerta tipo="error">{error}</Alerta>}
          <AreaTexto etiqueta="¿Por qué necesitas el rol gestor?" value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={1000}
            ayuda="Entre 20 y 1000 caracteres. El administrador lo verá al decidir." autoFocus />
          <div className={s.acciones}>
            <Boton variante="fantasma" onClick={() => setAbierto(false)}>Cancelar</Boton>
            <Boton cargando={enviando} disabled={motivo.trim().length < 20} onClick={() => void enviar()}>Enviar solicitud</Boton>
          </div>
        </div>
      ) : (
        !pendiente && (
          <div className={s.acciones}>
            <Boton variante="secundario" onClick={() => setAbierto(true)}>Solicitar rol gestor</Boton>
          </div>
        )
      )}
    </section>
  );
}
