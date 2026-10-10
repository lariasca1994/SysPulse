import { useEffect, useState } from "react";

// PWA de SysPulse: registra el service worker y ofrece instalar la app.
// Regla de capas gratuitas: el service worker no hace push, sync en segundo plano
// ni consultas periódicas; nada mantiene despierta a la API.

export function registrarServiceWorker(): void {
  if (!("serviceWorker" in navigator) || !window.isSecureContext || import.meta.env.DEV) return;
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => undefined);
  });
}

interface EventoInstalacion extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

export type ModoInstalacion = "boton" | "ios" | null;

function instalada(): boolean {
  const iosStandalone = (navigator as Navigator & { standalone?: boolean }).standalone === true;
  return window.matchMedia("(display-mode: standalone)").matches || iosStandalone;
}

function esIos(): boolean {
  const ipadModerno = navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1;
  return /iphone|ipad|ipod/i.test(navigator.userAgent) || ipadModerno;
}

/**
 * "boton": el navegador (Chrome/Edge en Android, Windows, macOS, Linux) permite instalar con un clic.
 * "ios": Safari no tiene diálogo; se guía con Compartir → Agregar a inicio.
 * null: ya instalada o el navegador no lo permite.
 */
export function useInstalacion(): { modo: ModoInstalacion; instalar: () => Promise<void> } {
  const [evento, setEvento] = useState<EventoInstalacion | null>(null);
  const [yaInstalada, setYaInstalada] = useState(instalada);

  useEffect(() => {
    const alOfrecer = (e: Event) => {
      e.preventDefault();
      setEvento(e as EventoInstalacion);
    };
    const alInstalar = () => {
      setYaInstalada(true);
      setEvento(null);
    };
    window.addEventListener("beforeinstallprompt", alOfrecer);
    window.addEventListener("appinstalled", alInstalar);
    return () => {
      window.removeEventListener("beforeinstallprompt", alOfrecer);
      window.removeEventListener("appinstalled", alInstalar);
    };
  }, []);

  const modo: ModoInstalacion = yaInstalada ? null : evento ? "boton" : esIos() ? "ios" : null;

  return {
    modo,
    instalar: async () => {
      if (!evento) return;
      await evento.prompt();
      await evento.userChoice;
      setEvento(null);
    },
  };
}
