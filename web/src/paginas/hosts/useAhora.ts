import { useEffect, useState } from "react";

/** Reloj que avanza cada 15 s para que "hace X min" se mantenga al día sin volver a consultar. */
export function useAhora(): number {
  const [ahora, setAhora] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setAhora(Date.now()), 15_000);
    return () => window.clearInterval(id);
  }, []);
  return ahora;
}
