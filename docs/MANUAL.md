# 📘 Manual de usuario · SysPulse

Guía para usar la plataforma paso a paso: qué significa cada campo, qué datos se ingresan y qué
protege la plataforma. Para la arquitectura y la instalación vuelve al [README](../README.md).

**Demo:** [syspulse.vercel.app](https://syspulse.vercel.app)

## Contenido

1. [En pocas palabras](#1-en-pocas-palabras)
2. [Antes de empezar](#2-antes-de-empezar)
3. [Paso a paso](#3-paso-a-paso)
4. [Qué información ingresas y dónde queda](#4-qué-información-ingresas-y-dónde-queda)
5. [Seguridad: qué se garantiza](#5-seguridad-qué-se-garantiza)
6. [Límites y recomendaciones](#6-límites-y-recomendaciones)
7. [Preguntas frecuentes](#7-preguntas-frecuentes)

---

## 1. En pocas palabras

Un agente instalado en cada computador o celular mide su salud (CPU, memoria, disco, red, batería,
temperatura) cada minuto. Si un valor supera su umbral durante **8 minutos seguidos**, SysPulse abre
un incidente con prioridad y SLA, avisa por Telegram y correo al nivel de soporte que corresponde
(N1, N2 o N3) y coordina al equipo en salas de chat en tiempo real.

## 2. Antes de empezar

- Necesitas un navegador, **una app autenticadora** (Google o Microsoft Authenticator, Authy) y
  una cuenta de **Telegram**.
- La API se duerme sin uso: la primera visita puede tardar hasta un minuto (la web lo avisa).
- Las capturas internas se tomaron con una cuenta de prueba de nivel **N2**; el correo y el
  Telegram aparecen ocultos. La pantalla de **Administración** solo la ve el rol admin y se
  describe en texto.

## 3. Paso a paso

### 3.1 Iniciar sesión

![Pantalla de inicio de sesión](img/manual/01-login.png)

| # | Campo | Qué escribir |
|:-:|---|---|
| 1 | Correo | El correo de tu cuenta |
| 2 | Contraseña | Tu contraseña |
| 3 | Continuar | Valida la contraseña y pide el código de la app autenticadora |
| 4 | ¿Olvidaste tu contraseña? | Recuperación (ver 3.3) |
| 5 | Regístrate | Crea una cuenta nueva |

La primera vez escaneas un QR con tu app autenticadora y **guardas los 10 códigos de
recuperación**; las siguientes, escribes el código de 6 dígitos de la app.

### 3.2 Crear una cuenta

![Formulario de registro con sus campos numerados](img/manual/02-registro.png)

| # | Campo | Qué escribir | Reglas |
|:-:|---|---|---|
| 1 | Nombre completo | Tu nombre | Obligatorio |
| 2 | Correo | Con el que vas a iniciar sesión | Se confirma con un enlace |
| 3 | Tu nivel | **N1** (atiende y aplica runbooks), **N2** (diagnóstico y causa raíz) o **N3** (especialista) | Define qué incidentes te llegan |
| 4 | Telegram | Elige si das tu **@usuario** o tu **número** | — |
| 5 | Usuario de Telegram | Tu @usuario (o país y número, si elegiste Número) | Se confirma con un código del bot |
| 6 | Contraseña | Mejor una frase larga | Mínimo **12 caracteres**; el medidor indica su fuerza |
| 7 | Confirmar contraseña | La misma contraseña | Debe coincidir |
| 8 | Crear cuenta | Botón | Crea la cuenta pendiente de verificación |

Después: confirma tu correo con el enlace, abre el bot de Telegram, pulsa **Iniciar** y escribe el
código que te envía. El rol **gestor** no se elige aquí: se solicita desde tu perfil y lo aprueba el
administrador.

### 3.3 Recuperar la contraseña

![Recuperación de contraseña](img/manual/03-olvide.png)

| # | Campo | Qué escribir |
|:-:|---|---|
| 1 | Correo | El correo de tu cuenta |
| 2 | Botón | Envía el enlace de restablecimiento |

Al abrir el enlace, además de la contraseña nueva se pide el **código de tu app autenticadora**:
quien solo tenga acceso a tu correo no puede cambiarla.

### 3.4 Mi turno

![Mi turno: asignados y disponibles para tomar](img/manual/04-mi-turno.png)

Lo asignado a ti y lo que puedes tomar en tu nivel, ordenado por lo que vence primero. Cada fila
muestra prioridad, código, equipo, nivel, estado y la barra del SLA (en rojo si ya venció). El
gestor ve además los incidentes con SLA en riesgo, vencidos o P1.

### 3.5 Salud de hosts

![Salud de hosts](img/manual/05-hosts.png)

Arriba, cuántos equipos están en línea, saludables, con alertas y en excepción. Cada tarjeta
muestra el estado del agente (**En línea**, **Sin reporte**), la salud (**Saludable**,
**Advertencia**, **Excepción**), la alerta activa y sus indicadores con las marcas de umbral.
Se actualiza cada 30 segundos.

![Detalle de un equipo](img/manual/06-host-detalle.png)

Al abrir un equipo ves sus alertas según el valor actual, las gráficas del historial (6 h, 24 h o
7 días, con las líneas de umbral) y **Todas las lecturas** agrupadas en memoria, almacenamiento,
red, batería y sistema.

### 3.6 Incidentes

![Lista de incidentes](img/manual/07-incidentes.png)

La lista resume los abiertos y los SLA en riesgo o vencidos. **Registrar incidente** abre el
formulario:

![Formulario para registrar un incidente](img/manual/08-registrar-incidente.png)

| # | Campo | Qué escribir |
|:-:|---|---|
| 1 | Título | Resumen de la falla (mínimo 5 caracteres, máximo 140) |
| 2 | Descripción | Opcional: qué pasa y desde cuándo (máximo 4.000 caracteres) |
| 3 | Equipo afectado | Uno de los equipos monitoreados, o **Ninguno en particular** |
| 4 | Impacto | **Alto** (muchos afectados), **Medio** (un equipo o grupo) o **Bajo** (una persona) |
| 5 | Urgencia | Qué tan rápido hay que actuar; por ejemplo **Alta** (detenido ahora) o **Media** (degradado) |
| 6 | Registrar | Crea el incidente; la **prioridad resultante** (P1 a P5) se calcula sola con impacto × urgencia y se avisa al nivel N1 |

![Detalle de un incidente](img/manual/09-incidente-detalle.png)

En el detalle, según tu rol, aparecen **Sala**, **Tomar**, **Iniciar trabajo**, **En espera**
(pausa el SLA), **Escalar**, **Asignar**, **Prioridad**, **Resolver** (con código de cierre) y
**Cerrar**. El **Historial** registra cada cambio, incluidos los avisos automáticos del SLA, y el
cuadro de nota agrega comentarios para el equipo. A la derecha, las barras de **Respuesta** y
**Resolución** muestran el plazo consumido.

### 3.7 Salas

![Salas en tiempo real](img/manual/10-salas.png)

`#operaciones` reúne los avisos generales (incidentes nuevos, escalamientos, resoluciones, cuentas
nuevas) y cada incidente tiene su sala. Escribe en el cuadro inferior (Enter envía, Shift + Enter
hace un salto de línea). Puedes retirar un mensaje propio durante 15 minutos; al cerrarse el
incidente, su sala queda de solo lectura. Abajo a la izquierda ves quién está en línea.

### 3.8 Analítica

![Analítica ITIL](img/manual/11-analitica.png)

Elige el período (7, 30 o 90 días):

| Bloque | Qué mide |
|---|---|
| Niveles de servicio | Cumplimiento del SLA de respuesta y de resolución, MTTA (tiempo hasta que alguien toma el incidente) y MTTR (tiempo hasta resolverlo) |
| Gestión de incidentes | Incidentes del período, tasa de escalamiento, resueltos en primer nivel y reaperturas; gráfica por día y tabla por prioridad |
| Gestión de problemas | Candidatos a problema: la misma alerta del mismo equipo 3 veces o más en 7 días |
| Disponibilidad y capacidad | Porcentaje de ventanas de 5 minutos con datos y uso promedio y máximo de CPU y RAM por equipo |
| Habilitación de cambios | Versiones de los agentes publicadas, registradas como cambios estándar |

### 3.9 Mi cuenta

![Mi cuenta](img/manual/12-perfil.png)

Muestra tu correo, tu Telegram (verificado), el estado de la verificación en dos pasos y tu nivel.
Desde aquí:

- **Cambiar contraseña:** contraseña actual, nueva (mínimo 12 caracteres) y confirmación. Al
  guardarla se cierran tus sesiones en otros dispositivos y recibes un aviso por Telegram.
- **App autenticadora → Configurar en otro teléfono:** la app actual sigue funcionando hasta que
  confirmes la nueva.
- **Rol de gestor → Solicitar rol gestor:** la solicitud la aprueba el administrador.

### 3.10 Administración (solo admin)

Agregar, renombrar o retirar equipos; activar o desactivar cuentas; aprobar, rechazar o revocar el
rol gestor; y ajustar umbrales y persistencia de las alertas.

SLA por prioridad:

| Prioridad | Primera respuesta | Resolución |
|---|---|---|
| P1 | 15 min | 4 h |
| P2 | 30 min | 8 h |
| P3 | 2 h | 24 h |
| P4 | 8 h | 72 h |
| P5 | 24 h | 5 días |

### 3.11 Agregar un equipo

1. El admin entra a **Administración → Equipos → Agregar equipo** y obtiene un **código de un solo
   uso** que vence en 24 horas.
2. **Windows:** descarga el agente desde GitHub Releases y, en PowerShell como administrador,
   ejecuta `.\SysPulse.AgentInstaller.exe install --codigo XXXX-XXXX`.
3. **Android:** instala el APK, abre **SysPulse Agent**, escribe el código y pulsa **Vincular**.
4. En menos de un minuto el equipo aparece en **Salud de hosts** con un nombre genérico.

## 4. Qué información ingresas y dónde queda

**Personas**

| Dato | Para qué se usa | Dónde se guarda | Cómo se protege |
|---|---|---|---|
| Nombre, correo y nivel | Identificarte y asignarte incidentes | MongoDB Atlas | Solo los ve el equipo de operaciones |
| Usuario o número de Telegram | Enviarte los avisos de tu nivel | MongoDB Atlas | Se verifica con un código del bot |
| Contraseña | Iniciar sesión | MongoDB Atlas, **solo su huella PBKDF2** | No se puede leer ni recuperar |
| Secreto de la app autenticadora | Segundo factor | MongoDB Atlas, **cifrado con AES-GCM** | La clave de cifrado vive fuera de la base |
| Códigos de recuperación y enlaces | Recuperar el acceso | Solo su huella SHA-256 | Un solo uso, con límite de intentos |
| Sesión | Mantenerte conectado | `sessionStorage` del navegador | Se borra al cerrar la pestaña; dura 8 horas |

**Equipos monitoreados:** solo métricas técnicas de salud.

| Se captura | No se captura nunca |
|---|---|
| Identificador anónimo (`win-` / `and-` + huella de 10 caracteres) | Nombre del equipo, usuario de Windows o cuenta del celular |
| Tipo de equipo y versión del agente | Marca, modelo, número de serie o IMEI |
| CPU, memoria, paginación y disco (promedio, mínimo, máximo y percentil 95) | Archivos, documentos, fotos, contactos o mensajes |
| Tráfico, errores, latencia y pérdida hacia DNS públicos, señal y número de puertos en escucha | Direcciones IP, ubicación, nombre de la red Wi-Fi o aplicaciones instaladas |
| Almacenamiento libre y, en celulares, batería y temperatura | Historial de navegación o pulsaciones de teclado |

Las métricas se conservan 90 días.

## 5. Seguridad: qué se garantiza

Comprobado en el código del proyecto (`src/SysPulse.Infrastructure/Cuentas`,
`src/SysPulse.Infrastructure/Seguridad`, `src/SysPulse.Api`, `web/src/sesion.tsx`):

- **Contraseñas con PBKDF2** (`PasswordHasher` de ASP.NET Core) y mínimo de 12 caracteres.
- **MFA obligatorio con TOTP** y 10 códigos de recuperación; el secreto del autenticador se guarda
  cifrado con AES-GCM.
- **Bloqueo por intentos:** 5 contraseñas erradas bloquean la cuenta 15 minutos; los códigos de
  verificación admiten 5 intentos y se comparan en tiempo constante.
- **Límite de peticiones** en los endpoints de autenticación (20 por minuto) y de los agentes (60
  por minuto).
- **Cuentas verificadas:** correo y Telegram se confirman antes de activar la cuenta.
- **Sesiones revocables:** el JWT dura 8 horas y deja de servir si cambian la contraseña, el MFA o
  el rol. En el navegador vive en `sessionStorage`, que se borra al cerrar la pestaña.
- **Roles en el servidor:** N1, N2 y N3 se eligen al registrarse; el rol gestor solo lo aprueba el
  admin; todas las rutas de administración exigen rol admin. Nadie borra datos: solo se desactiva
  o retira, y la auditoría es de solo inserción.
- **Privacidad de los equipos:** nombres genéricos y solo métricas técnicas (ver la tabla
  anterior).
- **Agentes sin secretos:** el equipo recibe su configuración al canjear un código de un solo uso
  (solo se guarda su huella) y la guarda cifrada (DPAPI en Windows, Android Keystore). Antes de
  publicar, `tools/escanear-secretos.ps1` revisa que los paquetes no lleven secretos.
- **Actualizaciones verificadas:** los agentes solo se actualizan desde GitHub Releases por HTTPS y
  descartan cualquier archivo cuya huella SHA-256 no coincida.
- **CORS restringido** a la web oficial y secretos solo en variables de entorno de Render.

## 6. Límites y recomendaciones

- Guarda los códigos de recuperación fuera del teléfono: sin la app y sin esos códigos no podrás
  entrar.
- Los avisos de Telegram llegan a tu cuenta personal: no reenvíes capturas de incidentes fuera del
  equipo.
- Instala el agente solo en equipos de los que seas responsable.

## 7. Preguntas frecuentes

**¿El agente puede ver mis archivos o lo que escribo?** No. Solo envía las métricas técnicas de la
tabla de la sección 4.

**¿Por qué mi alerta no abrió un incidente?** El valor debe superar el umbral durante 8 minutos
seguidos; los picos breves se ignoran a propósito.

**¿Cómo llego a ser gestor?** Desde tu nombre, arriba a la derecha → solicitar el rol de gestor.
El admin lo aprueba o rechaza.

**¿Qué significa la pantalla "Estamos en mantenimiento"?** El servicio o su base de datos no están respondiendo en ese momento. No se perdió nada: tus equipos, incidentes y mensajes siguen guardados, y ninguna acción quedó a medias (si no viste la confirmación, no se guardó). La pantalla se actualiza sola y vuelve a la normalidad apenas el servicio responda; también puedes reintentar más tarde.
