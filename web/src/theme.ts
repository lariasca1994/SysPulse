// Tema claro/oscuro: por defecto sigue al sistema; el usuario puede fijarlo.
// index.html aplica el tema guardado antes de pintar, para que no haya parpadeo.

export type Preferencia = "sistema" | "claro" | "oscuro";

const CLAVE = "syspulse.tema";

export function leerPreferencia(): Preferencia {
  try {
    const valor = localStorage.getItem(CLAVE);
    return valor === "claro" || valor === "oscuro" ? valor : "sistema";
  } catch {
    return "sistema";
  }
}

export function aplicarTema(preferencia: Preferencia): void {
  try {
    if (preferencia === "sistema") localStorage.removeItem(CLAVE);
    else localStorage.setItem(CLAVE, preferencia);
  } catch {
    // Sin almacenamiento disponible: el tema solo dura esta sesión.
  }

  const oscuro =
    preferencia === "oscuro" ||
    (preferencia === "sistema" && window.matchMedia("(prefers-color-scheme: dark)").matches);

  document.documentElement.dataset.theme = oscuro ? "dark" : "light";
  const color = getComputedStyle(document.documentElement).getPropertyValue("--nav").trim();
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", color);
}

export function escucharSistema(alCambiar: () => void): () => void {
  const consulta = window.matchMedia("(prefers-color-scheme: dark)");
  consulta.addEventListener("change", alCambiar);
  return () => consulta.removeEventListener("change", alCambiar);
}
