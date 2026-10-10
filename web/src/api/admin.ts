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
