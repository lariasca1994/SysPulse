import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { API_BASE } from "./base";
import { get } from "./cliente";

export interface Mensaje {
  id: string;
  salaId: string;
  tipo: "humano" | "sistema";
  autorId: string | null;
  autorNombre: string | null;
  autorRol: string | null;
  texto: string;
  ts: string;
  retirado: boolean;
}

export interface Sala {
  id: string;
  tipo: "general" | "incidente";
  titulo: string;
  incidenteId: string | null;
  codigo: string | null;
  prioridad: string | null;
  estado: string | null;
  soloLectura: boolean;
  ultimaActividad: string;
  noLeidos: number;
  ultimo: Mensaje | null;
}

export interface Persona {
  id: string;
  nombre: string;
  rol: string;
}

export const salas = {
  listar: (token: string) => get<Sala[]>("/api/salas", token),
  mensajes: (token: string, salaId: string, antes?: string) =>
    get<Mensaje[]>(`/api/salas/${encodeURIComponent(salaId)}/mensajes${antes ? `?antes=${antes}` : ""}`, token),
};

export const MAX_TEXTO = 2000;
/** Con la pestaña oculta más de este tiempo se cierra la conexión (ahorra el ancho de banda gratuito de Render). */
const INACTIVIDAD_MS = 15 * 60_000;

/** Crea la conexión del chat. El token viaja como access_token (SignalR no admite cabeceras en WebSocket). */
export function crearConexion(token: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/chat`, { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();
}

/** Pausa la conexión con la pestaña oculta y la reanuda al volver. Devuelve la función para dejar de vigilar. */
export function vigilarVisibilidad(conexion: HubConnection, arrancar: () => Promise<void>): () => void {
  let temporizador: number | undefined;
  const alCambiar = () => {
    if (document.visibilityState === "hidden") {
      temporizador = window.setTimeout(() => void conexion.stop(), INACTIVIDAD_MS);
    } else {
      window.clearTimeout(temporizador);
      if (conexion.state === HubConnectionState.Disconnected) void arrancar();
    }
  };
  document.addEventListener("visibilitychange", alCambiar);
  return () => {
    window.clearTimeout(temporizador);
    document.removeEventListener("visibilitychange", alCambiar);
  };
}
