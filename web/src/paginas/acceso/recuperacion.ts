/** Reglas de las pantallas de recuperación de acceso (sin React: se prueban con `npm test`). */

/** Mensaje si la confirmación no coincide con la contraseña nueva; undefined si está bien o aún vacía. */
export function errorConfirmacion(nueva: string, confirmacion: string): string | undefined {
  return confirmacion && nueva !== confirmacion ? "Las contraseñas no coinciden." : undefined;
}

/** "Olvidé mi contraseña", paso 2: solo contraseña nueva y su confirmación (sin códigos). */
export function puedeGuardarPassword(nueva: string, confirmacion: string): boolean {
  return nueva.length > 0 && confirmacion.length > 0 && errorConfirmacion(nueva, confirmacion) === undefined;
}

/** Código de 6 dígitos (correo o app autenticadora). */
export function esCodigoSeisDigitos(codigo: string): boolean {
  return /^\d{6}$/.test(codigo.trim());
}
