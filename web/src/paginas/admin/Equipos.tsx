import { useState } from "react";
import { Laptop, Monitor, Pencil, Smartphone, Archive, ArchiveRestore } from "lucide-react";
import { equipos, type Equipo } from "../../api/admin";
import { esFalla } from "../../api/cliente";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Campo } from "../../componentes/ui/Campos";
import { Dialogo } from "../../componentes/ui/Dialogo";
import { AltaEquipo } from "./AltaEquipo";
import s from "./admin.module.css";

const fecha = (iso: string | null) => (iso ? new Date(iso).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" }) : "—");
const CONEXION: Record<string, string> = { "en-linea": "En línea", retrasado: "Retrasado", "sin-reporte": "Sin reporte" };

function Icono({ e }: { e: Equipo }) {
  const I = e.tipo === "android" ? Smartphone : e.clase.includes("portátil") ? Laptop : Monitor;
  return <I size={16} aria-hidden="true" />;
}

/**
 * Equipos (gestión de activos ITIL): alta con código de un solo uso (también la hacen N2 y N3 desde Salud de hosts),
 * nombres genéricos y retiro sin borrar historial.
 * Los instaladores públicos no contienen secretos: los datos de conexión llegan al canjear el código.
 */
export function Equipos() {
  const { sesion } = useSesion();
  const lista = useConsulta((t) => equipos.listar(t), [], 60_000);
  const codigos = useConsulta((t) => equipos.codigos(t), []);
  const [renombrar, setRenombrar] = useState<Equipo | null>(null);
  const [nombre, setNombre] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function accion(fn: () => Promise<unknown>) {
    setError(null);
    try {
      await fn();
      setRenombrar(null);
      await lista.recargar();
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo completar la acción.");
    }
  }

  return (
    <section className={s.panel}>
      <div className={s.cabeceraEquipos}>
        <p className={s.nota}>Agrega computadores, portátiles, tablets y celulares sin copiar archivos ni contraseñas: genera un código, úsalo en el equipo y listo. Cada código sirve una vez y vence en 24 horas.</p>
        <AltaEquipo alGenerar={() => void codigos.recargar()} />
      </div>
      {(error || lista.error) && <div className={s.bloque}><Alerta tipo="error">{error ?? lista.error}</Alerta></div>}

      <div className={s.desplazable}>
        <table className={s.tabla}>
          <thead><tr><th>Equipo</th><th>Agente</th><th>Último reporte</th><th>Estado</th><th /></tr></thead>
          <tbody>
            {(lista.datos ?? []).map((e) => (
              <tr key={e.id} data-inactivo={e.retirado || undefined}>
                <td><span className={s.equipo}><Icono e={e} /> <strong>{e.nombre}</strong></span><br /><span className={s.tenue}>{e.clase}</span></td>
                <td className="num">{e.versionAgente ? `v${e.versionAgente}` : "—"}</td>
                <td className={s.tenue}>{fecha(e.ultimoReporte)}</td>
                <td>
                  {e.retirado
                    ? <span className={s.estado} data-activo="false">Retirado</span>
                    : <span className={s.conexion} data-conexion={e.conexion}>{CONEXION[e.conexion] ?? e.conexion}</span>}
                </td>
                <td className={s.acciones}>
                  <Boton variante="fantasma" onClick={() => { setRenombrar(e); setNombre(e.nombre); }}><Pencil size={14} aria-hidden="true" /> Renombrar</Boton>
                  {e.retirado
                    ? <Boton variante="fantasma" onClick={() => void accion(() => equipos.retirar(sesion!.token, e.id, false))}><ArchiveRestore size={14} aria-hidden="true" /> Restaurar</Boton>
                    : <Boton variante="fantasma" onClick={() => void accion(() => equipos.retirar(sesion!.token, e.id, true))}><Archive size={14} aria-hidden="true" /> Retirar</Boton>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className={s.nota}>Retirar oculta el equipo de la operación (salud, analítica) sin borrar su historial; desinstala antes el agente o volverá a reportar.</p>

      {(codigos.datos ?? []).length > 0 && (
        <>
          <h3 className={s.tituloGrupo}>Códigos recientes</h3>
          <ul className={s.codigosLista}>
            {codigos.datos!.slice(0, 6).map((c) => (
              <li key={`${c.pista}-${c.creado}`}>
                <span className="num">{c.pista}</span>
                <span className={s.estadoSolicitud} data-estado={c.estado === "usado" ? "aprobada" : c.estado === "vencido" ? "rechazada" : "pendiente"}>{c.estado}</span>
                <span className={s.tenue}>{c.estado === "usado" ? `${c.tipoUsado} · ${fecha(c.usadoEn)}` : `vence ${fecha(c.expira)}`}</span>
              </li>
            ))}
          </ul>
        </>
      )}

      {renombrar && (
        <Dialogo abierto titulo="Renombrar equipo" descripcion="Usa un nombre genérico (ej. «Tablet de soporte»): sin marcas, modelos ni nombres de personas." alCerrar={() => setRenombrar(null)}>
          <form className={s.formulario} onSubmit={(ev) => { ev.preventDefault(); void accion(() => equipos.renombrar(sesion!.token, renombrar.id, nombre)); }}>
            {error && <Alerta tipo="error">{error}</Alerta>}
            <Campo etiqueta="Nombre" value={nombre} onChange={(ev) => setNombre(ev.target.value)} maxLength={40} autoFocus />
            <div className={s.pieDialogo}>
              <Boton type="button" variante="secundario" onClick={() => setRenombrar(null)}>Cancelar</Boton>
              <Boton type="submit" disabled={nombre.trim().length < 3}>Guardar</Boton>
            </div>
          </form>
        </Dialogo>
      )}
    </section>
  );
}
