import { useEffect, useRef, useState } from "react";

const DURACION = 150;
const NUMERO = /\d+(?:,\d+)?/;

const quieto = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Muestra un valor ya formateado ("95,3%", "2 h 10 min", "12") y anima su primera cifra
 * desde 0 en 150 ms. El texto final es exactamente el recibido; con
 * prefers-reduced-motion (o sin cifra) se muestra directo.
 */
export function Cifra({ valor }: { valor: string }) {
  const [animado, setAnimado] = useState<{ de: string; texto: string } | null>(null);
  const marco = useRef(0);

  useEffect(() => {
    const m = valor.match(NUMERO);
    if (!m || quieto()) return undefined;
    const decimales = m[0].includes(",") ? m[0].split(",")[1].length : 0;
    const destino = Number(m[0].replace(",", "."));
    const inicio = performance.now();
    const paso = (ahora: number) => {
      const avance = Math.min(1, (ahora - inicio) / DURACION);
      const actual = (destino * (1 - (1 - avance) ** 3)).toFixed(decimales).replace(".", ",");
      setAnimado({ de: valor, texto: valor.replace(NUMERO, actual) });
      if (avance < 1) marco.current = requestAnimationFrame(paso);
    };
    marco.current = requestAnimationFrame(paso);
    return () => cancelAnimationFrame(marco.current);
  }, [valor]);

  if (animado?.de === valor) return <>{animado.texto}</>;
  // Antes del primer cuadro arranca en 0 para que no parpadee el valor final.
  return <>{NUMERO.test(valor) && !quieto() ? valor.replace(NUMERO, "0") : valor}</>;
}
