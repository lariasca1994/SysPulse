// Service worker de SysPulse.
// - Guarda solo la "cáscara" (index, /assets, íconos, fuentes). Nunca guarda respuestas de la API.
// - Navegación primero por red; si no hay conexión, muestra offline.html.
// - Sin push, sin background sync y sin consultas periódicas (regla de capas gratuitas).

const VERSION = "syspulse-v1";
const PRECACHE = ["/", "/offline.html", "/manifest.webmanifest", "/icons/icon.svg"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(PRECACHE)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((claves) => Promise.all(claves.filter((c) => c !== VERSION).map((c) => caches.delete(c)))),
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  // Solo recursos del propio sitio; la API (Render) nunca pasa por la caché.
  if (url.origin !== self.location.origin) return;

  if (request.mode === "navigate") {
    event.respondWith(fetch(request).catch(() => caches.match("/offline.html")));
    return;
  }

  const estatico = url.pathname.startsWith("/assets/") || url.pathname.startsWith("/icons/");
  if (!estatico) return;

  event.respondWith(
    caches.match(request).then(
      (guardado) =>
        guardado ||
        fetch(request).then((respuesta) => {
          if (respuesta.ok) {
            const copia = respuesta.clone();
            caches.open(VERSION).then((cache) => cache.put(request, copia));
          }
          return respuesta;
        }),
    ),
  );
});
