import { get, post, put } from "./cliente";

export interface UsuarioAdmin {
  id: string;
  nombre: string;
  email: string;
  rol: string;
  nivelTecnico: string;
  activo: boolean;
  telegramVerificado: boolean;
  mfaActivo: boolean;
  creado: string;
  ultimaConexion: string | null;
}

export interface Solicitud {
  id: string;
  usuarioId: string;
  usuarioNombre: string;
  nivelActual: string;
  motivo: string;
  estado: "pendiente" | "aprobada" | "rechazada" | "revocada";
  resueltoPorNombre: string | null;
  comentario: string | null;
  creado: string;
  resuelto: string | null;
}

export interface UmbralAdmin {
  metrica: string;
  nombre: string;
  tipoHost: "windows" | "android";
  advertencia: number;
  excepcion: number;
  recuperacion: number;
  personalizado: boolean;
  defectoAdvertencia: number;
  defectoExcepcion: number;
  defectoRecuperacion: number;
}

export interface ConfiguracionAlertas {
  persistenciaMinutos: number;
  umbrales: UmbralAdmin[];
}

export const admin = {
  usuarios: (t: string) => get<UsuarioAdmin[]>("/api/admin/usuarios", t),
  cambiarActivo: (t: string, id: string, activo: boolean) => post<{ ok: boolean }>(`/api/admin/usuarios/${id}/activo`, { activo }, t),
  revocarGestor: (t: string, id: string) => post<{ ok: boolean }>(`/api/admin/usuarios/${id}/revocar-gestor`, {}, t),
  solicitudes: (t: string) => get<Solicitud[]>("/api/admin/solicitudes", t),
  resolver: (t: string, id: string, aprobar: boolean, comentario?: string) =>
    post<{ ok: boolean }>(`/api/admin/solicitudes/${id}/resolver`, { aprobar, comentario }, t),
  alertas: (t: string) => get<ConfiguracionAlertas>("/api/admin/alertas", t),
  guardarAlertas: (t: string, persistenciaMinutos: number, umbrales: { metrica: string; tipoHost: string; advertencia: number; excepcion: number; recuperacion: number }[]) =>
    put<{ ok: boolean }>("/api/admin/alertas", { persistenciaMinutos, umbrales }, t),
};

export const cuenta = {
  miSolicitud: (t: string) => get<Solicitud | null>("/api/cuenta/solicitud-gestor", t),
  solicitarGestor: (t: string, motivo: string) => post<{ ok: boolean }>("/api/cuenta/solicitud-gestor", { motivo }, t),
};

export interface Equipo {
  id: string;
  nombre: string;
  clase: string;
  tipo: "windows" | "android" | string;
  versionAgente: string | null;
  ultimoReporte: string | null;
  conexion: "en-linea" | "retrasado" | "sin-reporte";
  retirado: boolean;
  retiradoEn: string | null;
}

export interface CodigoAlta {
  pista: string;
  creadoPor: string;
  creado: string;
  expira: string;
  estado: "pendiente" | "usado" | "vencido";
  tipoUsado: string | null;
  usadoEn: string | null;
}

export const equipos = {
  listar: (t: string) => get<Equipo[]>("/api/admin/equipos", t),
  renombrar: (t: string, id: string, nombre: string) => post<{ ok: boolean }>(`/api/admin/equipos/${encodeURIComponent(id)}/nombre`, { nombre }, t),
  retirar: (t: string, id: string, retirar: boolean) => post<{ ok: boolean }>(`/api/admin/equipos/${encodeURIComponent(id)}/retiro`, { retirar }, t),
  codigos: (t: string) => get<CodigoAlta[]>("/api/admin/altas", t),
  nuevoCodigo: (t: string) => post<{ codigo: string; expira: string }>("/api/admin/altas", {}, t),
};

/** Siempre la última versión publicada de los instaladores (repositorio público, solo binarios). */
export const DESCARGAS = {
  windows: "https://github.com/lariasca1994/syspulse-agentes/releases/latest/download/SysPulse.Agent-win-x64.zip",
  android: "https://github.com/lariasca1994/syspulse-agentes/releases/latest/download/SysPulse-agente-android.apk",
};
