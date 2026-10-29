// POST /api/agente/metricas — lote de ventanas de 5 minutos (metricas_5m). Reintentos: las ventanas repetidas se ignoran.
import { MongoBulkWriteError } from "mongodb";
import { autorizado, base, CLAVE_METRICA, esObjeto, ID_HOST, leerJson, MAX_DOCUMENTOS, MAX_METRICAS, respuesta, tipoHost } from "../_ingesta.js";

const RESUMEN = ["min", "avg", "max", "p95"] as const;

function documento(d: unknown): Record<string, unknown> | null {
  if (!esObjeto(d) || typeof d.host !== "string" || !ID_HOST.test(d.host)) return null;
  const tipo = tipoHost(d.tipo_host);
  const ts = typeof d.ts === "string" ? new Date(d.ts) : null;
  if (!tipo || !ts || Number.isNaN(ts.getTime()) || Math.abs(Date.now() - ts.getTime()) > 8 * 86_400_000) return null;
  if (!esObjeto(d.metricas)) return null;
  const entradas = Object.entries(d.metricas);
  if (entradas.length > MAX_METRICAS) return null;

  const metricas: Record<string, unknown> = {};
  for (const [clave, r] of entradas) {
    if (!CLAVE_METRICA.test(clave) || !esObjeto(r)) return null;
    if (!RESUMEN.every((k) => typeof r[k] === "number" && Number.isFinite(r[k]))) return null;
    if (typeof r.n !== "number" || !Number.isInteger(r.n) || r.n < 0) return null;
    const unidad = typeof r.u === "string" && r.u.length <= 20 ? r.u : "";
    metricas[clave] = { min: r.min, avg: r.avg, max: r.max, p95: r.p95, n: r.n, u: unidad };
  }
  return { host: d.host, tipo_host: tipo, ts, metricas };
}

export async function POST(req: Request): Promise<Response> {
  if (!autorizado(req)) return respuesta(401);
  const c = await leerJson(req);
  if (!Array.isArray(c) || c.length === 0 || c.length > MAX_DOCUMENTOS) return respuesta(400);
  const docs = c.map(documento);
  if (docs.some((d) => d === null)) return respuesta(400);

  try {
    await (await base()).collection("metricas_5m").insertMany(docs as Record<string, unknown>[], { ordered: false });
  } catch (e) {
    // Lote reenviado tras un corte: las ventanas que ya estaban (clave duplicada) no son un error.
    const soloDuplicados = e instanceof MongoBulkWriteError
      && (Array.isArray(e.writeErrors) ? e.writeErrors : [e.writeErrors]).every((w) => w.code === 11000);
    if (!soloDuplicados) throw e;
  }
  return respuesta(204);
}
