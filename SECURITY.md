# Seguridad

- Nunca subas `.env` ni otros archivos con claves, contraseñas o cadenas de conexión: van solo en
  variables de entorno. `web/.env.production` es la excepción porque solo tiene la dirección pública
  de la API.
- Antes de publicar, revisa `git status --ignored` y confirma que solo aparecen archivos fuente.
- Los instaladores de los agentes se revisan antes de cada publicación para que no lleven secretos.
- Si una credencial queda expuesta, revócala y cámbiala de inmediato: borrarla en un commit posterior
  no la elimina del historial.

## Reportar una vulnerabilidad

Escribe a [ariascluisf@hotmail.com](mailto:ariascluisf@hotmail.com) o por Telegram a
[@lfac6](https://t.me/lfac6) con los pasos para reproducirla. No la publiques mientras se corrige.
