import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@fontsource/ibm-plex-sans/400.css";
import "@fontsource/ibm-plex-sans/500.css";
import "@fontsource/ibm-plex-sans/600.css";
import "@fontsource/ibm-plex-mono/400.css";
import "./styles/global.css";
import App from "./App";
import { registrarServiceWorker } from "./pwa";
import { vigilarDespliegues } from "./recargaDespliegue";

registrarServiceWorker();
vigilarDespliegues();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
