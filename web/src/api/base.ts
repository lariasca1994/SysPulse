// URL de la API. En producción viene de web/.env.production; un valor vacío o con espacios se ignora
// (antes un valor vacío hacía que la web se llamara a sí misma y fallara con 405).
const configurada = ((import.meta.env.VITE_API_URL as string | undefined) ?? "").trim().replace(/\/+$/, "");

export const API_BASE = configurada || "http://localhost:5095";
