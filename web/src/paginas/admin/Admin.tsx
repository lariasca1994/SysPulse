import { useState } from "react";
import { Navigate } from "react-router";
import { BellRing, Check, MonitorSmartphone, RotateCcw, ShieldCheck, UserCheck, UserX, Users, X } from "lucide-react";
import { Equipos } from "./Equipos";
import { admin, type ConfiguracionAlertas, type Solicitud, type UmbralAdmin, type UsuarioAdmin } from "../../api/admin";
import { esFalla } from "../../api/cliente";
import { useConsulta } from "../../api/useConsulta";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { AreaTexto } from "../../componentes/ui/Campos";
import { Dialogo } from "../../componentes/ui/Dialogo";
import h from "../hosts/hosts.module.css";
import s from "./admin.module.css";

type Pestana = "equipos" | "usuarios" | "solicitudes" | "alertas";

const ROL: Record<string, string> = { N1: "N1", N2: "N2", N3: "N3", gestor: "Gestor", admin: "Admin" };
const fecha = (iso: string | null) => (iso ? new Date(iso).toLocaleString("es-CO", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }) : "—");

/** Funciones exclusivas del admin (sección 12): cuentas, rol gestor y alertas. Nada se borra: todo queda en la auditoría. */
export default function Admin() {
  const { usuario } = useSesion();
  const [pestana, setPestana] = useState<Pestana>("equipos");
  const solicitudes = useConsulta((t) => admin.solicitudes(t), [], 60_000);
  const pendientes = (solicitudes.datos ?? []).filter((x) => x.estado === "pendiente").length;

  if (usuario?.rol !== "admin") return <Navigate to="/" replace />;

  return (
    <>
      <div className={h.encabezado}>
        <div>
          <h1 className={h.titulo}>Administración</h1>
          <p className={h.subtitulo}>Equipos, cuentas, rol gestor y alertas de todos los agentes. Nada se borra: cada cambio queda en la auditoría.</p>
        </div>
      </div>

      <div className={s.pestanas} role="tablist" aria-label="Secciones">
        <button type="button" role="tab" aria-selected={pestana === "equipos"} onClick={() => setPestana("equipos")}><MonitorSmartphone size={16} aria-hidden="true" /> Equipos</button>
        <button type="button" role="tab" aria-selected={pestana === "usuarios"} onClick={() => setPestana("usuarios")}><Users size={16} aria-hidden="true" /> Usuarios</button>
        <button type="button" role="tab" aria-selected={pestana === "solicitudes"} onClick={() => setPestana("solicitudes")}>
          <ShieldCheck size={16} aria-hidden="true" /> Solicitudes de gestor {pendientes > 0 && <span className={s.contador}>{pendientes}</span>}
        </button>
        <button type="button" role="tab" aria-selected={pestana === "alertas"} onClick={() => setPestana("alertas")}><BellRing size={16} aria-hidden="true" /> Alertas</button>
      </div>

      {pestana === "equipos" && <Equipos />}
      {pestana === "usuarios" && <Usuarios miId={usuario.id} />}
      {pestana === "solicitudes" && <Solicitudes datos={solicitudes.datos} error={solicitudes.error} recargar={solicitudes.recargar} />}
      {pestana === "alertas" && <Alertas />}
    </>
  );
}

// ───────────── Usuarios ─────────────

