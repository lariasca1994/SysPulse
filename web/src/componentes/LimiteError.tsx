import { Component, type ReactNode } from "react";
import { AlertCircle } from "lucide-react";
import s from "./LimiteError.module.css";

/** Si una pantalla falla, se muestra un aviso en su lugar en vez de dejar toda la aplicación en blanco. Se reinicia al navegar (el layout le pasa la ruta como key). */
export class LimiteError extends Component<{ children: ReactNode }, { error: boolean }> {
  state = { error: false };

  static getDerivedStateFromError() {
    return { error: true };
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className={s.caja} role="alert">
        <AlertCircle size={22} aria-hidden="true" />
        <div>
          <h2>Esta sección no se pudo mostrar</h2>
          <p>Recarga la página. Si el problema continúa, avisa al administrador.</p>
          <button type="button" onClick={() => window.location.reload()}>Recargar</button>
        </div>
      </div>
    );
  }
}
