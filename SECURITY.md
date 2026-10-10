# Seguridad y publicación

No subas `.env`, archivos `.env.*` (salvo `.env.example` y `web/.env.production`, que solo contiene la
URL pública de la API), llaves de firma (`*.keystore`, `*.jks`), bases locales del agente (`*.db`) ni
la carpeta `artifacts/`. Antes de publicar, revisa `git status --ignored` y confirma que solo aparecen
los archivos fuente esperados.

Los secretos (cadenas de conexión, claves de JWT, MFA y agente, tokens de Telegram y Brevo) se
configuran solo como variables de entorno en Render y en el `.env` local. Los equipos reciben los suyos
al canjear un código de alta de un solo uso y los guardan cifrados (DPAPI en Windows, Android Keystore
en el celular).

## Instaladores públicos

El repositorio `syspulse-agentes` es público y solo contiene binarios. Antes de cada publicación:

1. Compila en `Release` (sin símbolos de depuración y sin rutas del equipo de compilación).
2. Ejecuta `tools/escanear-secretos.ps1` sobre el zip y el APK: debe terminar con 0 hallazgos.
3. Confirma que el APK no es depurable (`aapt2 dump badging` no debe mostrar `application-debuggable`).
4. Anuncia la versión con `SysPulse.Herramientas publicar-version`, que registra la huella SHA-256 en la
   configuración central: los equipos rechazan cualquier archivo que no coincida.

Las herramientas de administración (`tools/SysPulse.Herramientas`) nunca se publican.

## Si una credencial queda expuesta

Revócala y rota su valor de inmediato (Atlas, Telegram, Brevo, Render). Borrar el archivo en un commit
posterior no elimina su contenido del historial. Las sesiones se invalidan al rotar `JWT_SECRET`.
