// Utilidades de la ruta de ingesta de los agentes Android (funciones de Vercel).
// Seguridad: clave del agente comparada en tiempo constante, solo cuatro operaciones, entradas validadas y acotadas,
// y nunca se registran secretos ni el contenido recibido. Los datos se guardan con el mismo formato que el agente
// de Windows escribe directo en Atlas.
import { timingSafeEqual } from "node:crypto";
import { MongoClient, type Db } from "mongodb";

export const ID_HOST = /^(win|and)-[0-9a-f]{10}$/;
export const CLAVE_METRICA = /^[A-Za-z0-9_]{1,80}$/;
export const MAX_METRICAS = 200;
export const MAX_DOCUMENTOS = 100;
const MAX_CUERPO = 512 * 1024;

let cliente: MongoClient | null = null;

/** Cliente reutilizado entre invocaciones de la misma instancia (pocas conexiones a Atlas). */
export async function base(): Promise<Db> {
  const uri = (process.env.MONGO_URI_INGESTA ?? process.env.MONGO_URI)?.trim();
  if (!uri) throw new Error("Falta la configuración de la base");
  if (!cliente) {
    cliente = new MongoClient(uri, { maxPoolSize: 3, appName: "SysPulse.Ingesta", serverSelectionTimeoutMS: 15000 });
    await cliente.connect();
  }
  return cliente.db();
}

export function respuesta(estado: number, cuerpo?: unknown): Response {
  return cuerpo === undefined
    ? new Response(null, { status: estado, headers: { "Cache-Control": "no-store" } })
    : Response.json(cuerpo, { status: estado, headers: { "Cache-Control": "no-store" } });
}

/** Clave del agente en el encabezado X-Agent-Key, comparada en tiempo constante. */
export function autorizado(req: Request): boolean {
  // trim: el valor configurado puede traer un salto de línea final según cómo se cargó.
  const esperada = process.env.AGENT_API_KEY?.trim();
  const recibida = req.headers.get("x-agent-key")?.trim();
  if (!esperada || !recibida) return false;
  const a = Buffer.from(recibida);
  const b = Buffer.from(esperada);
  return a.length === b.length && timingSafeEqual(a, b);
}

/** Lee el cuerpo JSON con un tope de tamaño; null si es inválido o demasiado grande. */
export async function leerJson(req: Request): Promise<unknown> {
  const largo = Number(req.headers.get("content-length") ?? "0");
  if (largo > MAX_CUERPO) return null;
  const texto = await req.text();
  if (texto.length > MAX_CUERPO) return null;
  try {
    return JSON.parse(texto);
  } catch {
    return null;
  }
}

export function esObjeto(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

export function tipoHost(v: unknown): "windows" | "android" | null {
  return v === "windows" || v === "android" ? v : null;
}

/** Métricas planas { clave: número } validadas. */
export function valores(v: unknown): Record<string, number> | null {
  if (!esObjeto(v)) return null;
  const entradas = Object.entries(v);
  if (entradas.length > MAX_METRICAS) return null;
  const salida: Record<string, number> = {};
  for (const [clave, valor] of entradas) {
    if (!CLAVE_METRICA.test(clave) || typeof valor !== "number" || !Number.isFinite(valor)) return null;
    salida[clave] = valor;
  }
  return salida;
}
