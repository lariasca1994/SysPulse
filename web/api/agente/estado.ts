// PUT /api/agente/estado — último estado del equipo (un documento por host en estado_actual).
import { autorizado, base, esObjeto, ID_HOST, leerJson, respuesta, tipoHost, valores } from "../_ingesta.js";

export async function PUT(req: Request): Promise<Response> {
  if (!autorizado(req)) return respuesta(401);
  const c = await leerJson(req);
  if (!esObjeto(c) || typeof c.host !== "string" || !ID_HOST.test(c.host)) return respuesta(400);
  const tipo = tipoHost(c.tipo);
  const lecturas = valores(c.lecturas);
  if (!tipo || !lecturas) return respuesta(400);

  await (await base()).collection("estado_actual").replaceOne(
    { _id: c.host as never },
    { tipo_host: tipo, ts: new Date(), lecturas },
    { upsert: true },
  );
  return respuesta(204);
}
