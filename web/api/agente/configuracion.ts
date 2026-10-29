// GET /api/agente/configuracion — configuración central de los agentes (umbrales, versiones publicadas).
// Es el mismo documento que los agentes ya leían en Atlas; se entrega en JSON extendido para conservar tipos y fechas.
import { EJSON } from "bson";
import { autorizado, base, respuesta } from "../_ingesta.js";

export async function GET(req: Request): Promise<Response> {
  if (!autorizado(req)) return respuesta(401);
  const doc = await (await base()).collection("configuracion").findOne({ _id: "agentes" as never });
  if (!doc) return respuesta(204);
  return new Response(EJSON.stringify(doc, { relaxed: true }), {
    status: 200,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store" },
  });
}
