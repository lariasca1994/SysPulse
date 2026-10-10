// Cliente HTTP de la API de SysPulse. Normaliza errores (ProblemDetails de ASP.NET) a un formato único.

const BASE = (import.meta.env.VITE_API_URL as string | undefined) ?? "http://localhost:5095";

export interface ErrorApi {
  estado: number;
  mensaje: string;
  /** Errores por campo (validación del registro, etc.). */
  campos: Record<string, string>;
  /** Cuerpo completo, para respuestas con datos además del error (ej. paso de login). */
  datos?: Record<string, unknown>;
}

export class FallaApi extends Error {
  readonly detalle: ErrorApi;

  constructor(detalle: ErrorApi) {
    super(detalle.mensaje);
    this.detalle = detalle;
  }
}

const MENSAJE_RED = "No hay conexión con el servidor. Si acaba de despertar, espera unos segundos e inténtalo de nuevo.";

export async function post<T>(ruta: string, cuerpo: unknown, token?: string): Promise<T> {
  return solicitar<T>("POST", ruta, cuerpo, token);
}

export async function get<T>(ruta: string, token?: string): Promise<T> {
  return solicitar<T>("GET", ruta, undefined, token);
}

async function solicitar<T>(metodo: string, ruta: string, cuerpo: unknown, token?: string): Promise<T> {
  let respuesta: Response;
  try {
    respuesta = await fetch(`${BASE}${ruta}`, {
      method: metodo,
      headers: {
        ...(cuerpo !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: cuerpo !== undefined ? JSON.stringify(cuerpo) : undefined,
    });
  } catch {
    throw new FallaApi({ estado: 0, mensaje: MENSAJE_RED, campos: {} });
  }

  const texto = await respuesta.text();
  const json = texto ? (JSON.parse(texto) as Record<string, unknown>) : {};
  if (respuesta.ok) return json as T;

  const campos: Record<string, string> = {};
  const errores = json.errors as Record<string, string[]> | undefined;
  if (errores) for (const [campo, lista] of Object.entries(errores)) campos[campo.toLowerCase()] = lista[0];

  const mensaje =
    (json.detail as string | undefined) ??
    (json.mensaje as string | undefined) ??
    (respuesta.status === 429 ? "Demasiados intentos seguidos. Espera un minuto." : undefined) ??
    (json.title as string | undefined) ??
    "Ocurrió un error inesperado.";

  throw new FallaApi({ estado: respuesta.status, mensaje, campos, datos: json });
}

export function esFalla(e: unknown): e is FallaApi {
  return e instanceof FallaApi;
}
