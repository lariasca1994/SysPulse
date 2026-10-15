import { lazy, Suspense, type ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { ProveedorSesion } from "./sesion";
import { useSesion } from "./sesionContexto";
import Login from "./paginas/acceso/Login";
import { LayoutApp } from "./componentes/LayoutApp";
import { Mantenimiento } from "./componentes/Mantenimiento";

// Las pantallas que no se ven al entrar se cargan bajo demanda (la PWA abre más rápido).
const Registro = lazy(() => import("./paginas/acceso/Registro"));
const CompletarRegistro = lazy(() => import("./paginas/acceso/CompletarRegistro"));
const VerificarCorreo = lazy(() => import("./paginas/acceso/VerificarCorreo"));
const CambiarPassword = lazy(() => import("./paginas/acceso/CambiarPassword"));
const ConfigurarMfa = lazy(() => import("./paginas/acceso/ConfigurarMfa"));
const VerificarMfa = lazy(() => import("./paginas/acceso/VerificarMfa"));
const SaludHosts = lazy(() => import("./paginas/hosts/SaludHosts"));
const DetalleHost = lazy(() => import("./paginas/hosts/DetalleHost"));
const Incidentes = lazy(() => import("./paginas/incidentes/Incidentes"));
const DetalleIncidente = lazy(() => import("./paginas/incidentes/DetalleIncidente"));
const MiTurno = lazy(() => import("./paginas/incidentes/MiTurno"));
const OlvidePassword = lazy(() => import("./paginas/acceso/OlvidePassword"));
const Restablecer = lazy(() => import("./paginas/acceso/Restablecer"));
const Perfil = lazy(() => import("./paginas/Perfil"));
const Salas = lazy(() => import("./paginas/salas/Salas"));
const Admin = lazy(() => import("./paginas/admin/Admin"));
const Analitica = lazy(() => import("./paginas/analitica/Analitica"));

function Protegida({ children }: { children: ReactNode }) {
  const { sesion } = useSesion();
  return sesion ? children : <Navigate to="/login" replace />;
}

function SoloInvitado({ children }: { children: ReactNode }) {
  const { sesion } = useSesion();
  return sesion ? <Navigate to="/" replace /> : children;
}

export default function App() {
  return (
    <ProveedorSesion>
      <Mantenimiento />
      <BrowserRouter>
        <Suspense fallback={null}>
          <Routes>
            <Route path="/login" element={<SoloInvitado><Login /></SoloInvitado>} />
            <Route path="/registro" element={<SoloInvitado><Registro /></SoloInvitado>} />
            <Route path="/registro/completar" element={<CompletarRegistro />} />
            <Route path="/verificar-correo" element={<VerificarCorreo />} />
            <Route path="/cambiar-password" element={<CambiarPassword />} />
            <Route path="/mfa/configurar" element={<ConfigurarMfa />} />
            <Route path="/mfa" element={<VerificarMfa />} />
            <Route path="/olvide" element={<SoloInvitado><OlvidePassword /></SoloInvitado>} />
            <Route path="/restablecer" element={<Restablecer />} />
            <Route element={<Protegida><LayoutApp /></Protegida>}>
              <Route index element={<Navigate to="/turno" replace />} />
              <Route path="/hosts" element={<Suspense fallback={null}><SaludHosts /></Suspense>} />
              <Route path="/hosts/:id" element={<Suspense fallback={null}><DetalleHost /></Suspense>} />
              <Route path="/incidentes" element={<Suspense fallback={null}><Incidentes /></Suspense>} />
              <Route path="/incidentes/:id" element={<Suspense fallback={null}><DetalleIncidente /></Suspense>} />
              <Route path="/turno" element={<Suspense fallback={null}><MiTurno /></Suspense>} />
              <Route path="/salas" element={<Suspense fallback={null}><Salas /></Suspense>} />
              <Route path="/salas/:id" element={<Suspense fallback={null}><Salas /></Suspense>} />
              <Route path="/admin" element={<Suspense fallback={null}><Admin /></Suspense>} />
              <Route path="/perfil" element={<Suspense fallback={null}><Perfil /></Suspense>} />
              <Route path="/analitica" element={<Suspense fallback={null}><Analitica /></Suspense>} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </BrowserRouter>
    </ProveedorSesion>
  );
}
