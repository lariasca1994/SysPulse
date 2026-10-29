// POST /api/agente/host — registra el equipo con su modelo comercial; nunca el nombre que le dio el dueño.
import { autorizado, base, esObjeto, ID_HOST, leerJson, respuesta, tipoHost } from "../_ingesta.js";

export async function POST(req: Request): Promise<Response> {
  if (!autorizado(req)) return respuesta(401);
  const c = await leerJson(req);
  if (!esObjeto(c) || typeof c.host !== "string" || !ID_HOST.test(c.host)) return respuesta(400);
  const tipo = tipoHost(c.tipo);
  const version = typeof c.version === "string" && /^[0-9A-Za-z.+-]{1,40}$/.test(c.version) ? c.version : null;
  // Modelo comercial (p. ej. "Lenovo Tab M11"): opcional, acotado y solo con caracteres simples.
  const modelo = typeof c.modelo === "string" && /^[\p{L}\p{N} ._()+\-/·]{1,60}$/u.test(c.modelo.trim()) ? c.modelo.trim() : null;
  if (!tipo || !version) return respuesta(400);

  await (await base()).collection("hosts").replaceOne(
    { _id: c.host as never },
    { tipo, version_agente: version, ultimo_arranque: new Date(), ...(modelo ? { modelo } : {}) },
    { upsert: true },
  );
  return respuesta(204);
}
