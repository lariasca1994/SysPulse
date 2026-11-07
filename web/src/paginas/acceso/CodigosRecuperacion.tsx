import { useState } from "react";
import { Check, Copy, Download } from "lucide-react";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { nombreArchivoCodigos, textoArchivoCodigos } from "./recuperacion";
import s from "./acceso.module.css";

/**
 * Códigos de recuperación nuevos: se muestran una sola vez (copiar, descargar) y exigen confirmar que se guardaron.
 * Se usa al cambiar la contraseña en Mi cuenta y en el primer ingreso después de "Olvidé mi contraseña".
 */
export function CodigosRecuperacion({ codigos, textoContinuar, alContinuar }: {
  codigos: string[];
  textoContinuar: string;
  alContinuar: () => void;
}) {
  const [guardados, setGuardados] = useState(false);
  const [copiado, setCopiado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function copiar() {
    try {
      await navigator.clipboard.writeText(codigos.join("\n"));
      setCopiado(true);
      window.setTimeout(() => setCopiado(false), 2000);
    } catch {
      setError("No se pudo copiar. Selecciona el texto y cópialo manualmente.");
    }
  }

  function descargar() {
    const url = URL.createObjectURL(new Blob([textoArchivoCodigos(codigos)], { type: "text/plain;charset=utf-8" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: nombreArchivoCodigos });
    a.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className={s.formulario}>
      <Alerta tipo="aviso">Tus códigos de recuperación anteriores ya no sirven. Guarda estos: solo se muestran ahora.</Alerta>
      {error && <Alerta tipo="error">{error}</Alerta>}
      <ul className={s.codigos} aria-label="Códigos de recuperación nuevos">
        {codigos.map((c) => <li key={c}>{c}</li>)}
      </ul>
      <div className={s.acciones}>
        <Boton type="button" variante="secundario" onClick={() => void copiar()}>
          {copiado ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />} {copiado ? "Copiados" : "Copiar"}
        </Boton>
        <Boton type="button" variante="secundario" onClick={descargar}>
          <Download size={16} aria-hidden="true" /> Descargar .txt
        </Boton>
      </div>
      <label className={s.check}>
        <input type="checkbox" checked={guardados} onChange={(e) => setGuardados(e.target.checked)} />
        Guardé mis códigos en un lugar seguro (gestor de contraseñas o papel).
      </label>
      <Boton type="button" ancho disabled={!guardados} onClick={alContinuar}>{textoContinuar}</Boton>
    </div>
  );
}
