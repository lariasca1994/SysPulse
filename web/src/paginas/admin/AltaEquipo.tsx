import { useState } from "react";
import { Check, Copy, Download, Monitor, Plus, Smartphone } from "lucide-react";
import { DESCARGAS, equipos } from "../../api/admin";
import { esFalla } from "../../api/cliente";
import { useSesion } from "../../sesionContexto";
import { Alerta, Boton } from "../../componentes/ui/Basicos";
import { Dialogo } from "../../componentes/ui/Dialogo";
import s from "./admin.module.css";

const fecha = (iso: string) => new Date(iso).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });

/**
 * "Agregar equipo": genera un código de alta de un solo uso (24 h) y muestra cómo usarlo en Windows o Android.
 * Lo usan los técnicos N2 y N3 (Salud de hosts) y el admin (Administración → Equipos). Los instaladores públicos no
 * contienen secretos: el equipo recibe solo su credencial limitada al canjear el código.
 */
export function AltaEquipo({ alGenerar }: { alGenerar?: () => void }) {
  const { sesion } = useSesion();
  const [nuevo, setNuevo] = useState<{ codigo: string; expira: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [copiado, setCopiado] = useState<string | null>(null);

  async function generar() {
    if (!sesion) return;
    setOcupado(true);
    setError(null);
    try {
      setNuevo(await equipos.nuevoCodigo(sesion.token));
      alGenerar?.();
    } catch (e) {
      setError(esFalla(e) ? e.detalle.mensaje : "No se pudo generar el código.");
    } finally {
      setOcupado(false);
    }
  }

  async function copiar(texto: string, que: string) {
    try {
      await navigator.clipboard.writeText(texto);
      setCopiado(que);
      window.setTimeout(() => setCopiado(null), 2000);
    } catch {
      // Sin portapapeles: el texto queda visible para copiarlo a mano.
    }
  }

  const comando = nuevo ? `.\\SysPulse.AgentInstaller.exe install --codigo ${nuevo.codigo}` : "";

  return (
    <>
      <Boton cargando={ocupado} onClick={() => void generar()}><Plus size={16} aria-hidden="true" /> Agregar equipo</Boton>
      {error && <Alerta tipo="error">{error}</Alerta>}

      {nuevo && (
        <Dialogo abierto titulo="Código de alta" descripcion="Sirve para un solo equipo y vence en 24 horas. No lo compartas por canales públicos." alCerrar={() => setNuevo(null)}>
          <div className={s.formulario}>
            <div className={s.codigoGrande}>
              <span className="num">{nuevo.codigo}</span>
              <Boton variante="fantasma" onClick={() => void copiar(nuevo.codigo, "codigo")} aria-label="Copiar código">
                {copiado === "codigo" ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
              </Boton>
            </div>
            <p className={s.tenue}>Vence: {fecha(nuevo.expira)}</p>

            <div className={s.instruccion}>
              <h4><Monitor size={16} aria-hidden="true" /> Computador, portátil o tablet Windows</h4>
              <ol>
                <li>Descarga y descomprime el instalador: <a href={DESCARGAS.windows}><Download size={13} aria-hidden="true" /> SysPulse.Agent-win-x64.zip</a></li>
                <li>En esa carpeta, abre PowerShell <strong>como administrador</strong> y ejecuta:</li>
              </ol>
              <div className={s.comando}>
                <code>{comando}</code>
                <Boton variante="fantasma" onClick={() => void copiar(comando, "comando")} aria-label="Copiar comando">
                  {copiado === "comando" ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
                </Boton>
              </div>
              <p className={s.tenue}>¿Poco espacio en C:? Agrega <code>--destino D:\SysPulse\Agent --datos D:\SysPulse\Datos</code></p>
            </div>

            <div className={s.instruccion}>
              <h4><Smartphone size={16} aria-hidden="true" /> Celular Android</h4>
              <ol>
                <li>Descarga e instala la app: <a href={DESCARGAS.android}><Download size={13} aria-hidden="true" /> SysPulse-agente-android.apk</a> (permite "instalar apps desconocidas" si lo pide).</li>
                <li>Abre <strong>SysPulse Agent</strong>, escribe el código y toca <strong>Vincular</strong>.</li>
              </ol>
            </div>
            <p className={s.tenue}>En menos de un minuto el equipo aparece en Salud de hosts con un nombre genérico (ej. «Equipo portátil 3»). Después se actualiza solo.</p>
          </div>
        </Dialogo>
      )}
    </>
  );
}