function Usuarios({ miId }: { miId: string }) {
  const { sesion } = useSesion();
  const { datos, error, recargar } = useConsulta((t) => admin.usuarios(t), []);
  const [confirmar, setConfirmar] = useState<{ u: UsuarioAdmin; accion: "desactivar" | "reactivar" | "revocar" } | null>(null);
  const [fallo, setFallo] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function ejecutar() {
    if (!confirmar || !sesion) return;
    setOcupado(true);
    setFallo(null);
    try {
      if (confirmar.accion === "revocar") await admin.revocarGestor(sesion.token, confirmar.u.id);
      else await admin.cambiarActivo(sesion.token, confirmar.u.id, confirmar.accion === "reactivar");
      setConfirmar(null);
      await recargar();
    } catch (e) {
      setFallo(esFalla(e) ? e.detalle.mensaje : "No se pudo completar la acción.");
    } finally {
      setOcupado(false);
    }
  }

  return (
    <section className={s.panel}>
      {(error || fallo) && <div className={s.bloque}><Alerta tipo="error">{fallo ?? error}</Alerta></div>}
      <div className={s.desplazable}>
        <table className={s.tabla}>
          <thead>
            <tr><th>Persona</th><th>Rol</th><th>Telegram</th><th>MFA</th><th>Última conexión</th><th>Estado</th><th /></tr>
          </thead>
          <tbody>
            {(datos ?? []).map((u) => (
              <tr key={u.id} data-inactivo={!u.activo || undefined}>
                <td><strong>{u.nombre}</strong>{u.id === miId && <span className={s.tenue}> (tú)</span>}<br /><span className={s.tenue}>{u.email}</span></td>
                <td><span className={s.rol} data-rol={u.rol}>{ROL[u.rol] ?? u.rol}</span>{u.rol === "gestor" && <span className={s.tenue}> · era {u.nivelTecnico}</span>}</td>
                <td>{u.telegramVerificado ? <Check size={16} className={s.si} aria-label="Verificado" /> : <X size={16} className={s.no} aria-label="Sin verificar" />}</td>
                <td>{u.mfaActivo ? <Check size={16} className={s.si} aria-label="Activo" /> : <X size={16} className={s.no} aria-label="Pendiente" />}</td>
                <td className={s.tenue}>{fecha(u.ultimaConexion)}</td>
                <td><span className={s.estado} data-activo={u.activo}>{u.activo ? "Activa" : "Desactivada"}</span></td>
                <td className={s.acciones}>
                  {u.rol === "gestor" && <Boton variante="fantasma" onClick={() => setConfirmar({ u, accion: "revocar" })}>Revocar gestor</Boton>}
                  {u.id !== miId && (u.activo
                    ? <Boton variante="fantasma" onClick={() => setConfirmar({ u, accion: "desactivar" })}><UserX size={16} aria-hidden="true" /> Desactivar</Boton>
                    : <Boton variante="fantasma" onClick={() => setConfirmar({ u, accion: "reactivar" })}><UserCheck size={16} aria-hidden="true" /> Reactivar</Boton>)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {confirmar && (
        <Dialogo abierto titulo={confirmar.accion === "desactivar" ? `Desactivar a ${confirmar.u.nombre}` : confirmar.accion === "reactivar" ? `Reactivar a ${confirmar.u.nombre}` : `Revocar el rol gestor a ${confirmar.u.nombre}`}
          descripcion={confirmar.accion === "desactivar" ? "Su sesión se cierra de inmediato y no podrá ingresar. Sus registros se conservan; puedes reactivarla cuando quieras."
            : confirmar.accion === "reactivar" ? "Podrá volver a ingresar con su contraseña y su app autenticadora."
            : `Vuelve a su nivel técnico (${confirmar.u.nivelTecnico}) y se le avisa por Telegram y correo.`}
          alCerrar={() => setConfirmar(null)}>
          {fallo && <Alerta tipo="error">{fallo}</Alerta>}
          <div className={s.pieDialogo}>
            <Boton variante="secundario" onClick={() => setConfirmar(null)}>Cancelar</Boton>
            <Boton cargando={ocupado} onClick={() => void ejecutar()}>Confirmar</Boton>
          </div>
        </Dialogo>
      )}
    </section>
  );
}

// ───────────── Solicitudes de gestor ─────────────

function Solicitudes({ datos, error, recargar }: { datos: Solicitud[] | null; error: string | null; recargar: () => Promise<void> }) {
  const { sesion } = useSesion();
  const [rechazar, setRechazar] = useState<Solicitud | null>(null);
  const [comentario, setComentario] = useState("");
  const [fallo, setFallo] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState<string | null>(null);

  async function resolver(sol: Solicitud, aprobar: boolean) {
    if (!sesion) return;
    setOcupado(sol.id);
    setFallo(null);
    try {
      await admin.resolver(sesion.token, sol.id, aprobar, aprobar ? undefined : comentario);
      setRechazar(null);
      setComentario("");
      await recargar();
    } catch (e) {
      setFallo(esFalla(e) ? e.detalle.mensaje : "No se pudo resolver la solicitud.");
    } finally {
      setOcupado(null);
    }
  }

  const lista = datos ?? [];
  return (
    <section className={s.panel}>
      <p className={s.nota}>El rol gestor decide sobre riesgo, prioridad y aprobaciones (no resuelve técnicamente). Al aprobar, la persona pasa a gestor y se le avisa; puedes revocarlo desde Usuarios.</p>
      {(error || fallo) && <div className={s.bloque}><Alerta tipo="error">{fallo ?? error}</Alerta></div>}
      {lista.length === 0 ? <p className={s.vacio}>No hay solicitudes.</p> : (
        <ul className={s.solicitudes}>
          {lista.map((x) => (
            <li key={x.id} data-estado={x.estado}>
              <div className={s.solicitudCabeza}>
                <strong>{x.usuarioNombre}</strong>
                <span className={s.rol} data-rol={x.nivelActual}>{x.nivelActual}</span>
                <span className={s.estadoSolicitud} data-estado={x.estado}>{x.estado}</span>
                <span className={s.tenue}>{fecha(x.creado)}</span>
              </div>
              <p className={s.motivo}>{x.motivo}</p>
              {x.estado === "pendiente" ? (
                <div className={s.acciones}>
                  <Boton variante="secundario" onClick={() => { setRechazar(x); setComentario(""); }}>Rechazar</Boton>
                  <Boton cargando={ocupado === x.id} onClick={() => void resolver(x, true)}>Aprobar</Boton>
                </div>
              ) : (
                <p className={s.tenue}>Resuelta por {x.resueltoPorNombre ?? "—"} el {fecha(x.resuelto)}{x.comentario ? ` · ${x.comentario}` : ""}</p>
              )}
            </li>
          ))}
        </ul>
      )}
      {rechazar && (
        <Dialogo abierto titulo={`Rechazar la solicitud de ${rechazar.usuarioNombre}`} descripcion="La persona recibirá el motivo por Telegram y correo." alCerrar={() => setRechazar(null)}>
          <div className={s.formulario}>
            {fallo && <Alerta tipo="error">{fallo}</Alerta>}
            <AreaTexto etiqueta="Motivo del rechazo" value={comentario} onChange={(e) => setComentario(e.target.value)} maxLength={500} autoFocus />
            <div className={s.pieDialogo}>
              <Boton variante="secundario" onClick={() => setRechazar(null)}>Cancelar</Boton>
              <Boton cargando={ocupado === rechazar.id} disabled={comentario.trim().length < 5} onClick={() => void resolver(rechazar, false)}>Rechazar solicitud</Boton>
            </div>
          </div>
        </Dialogo>
      )}
    </section>
  );
}

// ───────────── Alertas ─────────────

function Alertas() {
  const { datos, error } = useConsulta((t) => admin.alertas(t), []);
  if (error) return <section className={s.panel}><Alerta tipo="error">{error}</Alerta></section>;
  if (!datos) return <section className={`${s.panel} ${s.cargando} ${h.esqueleto}`} aria-busy="true" />;
  return <EditorAlertas inicial={datos} />;
}

/** Edita una copia local de la configuración; se guarda en la configuración central al pulsar Guardar. */
function EditorAlertas({ inicial }: { inicial: ConfiguracionAlertas }) {
  const { sesion } = useSesion();
  const [config, setConfig] = useState<ConfiguracionAlertas>(() => structuredClone(inicial));
  const [mensaje, setMensaje] = useState<{ tipo: "exito" | "error"; texto: string } | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cambiar = (i: number, campo: "advertencia" | "excepcion" | "recuperacion", valor: number) =>
    setConfig({ ...config, umbrales: config.umbrales.map((u, j) => (j === i ? { ...u, [campo]: valor } : u)) });
  const restablecer = (i: number) =>
    setConfig({ ...config, umbrales: config.umbrales.map((u, j) => (j === i ? { ...u, advertencia: u.defectoAdvertencia, excepcion: u.defectoExcepcion, recuperacion: u.defectoRecuperacion } : u)) });
  const esPersonalizado = (u: UmbralAdmin) => u.advertencia !== u.defectoAdvertencia || u.excepcion !== u.defectoExcepcion || u.recuperacion !== u.defectoRecuperacion;

  async function guardar() {
    if (!sesion) return;
    setGuardando(true);
    setMensaje(null);
    try {
      // Solo se guardan los umbrales distintos del valor de fábrica.
      const personalizados = config.umbrales.filter(esPersonalizado)
        .map((u) => ({ metrica: u.metrica, tipoHost: u.tipoHost, advertencia: u.advertencia, excepcion: u.excepcion, recuperacion: u.recuperacion }));
      await admin.guardarAlertas(sesion.token, config.persistenciaMinutos, personalizados);
      setMensaje({ tipo: "exito", texto: "Guardado. Todos los agentes aplican la configuración en menos de 5 minutos, sin reinstalar." });
    } catch (e) {
      setMensaje({ tipo: "error", texto: esFalla(e) ? e.detalle.mensaje : "No se pudo guardar." });
    } finally {
      setGuardando(false);
    }
  }

  const grupos: { tipo: UmbralAdmin["tipoHost"]; titulo: string }[] = [{ tipo: "windows", titulo: "Equipos Windows" }, { tipo: "android", titulo: "Celulares Android" }];

  return (
    <section className={s.panel}>
      <div className={s.persistencia}>
        <label htmlFor="persistencia">
          <strong>Persistencia antes de avisar</strong>
          <span className={s.tenue}>Una alerta solo se abre, escala o se da por solucionada si la condición se mantiene este tiempo. Evita la lluvia de avisos.</span>
        </label>
        <div className={s.campoMinutos}>
          <input id="persistencia" type="number" min={1} max={60} value={config.persistenciaMinutos}
            onChange={(e) => setConfig({ ...config, persistenciaMinutos: Number(e.target.value) })} />
          <span>minutos</span>
        </div>
      </div>

      {grupos.map((g) => (
        <div key={g.tipo} className={s.grupo}>
          <h3 className={s.tituloGrupo}>{g.titulo}</h3>
          <div className={s.desplazable}>
            <table className={s.tabla}>
              <thead><tr><th>Métrica</th><th>Advertencia</th><th>Excepción</th><th>Recuperación</th><th /></tr></thead>
              <tbody>
                {config.umbrales.map((u, i) => u.tipoHost !== g.tipo ? null : (
                  <tr key={`${u.tipoHost}-${u.metrica}`}>
                    <td>{u.nombre}{u.metrica.startsWith("disco.") && <span className={s.tenue}> (cada unidad)</span>}{esPersonalizado(u) && <span className={s.personalizado}>personalizado</span>}</td>
                    {(["advertencia", "excepcion", "recuperacion"] as const).map((c) => (
                      <td key={c}>
                        <input className={s.numero} type="number" step="any" value={u[c]} aria-label={`${u.nombre}: ${c}`}
                          onChange={(e) => cambiar(i, c, Number(e.target.value))} data-nivel={c} />
                      </td>
                    ))}
                    <td>{esPersonalizado(u) && <button type="button" className={s.restablecer} onClick={() => restablecer(i)} title="Volver al valor de fábrica"><RotateCcw size={14} aria-hidden="true" /> Restablecer</button>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ))}

      {mensaje && <div className={s.bloque}><Alerta tipo={mensaje.tipo}>{mensaje.texto}</Alerta></div>}
      <div className={s.acciones}>
        <Boton cargando={guardando} onClick={() => void guardar()}>Guardar configuración</Boton>
      </div>
    </section>
  );
}
