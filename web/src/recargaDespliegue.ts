// Después de un despliegue, una pestaña (o la app instalada) que quedó abierta todavía tiene el
// índice anterior: al entrar a una pantalla que se carga aparte (por ejemplo Administración)
// pide un archivo cuyo nombre ya no existe y la pantalla fallaba con "Esta sección no se pudo
// mostrar". En ese caso se recarga la página una sola vez para tomar la versión nueva.

const CLAVE = "syspulse-recarga-despliegue";

/** Recarga una vez por dirección; si vuelve a fallar en la misma, deja ver el aviso de error. */
export function recargarPorDespliegue(): boolean {
  try {
    if (sessionStorage.getItem(CLAVE) === location.pathname) return false;
    sessionStorage.setItem(CLAVE, location.pathname);
  } catch {
    return false;
  }
  location.reload();
  return true;
}

/** Errores de carga de un trozo de la aplicación (import dinámico que no existe). */
export function esFallaDeCarga(error: unknown): boolean {
  const texto = error instanceof Error ? `${error.name} ${error.message}` : String(error);
  return /dynamically imported module|Importing a module script failed|error loading dynamically|Unable to preload|ChunkLoadError/i.test(texto);
}

export function vigilarDespliegues(): void {
  window.addEventListener("vite:preloadError", (evento) => {
    if (recargarPorDespliegue()) evento.preventDefault();
  });
  // Si la página cargó bien, se olvida la marca para el próximo despliegue.
  window.addEventListener("load", () => {
    window.setTimeout(() => {
      try { sessionStorage.removeItem(CLAVE); } catch { /* sin almacenamiento */ }
    }, 10_000);
  });
}
