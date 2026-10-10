import { get, post } from "./cliente";

export type Nivel = "N1" | "N2" | "N3";

export interface Perfil {
  id: string;
  nombre: string;
  email: string;
  rol: "N1" | "N2" | "N3" | "gestor" | "admin";
  telegram: string | null;
  telegramVerificado: boolean;
}

export interface Sesion {
  token: string;
  expira: string;
  usuario: Perfil;
}

export interface ResultadoRegistro {
  usuarioId: string;
  enlaceTelegram: string;
  telegramMostrado: string;
  enlaceCorreoDesarrollo: string | null;
}

export type PasoLogin =
  | { paso: "cambiar-password"; token: string }
  | { paso: "configurar-mfa"; token: string }
  | { paso: "mfa"; token: string };

export const auth = {
  registro: (datos: { nombre: string; email: string; password: string; nivel: Nivel; telegram: string; pais: string }) =>
    post<ResultadoRegistro>("/api/auth/registro", datos),
  verificarCorreo: (token: string) => post<{ mensaje: string }>("/api/auth/verificar-correo", { token }),
  confirmarTelegram: (usuarioId: string, codigo: string) => post<{ mensaje: string }>("/api/auth/telegram/confirmar", { usuarioId, codigo }),
  nuevoEnlaceTelegram: (usuarioId: string) => post<{ enlaceTelegram: string }>("/api/auth/telegram/nuevo-enlace", { usuarioId }),
  login: (email: string, password: string) => post<PasoLogin>("/api/auth/login", { email, password }),
  cambiarPassword: (token: string, nueva: string) => post<PasoLogin>("/api/auth/cambiar-password", { token, nueva }),
  iniciarMfa: (token: string) => post<{ secreto: string; uri: string }>("/api/auth/mfa/iniciar", { token }),
  activarMfa: (token: string, codigo: string) =>
    post<{ codigosRecuperacion: string[]; sesion: Sesion }>("/api/auth/mfa/activar", { token, codigo }),
  verificarMfa: (token: string, codigo: string) => post<{ sesion: Sesion }>("/api/auth/mfa/verificar", { token, codigo }),
  yo: (token: string) => get<Perfil>("/api/auth/yo", token),
  cambiarPasswordConSesion: (token: string, actual: string, nueva: string) =>
    post<{ sesion: Sesion }>("/api/auth/password/cambiar", { actual, nueva }, token),
  cambiarMfaIniciar: (token: string, password: string, codigo: string) =>
    post<{ secreto: string; uri: string }>("/api/auth/mfa/cambiar/iniciar", { password, codigo }, token),
  cambiarMfaConfirmar: (token: string, codigo: string) =>
    post<{ codigosRecuperacion: string[]; sesion: Sesion }>("/api/auth/mfa/cambiar/confirmar", { codigo }, token),
  olvidePassword: (email: string) =>
    post<{ mensaje: string; enlaceDesarrollo: string | null }>("/api/auth/password/olvide", { email }),
  restablecerPassword: (token: string, nueva: string, codigo: string) =>
    post<{ mensaje: string }>("/api/auth/password/restablecer", { token, nueva, codigo }),
};
