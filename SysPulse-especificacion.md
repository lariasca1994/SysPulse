# SysPulse — Especificación del proyecto

> Documento de diseño para construir SysPulse con Claude Code. Recoge todas las decisiones tomadas: alcance, arquitectura, stack, ITIL v4, chat, analítica, modelo de datos y reglas para no salirse de las capas gratuitas.

---

## 1. Qué es

SysPulse es una plataforma de **operaciones de TI** que une en un solo lugar lo que en la vida real está repartido en cuatro herramientas:

| Herramienta típica | Qué cubre | En SysPulse |
|---|---|---|
| Grafana / Zabbix / Kibana | Métricas y alertas | Monitoreo del host desde adentro, con lectura en lenguaje natural |
| ServiceNow / Jira SM | Tickets ITIL | Eventos, solicitudes, problemas, errores conocidos y cambios |
| Slack / Teams | Coordinación del equipo | Salas en tiempo real por registro, con confirmación de lectura |
| Confluence | Procedimientos | Runbooks y base de errores conocidos ligados a cada alerta |

**No compite con Grafana ni Zabbix en volumen.** Su diferencia es el **flujo operativo guiado**: cada pantalla responde qué pasa, qué tan grave es, quién lo tiene, cuánto tiempo queda y qué hacer. Expone `/metrics` en formato Prometheus para quien quiera conectarlo a Grafana.

**Relación con otros proyectos del portafolio:**

- **Portfolio Status**: monitoreo *externo* (HTTP de las apps). SysPulse: monitoreo *interno* del servidor. No se duplican.
- **GestorIncidentesTI**: proyecto aparte. SysPulse **no se integra** con él (ni con ningún otro): es independiente y maneja sus propios incidentes, SLA y escalamiento.
- **QALabSPBVI**: cubre el eje QA. SysPulse se enfoca en **desarrollo + soporte/operaciones**.

---

## 2. Stack

| Capa | Tecnología | Plataforma (capa gratuita) |
|---|---|---|
| Agente de monitoreo — Windows | C# · .NET 10 Worker Service como **servicio de Windows** (`UseWindowsService()`) | **Equipos propios con Windows** (PC, portátil o tablet; tantos como se agreguen), sin costo de nube |
| Agente de monitoreo — Android | C# · .NET 10 · **.NET MAUI (solo Android)** con servicio en primer plano | **Celulares Android propios** (tantos como se agreguen), encendidos 24/7; APK instalado desde GitHub Releases (sin Google Play: tiene costo) |
| Backend | C# · .NET 10 · ASP.NET Core Web API · SignalR | Render (web service gratuito, vía Dockerfile) |
| Analítica | ML.NET (Microsoft.ML.TimeSeries) + aggregation pipeline de MongoDB | Agente (local) y API |
| Base de datos | MongoDB Atlas M0 (proyecto nuevo `syspulse`, 512 MB) | Atlas |
| Buffer local | SQLite (`Microsoft.Data.Sqlite`) | Disco del equipo (`%ProgramData%\SysPulse`) |
| Frontend | **React 19 + Vite 8 + TypeScript 6** + lucide-react (íconos) + `@microsoft/signalr` + ECharts (`echarts-for-react`, carga bajo demanda) | Vercel |
| App multiplataforma | **PWA** instalable en Android, iOS, Windows, macOS y Linux (`manifest.webmanifest`, `sw.js`, `offline.html`, íconos) | Vercel (sin tiendas de apps: tienen costo) |
| Adjuntos del chat | Cloudinary | Cuenta existente |
| Correo | Brevo **API HTTP** (no SMTP: Render gratuito bloquea los puertos 25/465/587) | Cuenta existente, 300 correos/día |
| Telegram | Bot API (bot creado con @BotFather) | Gratuito |
| CI/CD | GitHub Actions | GitHub |

> **Regla de proyecto: lenguajes.** Todo el backend se escribe en **C# con .NET 10**: agente, API, dominio, infraestructura, tareas programadas y scripts de utilidad. El **frontend es React + TypeScript** y se distribuye como PWA para todas las plataformas. Solo lo que no se pueda hacer en C# se escribe en **Python**; con el alcance actual no hay ninguna pieza en Python. Los archivos de configuración (Dockerfile, workflows de GitHub Actions, YAML de semilla) no son código de la aplicación.
>
> **Regla de proyecto: todo gratuito.** Cualquier servicio, librería o funcionalidad que no tenga capa gratuita permanente **se omite** (no se usa crédito de prueba ni planes de pago). Quedan fuera explícitamente: **WhatsApp en cualquier forma** (decisión del proyecto: la API de Meta cobra por mensaje y las alternativas gratuitas no son oficiales), Twilio, VMs en la nube (GCP: la única e2-micro gratuita es exclusiva de n8n; AWS y Azure solo tienen créditos o 12 meses; OCI descartado) e IP estática reservada.

**Librerías .NET:** MongoDB.Driver, Microsoft.AspNetCore.SignalR, Microsoft.AspNetCore.Authentication.JwtBearer, Stateless (máquinas de estado), FluentValidation, YamlDotNet, Polly, Serilog, Microsoft.Extensions.Hosting.WindowsServices, System.Diagnostics.PerformanceCounter, System.Management (WMI), System.ServiceProcess.ServiceController, System.Diagnostics.EventLog, Microsoft.ML, Microsoft.ML.TimeSeries, QuestPDF (reporte PDF), Otp.NET (MFA TOTP), libphonenumber-csharp (validación de teléfonos), xUnit.

---

## 3. Arquitectura

```
[Equipo propio — Windows 11]  ← host monitoreado
  SysPulse.Agent (.NET 10 Worker Service, servicio de Windows, inicio automático)
    ├─ cada 60 s   → lectura cruda → SQLite (se conserva 48 h)
    ├─ cada 5 min  → chequeos de red (TCP, ping, DNS) + agregado min/avg/max/p95 → cola SQLite
    ├─ cada 30 min → lote (insertMany) → MongoDB Atlas (DIRECTO, sin pasar por Render)
    ├─ cada 60 s   → sobrescribe 1 documento estado_actual → MongoDB Atlas
    ├─ 1 vez al día → analítica local con ML.NET (pronóstico + anomalías) → resumen_diario en Atlas
    │                 (si el equipo estuvo apagado, se ejecuta al arrancar la que quedó pendiente)
    ├─ cada 5 min  → lee incidentes abiertos en Atlas; solo si uno cruza 75 % o 100 % del SLA → POST /api/sla/umbral
    ├─ evento de excepción → POST /api/eventos → Render (lo despierta si duerme)
    └─ si Render no responde en 90 s → el evento queda en SQLite y se reintenta (el equipo no avisa por su cuenta)

  Además es el NODO DE ANALÍTICA: la analítica diaria con ML.NET corre aquí para todos los hosts
  (ML.NET no tiene soporte para Android).

[Celular Android — 24/7]  ← hosts Android monitoreados (sondas móviles; uno o varios)
  SysPulse.Agent.Android (.NET MAUI, servicio en primer plano, arranca con el teléfono)
    ├─ mismo núcleo que el agente de Windows (SysPulse.Agent.Core): alertas, SQLite, lotes, eventos
    ├─ cada 60 s   → batería, temperatura, RAM, almacenamiento, red → SQLite
    ├─ cada 5 min  → chequeos TCP / ping / DNS desde la red del celular (Wi-Fi o datos móviles)
    ├─ cada 30 min → lote → MongoDB Atlas (DIRECTO)
    └─ evento de excepción → POST /api/eventos → Render (se reintenta hasta 24 h)

  Host apagado o sin internet: el agente sigue guardando en SQLite (si está encendido) y envía todo
  al reconectar. En la plataforma, si estado_actual.ts de un host tiene más de 5 min, se muestra
  "Host sin datos desde hh:mm" (calculado al consultar, sin procesos en segundo plano).

[Render — SysPulse.Api (ASP.NET Core en Docker)]
    ├─ REST: eventos, incidentes (+ SLA), solicitudes, problemas, errores conocidos, cambios, plantillas, analítica, reportes
    ├─ Notificaciones: Channel<T> en memoria → Brevo API · Telegram
    ├─ SignalR ChatHub: #operaciones, salas, presencia, "escribiendo…", vistos, comandos
    └─ /metrics (formato Prometheus)

[Vercel — frontend React + PWA instalable]  → HTTPS / WSS a Render
[MongoDB Atlas M0 — proyecto "syspulse"]
```

**Regla de oro:** el agente **nunca** mantiene una conexión abierta con Render ni le envía métricas. Render solo se despierta cuando una persona usa la plataforma o llega un evento de excepción.

---

## 4. Estructura de la solución

```
SysPulse/
├─ SysPulse.slnx                Formato de solución XML de .NET 10
├─ src/
│  ├─ SysPulse.Agent.Core/       Biblioteca compartida por ambos agentes (net10.0)
│  │   ├─ Collectors/            ICollector (independiente del sistema operativo)
│  │   ├─ Checks/                TcpCheck, PingCheck, DnsCheck
│  │   ├─ Alertas/               Umbrales con histéresis, deduplicación, clasificación de eventos
│  │   ├─ Buffer/                SQLite (store-and-forward)
│  │   └─ Envio/                 Lotes a Atlas y eventos a la API
│  ├─ SysPulse.Agent/            Servicio de Windows (cada host Windows)
│  │   ├─ Collectors/Windows/    CpuCollector, MemoriaCollector, PaginacionCollector, DiscoCollector,
│  │   │                         RedCollector, ProcesosCollector, ServiciosCollector, EventLogCollector
│  │   └─ Analitica/             Pronóstico (SSA), anomalías (ML.NET) y resumen diario de todos los hosts
│  ├─ SysPulse.Agent.Android/    .NET MAUI net10.0-android (cada host Android)
│  │   ├─ Collectors/Android/    BateriaCollector, TermicoCollector, MemoriaCollector,
│  │   │                         AlmacenamientoCollector, RedMovilCollector, TraficoCollector
│  │   ├─ Platforms/Android/     ForegroundService, BootReceiver, permisos
│  │   └─ Pages/                 Pantalla mínima: estado, último envío, configuración inicial
│  ├─ SysPulse.Api/
│  │   ├─ Endpoints/             eventos, solicitudes, problemas, errores-conocidos, cambios,
│  │   │                         plantillas, salas, analitica, reportes, metrics
│  │   ├─ Hubs/                  ChatHub : Hub<IChatCliente>
│  │   └─ Auth/                  JWT + policies por rol
│  ├─ SysPulse.Domain/           Entidades ITIL, máquinas de estado (Stateless), matriz de prioridad,
│  │                             reglas de escalamiento, cálculo de tiempos e indicadores
│  ├─ SysPulse.Infrastructure/   Repositorios Mongo, Cloudinary,
│  │                             Notificaciones/ (INotificador: BrevoNotificador, TelegramNotificador),
│  │                             generación de PDF (QuestPDF)
│  └─ SysPulse.Contracts/        DTOs compartidos agentes ↔ API
├─ tests/
│  └─ SysPulse.Domain.Tests/     xUnit sobre reglas de negocio
├─ web/                          React 19 + Vite 8 + TypeScript 6 (oxlint)
│  ├─ public/                    manifest.webmanifest, sw.js, offline.html, icons/
│  ├─ src/                       páginas, chat SignalR, gráficas, registro/MFA, pwa.ts
│  ├─ e2e/                       Playwright (incluye pruebas de la PWA)
│  └─ vercel.json                rewrites /api → Render, cabeceras de seguridad y del service worker
├─ seed/
│  ├─ plantillas.yaml            Plantillas ITIL precargadas
│  ├─ catalogo.yaml              Catálogo de solicitudes de servicio
│  └─ runbooks/                  Un .md por tipo de alerta
├─ deploy/
│  ├─ Dockerfile.api             Multi-stage .NET 10
│  └─ agent.appsettings.example.json
├─ tools/
│  └─ SysPulse.AgentInstaller/   Consola C#: instala/actualiza/desinstala el servicio de Windows
└─ .github/workflows/            build, test, deploy API, deploy agente, registro automático de cambio
```

---

## 5. Agente: qué mide y de dónde sale

El agente corre en **Windows 11** y lee contadores de rendimiento (PDH), WMI y APIs de .NET. Todo detrás de `ICollector`, para poder agregar colectores de Linux más adelante sin cambiar alertas, buffer ni envío.

### Red

| Métrica | Fuente en C# | Frecuencia |
|---|---|---|
| Latencia TCP a destinos de infraestructura | `TcpClient.ConnectAsync` + `Stopwatch` | 5 min |
| Ping: RTT, jitter, pérdida (5 paquetes) | `System.Net.NetworkInformation.Ping` (no requiere permisos de administrador en Windows) | 5 min |
| Tiempo de resolución DNS | `Dns.GetHostAddressesAsync` + `Stopwatch` | 5 min |
| Tráfico entrada/salida (MB/s) | `NetworkInterface.GetIPv4Statistics()` (diferencia entre lecturas) | 60 s |
| Errores y descartes | `IPv4InterfaceStatistics` (IncomingPacketsWithErrors, IncomingPacketsDiscarded, OutgoingPacketsDiscarded) | 60 s |
| Conexiones por estado | `IPGlobalProperties.GetActiveTcpConnections()` | 5 min |
| Wi-Fi / cable activo y velocidad del enlace | `NetworkInterface` (tipo, `Speed`, `OperationalStatus`) | 5 min |

Destinos: DNS públicos, gateway del router, endpoint de MongoDB Atlas, URL de la API en Render (solo TCP, sin despertarla). **No** las apps del portafolio (eso es de Portfolio Status). Solo TCP handshake, **nunca login** contra bases de datos.

### Hardware

| Métrica | Fuente |
|---|---|
| CPU % total y por núcleo | `PerformanceCounter("Processor", "% Processor Time", "_Total" / núcleo)` |
| Cola del procesador (equivalente a load average) | `PerformanceCounter("System", "Processor Queue Length")` |
| RAM usada, disponible, % | `GlobalMemoryStatusEx` (P/Invoke) |
| Disco % por unidad | `DriveInfo` |
| E/S de disco (MB/s, IOPS, % tiempo activo, cola) | `PerformanceCounter("PhysicalDisk", …)` |
| Uptime | `Environment.TickCount64` |
| Batería (si es portátil): %, cargando, estado | WMI `Win32_Battery` |
| Temperatura (si el hardware la expone) | WMI `MSAcpi_ThermalZoneTemperature`; si no está disponible, se marca "no soportado" |

### Software

| Métrica | Fuente |
|---|---|
| Archivo de paginación % (equivalente a swap) | WMI `Win32_PageFileUsage` |
| Páginas/s (presión de memoria) | `PerformanceCounter("Memory", "Pages/sec")` |
| Top 5 procesos por CPU y RAM | `Process.GetProcesses()` (foto, no histórico) |
| Total de procesos, hilos y handles | `PerformanceCounter("System", "Processes" / "Threads")`, `("Process", "Handle Count", "_Total")` |
| Estado de servicios configurados | `ServiceController` |
| Puertos en escucha | `IPGlobalProperties.GetActiveTcpListeners()` |
| Errores del sistema (últimos 5 min) | `EventLog` "System" y "Application", nivel Error/Crítico (conteo + fuentes principales) |
| Reinicios inesperados | Evento 41 (Kernel-Power) y 6008 del registro System |
| Windows Update pendiente de reinicio | Registro `RebootRequired` |

### Hosts Android: celulares (sondas móviles)

Android no permite a una app leer el CPU total del sistema, otros procesos, servicios ni registros (bloqueado desde Android 8). Por eso el celular mide lo que sí expone y funciona sobre todo como **sonda de red** desde otra conexión.

| Métrica | Fuente (API de Android vía .NET MAUI) | Frecuencia |
|---|---|---|
| Batería: %, cargando, salud, voltaje | `BatteryManager` / intent `ACTION_BATTERY_CHANGED` | 60 s |
| Temperatura de la batería | `ACTION_BATTERY_CHANGED` (`EXTRA_TEMPERATURE`) | 60 s |
| Estado térmico del sistema | `PowerManager.CurrentThermalStatus` (Android 10+) | 60 s |
| RAM disponible y "memoria baja" | `ActivityManager.MemoryInfo` | 60 s |
| Almacenamiento % | `StatFs` | 5 min |
| Tipo de red (Wi-Fi / datos móviles), ancho de banda estimado | `ConnectivityManager` + `NetworkCapabilities` | 60 s |
| Señal Wi-Fi (RSSI) y señal móvil (nivel) | `WifiManager` / `TelephonyManager.SignalStrength` | 5 min |
| Tráfico entrada/salida | `TrafficStats` (diferencia entre lecturas) | 60 s |
| Latencia TCP, ping, DNS | Mismos chequeos de `SysPulse.Agent.Core` | 5 min |
| Uptime del teléfono | `SystemClock.ElapsedRealtime` | 60 s |

**Funcionamiento 24/7 en Android:**

- **Servicio en primer plano** de tipo `specialUse` (los tipos `dataSync` tienen límite de horas en Android 15+), con notificación fija "SysPulse monitoreando".
- **Arranque automático** al encender el teléfono (`BOOT_COMPLETED`).
- La app pide **excluirse de la optimización de batería** para que el modo reposo (Doze) no corte la red.
- Secretos guardados con `SecureStorage` (Android Keystore).
- **Recomendación por estar siempre conectado al cargador:** activar en el teléfono la protección de batería (límite de carga al 80–85 %) si el fabricante la ofrece. La temperatura de la batería tiene alerta propia.

**Umbrales propios del celular:** temperatura de batería ≥ 40 °C → advertencia, ≥ 45 °C → excepción; estado térmico "grave" o superior → excepción; batería < 20 % sin cargar → advertencia (indica que se desconectó del cargador); sin red > 10 min → evento al reconectar.

**Prueba técnica en el teléfono de referencia (resultados):**

| Prueba | Resultado |
|---|---|
| DNS / TCP 27017 a Atlas | OK (137 ms / 185 ms) |
| Ping ICMP (`System.Net.NetworkInformation.Ping`) sin privilegios | OK (11 ms, 0 % pérdida): **se mantiene la métrica de ping** |
| `MongoDB.Driver` 3.12 con `mongodb://host1,host2,host3/...` | OK (ping en 191 ms) |
| `MongoDB.Driver` 3.12 con `mongodb+srv://` | **Falla** en Android ("List of configured name servers must not be empty") |

- **Decisión:** el agente Android usa **siempre la cadena estándar `mongodb://`** (hosts, `replicaSet` y `authSource` resueltos una vez desde el registro SRV/TXT del clúster). Si Atlas cambia los hosts del clúster, se actualiza la cadena en la app.
- Las compilaciones Debug que se instalan con `adb install` deben llevar `-p:EmbedAssembliesIntoApk=true` (sin eso, Fast Deployment deja el APK sin ensamblados y la app se cierra al abrir). Los Release siempre los incluyen.

**Pendiente de validar:**

- SQLite nativo compatible con páginas de 16 KB (Android 15+): usar `SQLitePCLRaw` 2.1.10 o superior.

**Versión de referencia:** **Android 16 (API 36)**; mínimo Android 10.

- `TargetSdk` 36 y `MinSdk` 29 (Android 10: estado térmico del sistema). Compila con el workload `android` de .NET 10 (36.x).
- El servicio de tipo `specialUse` sí puede iniciarse desde `BOOT_COMPLETED` en Android 15/16 (la restricción aplica a `dataSync`, `camera`, `mediaPlayback`, `phoneCall`, `mediaProjection` y `microphone`).
- **Algunas capas de fabricante cierran apps en segundo plano de forma agresiva.** Además de la exclusión de batería estándar, hay que configurar a mano (la app abre cada pantalla y muestra una guía):
  1. Ajustes → Apps → SysPulse → **Inicio automático: activado**.
  2. Ajustes → Apps → SysPulse → Ahorro de batería → **Sin restricciones**.
  3. En la vista de apps recientes, **bloquear (candado)** la tarjeta de SysPulse.
  4. Notificaciones de SysPulse permitidas (sin ellas Android puede detener el servicio en primer plano).
- La app reporta como evento **"Agente detenido por el sistema"** cuando detecta un hueco de lecturas al reanudar, para saber si el sistema lo está cerrando.
- Instalación de desarrollo por USB: en algunos fabricantes, además de "Depuración USB", hay que activar **"Instalar vía USB"** en Opciones de desarrollador.

### Alertas

- Umbrales configurables por tipo de host (Windows / Android) en `appsettings.json`, recargables sin reiniciar.
- **Histéresis**: se dispara si se supera el umbral en N lecturas seguidas y se recupera bajo un umbral inferior (evita alertas intermitentes).
- **Persistencia mínima (8 min por defecto)**: una alerta solo se abre, escala o se da por solucionada si la condición se mantiene ese tiempo, para evitar la lluvia de avisos y soluciones. El admin la cambia desde la web (Administración → Alertas) y los agentes la aplican sin reinstalar.
- **Deduplicación** por clave (`host + tipo + métrica`): un evento abierto no genera otro igual.
- Clasificación ITIL del evento: **informativo**, **advertencia** o **excepción**. Solo la excepción se envía a la API y puede abrir incidente.

---

## 6. ITIL v4 en SysPulse

### Prácticas ITIL v4 cubiertas (todas dentro de SysPulse)

| Práctica ITIL v4 | En SysPulse |
|---|---|
| Gestión de monitoreo y eventos | Clasificación, deduplicación, correlación con cambios |
| Gestión de incidentes | Registro, sala, escalamiento N1 → N2 → N3, SLA (sección 11) |
| Gestión de solicitudes de servicio | Catálogo, aprobación, tiempos de cumplimiento |
| Gestión de problemas | Reactivo y proactivo, RCA (5 porqués + Ishikawa), vínculo a incidentes |
| Gestión del conocimiento | Errores conocidos (KEDB), runbooks |
| Habilitación de cambios | RFC estándar / normal / emergencia, despliegues automáticos |
| Mesa de servicio | Salas, entrega de turno, comunicados de incidente mayor |
| Gestión de niveles de servicio | SLA por incidente, SLO, presupuesto de error, indicadores |

### Tipos de registro

| Tipo | Definición | Ejemplo |
|---|---|---|
| Evento | Algo que detectó el monitoreo | CPU > 90 % |
| Incidente | Interrupción o degradación no planeada | Servicio caído |
| Solicitud (catálogo) | Algo estándar y preaprobado | Reiniciar servicio, dar acceso, ampliar disco |
| Requerimiento | Necesita desarrollo o configuración nueva; genera un cambio normal | Monitorear un puerto nuevo |
| Problema | Causa raíz de uno o más incidentes | El disco se llena cada semana |
| Error conocido | Causa identificada + workaround documentado | EC-004: carpeta temporal sin limpieza llena el disco |
| Cambio | Modificación controlada | Despliegue v1.3 |

### Prioridad = impacto × urgencia

| Impacto \ Urgencia | Alta | Media | Baja |
|---|---|---|---|
| Alto | P1 | P2 | P3 |
| Medio | P2 | P3 | P4 |
| Bajo | P3 | P4 | P5 |

SysPulse sugiere la prioridad (servicio caído → P1; disco que se llena en < 6 h → P2; en > 24 h → P3). Cambiarla exige justificación y queda auditado.

### Escalamientos

- **Funcional (N1 → N2 → N3)**: por conocimiento. Manual con `/escalar` o sugerido si no hay avance en un tiempo configurado.
- **Jerárquico (al `gestor`)**: por riesgo. SLA al 75 % → aviso; al 100 % → escalamiento automático; P1 → aviso inmediato.
- Los tiempos se **calculan al consultar** con las marcas de tiempo de cada estado. **No hay `BackgroundService` consultando ninguna base.**

### Máquinas de estado (Stateless)

- **Incidente:** Nuevo → Asignado → En progreso ⇄ En espera → Resuelto → Cerrado (Resuelto → Reabierto → En progreso). Ver sección 11.
- **Solicitud:** Nueva → Pendiente de aprobación → Aprobada / Rechazada → En ejecución → Cumplida → Cerrada.
- **Problema:** Nuevo → En análisis → Error conocido → En cambio → Resuelto → Cerrado.
- **Cambio:** Borrador → Evaluación → Aprobado / Rechazado → Programado → En implementación → Implementado / Revertido → Revisado → Cerrado. El cambio estándar salta la aprobación; el de emergencia se aprueba después.
- **Evento:** Abierto → Reconocido → Asociado a incidente / Descartado → Cerrado.

Una transición inválida la rechaza el dominio, nunca un `if` suelto en el endpoint.

### Incidente mayor (P1)

Lo declara el `gestor`, que asigna al coordinador del incidente (normalmente un N3). Sala especial, comunicado obligatorio cada 30 minutos (recordatorio en la sala) y revisión post-incidente obligatoria antes de cerrar.

### Problemas

- **Reactivo:** 3 eventos o incidentes del mismo tipo en 7 días → se propone abrir un problema.
- **Proactivo:** la analítica detecta una tendencia (ej.: el disco se llena en 6 semanas) → se propone abrir un problema.
- **RCA:** 5 porqués + Ishikawa con las 6M adaptadas a TI (método, máquina, materiales, mano de obra, medición, medio ambiente).
- Al documentar la causa y el workaround se crea el **error conocido**; las siguientes alertas iguales muestran "Error conocido EC-XXX: aplicar workaround".

### Cambios

- **Estándar:** preaprobados (rotar logs, reiniciar servicio). Los despliegues de GitHub Actions se registran solos como cambio estándar con versión, autor y commit.
- **Normal:** requieren aprobación del `gestor`, evaluación de riesgo y plan de vuelta atrás. Quien crea el cambio no puede aprobarlo.
- **Emergencia:** para resolver un P1; lo ejecuta un N3 y el `gestor` da la aprobación posterior.
- Cada cambio aparece como marcador sobre las gráficas y en `#operaciones`.

---

## 7. Chat en tiempo real (SignalR)

### Canales

- `#operaciones`: general, con presencia (quién está en línea).
- Una sala por evento escalado, solicitud, problema o cambio.
- Sala especial por incidente mayor (P1).
- `#entrega-de-turno`.

### Interfaz (nivel producto)

- Tres paneles: **contexto** (gráfica, cambios relacionados, top procesos, runbook, posible error conocido) | **conversación** | **ficha ITIL** (tipo, prioridad, estado, asignado, SLA, cambio y problema vinculados, botones de plantilla).
- Mensajes agrupados por autor (< 5 min), separadores "Hoy" / "Ayer" / fecha relativa.
- Avatar con iniciales e **insignia de rol con color** (N1, N2, N3, gestor, admin).
- Mensajes del sistema como píldora gris centrada, **inmutables** (pista de auditoría).
- Plantillas como **tarjetas con campos**, no texto pegado.
- Respuesta citada, menciones con notificación, contador de no leídos por sala.
- **Envío optimista**: el mensaje aparece con 🕓 y cambia a ✓ al confirmarse.
- Nota interna (solo equipo) vs. comentario visible.
- Editar dentro de 15 minutos (marca "editado"); borrar deja "mensaje eliminado por @usuario".
- Notificaciones del navegador para menciones y P1 mientras la app está abierta y la pestaña no está visible (sin Web Push: con la app cerrada, los avisos llegan por Telegram y correo).
- Tema claro/oscuro, diseño adaptable a móvil, cargadores tipo skeleton.

### Experiencia visual y reglas de CSS

Identidad **propia** de SysPulse (independiente de otros proyectos del portafolio). Paleta **inspirada** en la mezcla de dos operadores de telecomunicaciones: el **azul marino y el cian** de Tigo (Millicom) como base y el **rojo** de Claro para lo crítico. Son tonos de inspiración, no colores de marca oficiales; no se usan logos, nombres ni elementos de esas marcas.

**Uso del color (reparto):**

- **Azul marino** (estructura): barra lateral, encabezados, botón primario en tema claro.
- **Cian** (acción y datos): enlaces, foco, selección, serie principal de las gráficas, botón primario en tema oscuro.
- **Rojo** (crítico): solo excepción, P1, SLA vencido y el punto de "pulso" del logo. Nunca decorativo.
- **Amarillo** (advertencia): advertencias, SLA al 75 %, rol gestor.
- **Neutros fríos** para todo lo demás: el 85–90 % de la pantalla es neutro; el color se reserva para estado y acción.

**Tokens — tema claro (`:root`):**

| Token | Valor | Uso |
|---|---|---|
| `--bg` | `#F4F6FA` | Fondo de la página |
| `--surface` | `#FFFFFF` | Tarjetas, paneles |
| `--surface-2` | `#EEF2F8` | Encabezados de tabla, hover |
| `--border` / `--border-strong` | `#DDE3EC` / `#C5CEDB` | Bordes |
| `--text` | `#0B1F3A` | Texto principal |
| `--text-muted` | `#4F5D73` | Texto secundario |
| `--text-subtle` | `#5F6B80` | Marcas de tiempo y metadatos |
| `--nav` | `#0B2A5B` | Barra lateral |
| `--primary` / `--primary-hover` | `#0B3A7E` / `#082C61` | Botón primario (texto blanco) |
| `--primary-soft` | `#E6EEF9` | Fondo de selección |
| `--accent` | `#00A9E0` | Rellenos, gráficas, indicadores |
| `--link` | `#006AA3` | Enlaces y foco |
| `--pulse` | `#E1251B` | Punto del logo |

**Tokens — tema oscuro (`:root[data-theme="dark"]`):**

| Token | Valor |
|---|---|
| `--bg` | `#07111F` |
| `--surface` / `--surface-2` | `#0E1B2E` / `#14243B` |
| `--border` / `--border-strong` | `#22344F` / `#31466A` |
| `--text` / `--text-muted` / `--text-subtle` | `#E6EDF7` / `#A3B1C6` / `#8191AA` |
| `--nav` | `#050C17` |
| `--primary` / `--primary-hover` | `#00C8FF` / `#5AD8FF` (texto `#04182F`) |
| `--primary-soft` | `#0C2F47` |
| `--accent` | `#00C8FF` |
| `--link` | `#4FD1FF` |
| `--pulse` | `#FF4D3D` |

**Estados (severidad ITIL) — texto / fondo suave:**

| Estado | Claro | Oscuro |
|---|---|---|
| Crítico (excepción, P1) | `#C8231A` / `#FDECEA` | `#FF5A4E` / `#3A1517` |
| Advertencia | `#8A6100` / `#FFF6D6` (relleno `#FFBE00`) | `#FFC933` / `#3A2E0A` |
| OK / resuelto | `#1A7335` / `#E7F5EC` | `#4CC38A` / `#0F2E22` |
| Informativo | `#006AA3` / `#E3F3FB` | `#4FB8F0` / `#0C2A40` |

**Prioridades:** P1 `#C8231A` · P2 `#C2410C` · P3 `#8A6100` · P4 `#006AA3` · P5 `#5F6B80` (en oscuro, las variantes claras equivalentes).

**Insignias de rol:** N1 azul `#006AA3` · N2 marino `#0B3A7E` · N3 turquesa `#00727C` · gestor amarillo `#8A6100` sobre `#FFF6D6` · admin grafito `#2F3B52`.

**Gráficas (ECharts):** serie principal cian, secundarias marino, turquesa y gris; el **rojo y el amarillo se reservan para líneas de umbral** y zonas de alerta. El tema de ECharts se genera leyendo los tokens CSS en tiempo de ejecución y se vuelve a pintar al cambiar de tema.

**Tipografía:** IBM Plex Sans (interfaz) e IBM Plex Mono (métricas, IDs, logs), licencia OFL, **empaquetadas con `@fontsource`** (sin fuentes remotas, funcionan sin conexión en la PWA). Números con `font-variant-numeric: tabular-nums`.

**Reglas de CSS:**

1. **Solo tokens:** ningún color hexadecimal fuera de `web/src/styles/tokens.css`. Los componentes usan `var(--…)`.
2. **CSS Modules** por componente (soporte nativo de Vite) + un `global.css` mínimo. Sin frameworks CSS pesados.
3. **Tema:** por defecto sigue `prefers-color-scheme`; el usuario puede fijar claro/oscuro (se guarda en `localStorage` y se aplica con `data-theme` en `<html>` antes de pintar, sin parpadeo). `color-scheme` y `<meta name="theme-color">` se actualizan con el tema.
4. **Accesibilidad WCAG 2.2 AA:** contraste ≥ 4.5:1 en texto y ≥ 3:1 en controles; todos los tokens de texto cumplen ≥ 4.5:1 sobre `--bg`, `--surface` y `--surface-2` (verificado). **El color nunca es la única señal:** cada estado lleva ícono y texto (p. ej. ⬤ + "Crítico").
5. **Foco visible** en todo control (`:focus-visible`, anillo de 2 px con `--link`).
6. **Densidad productiva:** texto base 14 px; escala 12/13/14/16/20/24/30; espaciado en múltiplos de 4 px; radios 6/8/12 px; tablas compactas con filas de 36 px.
7. **Responsive:** puntos de corte 640 / 1024 / 1440 px. La sala de chat de tres paneles pasa a pestañas (Contexto · Conversación · Ficha) en móvil. Ancho mínimo soportado 320 px.
8. **Movimiento:** transiciones ≤ 150 ms; todo se desactiva con `prefers-reduced-motion`.
9. **Carga:** skeletons en lugar de spinners; pantalla "Iniciando servicio…" mientras Render despierta.
10. **Correos:** HTML con CSS en línea, una columna, sin JavaScript, fuentes ni imágenes remotas, con versión en texto plano; mismos colores de estado.

### App para todas las plataformas (PWA)


- **Instalable** en Android, iOS, Windows, macOS y Linux. En Chrome/Edge aparece el botón "Instalar"; en iOS se guía al usuario con Compartir → Agregar a inicio (`pwa.ts` detecta cada caso).
- El service worker **solo guarda la cáscara** (index, `/assets`, íconos) y **nunca `/api`**; navegación primero por red, con `offline.html` si no hay conexión.
- **Nada mantiene vivas las plataformas:** sin background sync, sin push y sin consultas periódicas con la app oculta. La conexión SignalR se pausa con la app en segundo plano y se cierra tras 15 min de inactividad.
- **Sin tiendas de aplicaciones** (Google Play cobra el registro y Apple cobra una cuota anual): la instalación es siempre desde el navegador.

### Indicadores de lectura

| Marca | Significado |
|---|---|
| 🕓 | Enviando |
| ✓ | Guardado en el servidor |
| ✓✓ gris | Entregado a todos los participantes |
| ✓✓ azul | Leído por todos |
| "Visto por 3 de 5" | Al tocarlo: quién lo vio y cuándo, y quién no |

**Implementación con puntero de lectura** (no un registro por mensaje y usuario):

```
salas_lectura: { sala_id, usuario_id, ultimo_entregado_id, ultimo_leido_id, actualizado }
¿Quién vio M?          → usuarios con ultimo_leido_id >= M
¿Entregado a todos?    → min(ultimo_entregado_id) de la sala >= M
```

- El cliente agrupa las marcas de lectura y llama a `MarcarLeido` como máximo cada 2–3 s.
- "Escribiendo…" y "en línea" viajan solo por SignalR, nunca por la base.
- "Última vez en línea" se guarda una sola vez, al desconectarse.
- Presencia en memoria (`ConcurrentDictionary`); con una instancia no hace falta Redis.
- Cada sala es un grupo de SignalR (`Groups.AddToGroupAsync`); JWT por `access_token` en el query string.

### Comandos

Todos los roles leen todas las salas y escriben mensajes. Cada comando exige el rol mínimo indicado en la matriz de permisos de la sección 12.

```
/tomar                        me asigno el registro
/asignar @usuario             asigno a otra persona
/escalar N2 <motivo>          escalamiento funcional (abre plantilla; notifica al nuevo nivel)
/prioridad P1 <justificación> cambio de prioridad auditado
/espera <motivo>              pausa el SLA
/reanudar                     reanuda el SLA
/metricas disco|cpu|ram|red   inserta una foto de la métrica
/runbook                      muestra el procedimiento
/problema                     crea o vincula un problema
/cambio                       crea una RFC
/resolver                     abre plantilla de resolución
/comunicado                   comunicado de incidente mayor
```

### Plantillas precargadas (`seed/plantillas.yaml`)

Registro de incidente · Escalamiento funcional · Escalamiento jerárquico · Comunicado de incidente mayor (inicial, actualización, restablecimiento) · Solicitud de servicio · Requerimiento · Resolución y cierre (con códigos de cierre) · Entrega de turno · Registro de problema y RCA (5 porqués + Ishikawa) · Error conocido · Solicitud de cambio (RFC) · Revisión post-incidente (línea de tiempo generada automáticamente desde los mensajes de sistema).

### Notificaciones externas (correo y Telegram)

Cuando un evento, incidente u otro registro supera los límites establecidos:

1. La API guarda el evento y **crea su sala en el chat** + mensaje de sistema en `#operaciones`.
2. Encola la notificación en un `Channel<T>` en memoria (no bloquea la petición) y la envía por cada canal habilitado.

**Qué se notifica y por dónde:**

| Severidad | Correo (Brevo) | Telegram |
|---|:-:|:-:|
| Excepción (y todo P1) | ✓ | ✓ |
| Advertencia | | ✓ |
| Informativo | | |

**Quién recibe: según el nivel al que está escalado el registro.**

| Momento | Destinatarios |
|---|---|
| Se abre (nivel inicial N1) | Todos los usuarios `N1` + el asignado (si ya hay uno) |
| Se escala a N2 (`/escalar N2`) | Todos los usuarios `N2` + el asignado anterior (para que sepa a quién pasó) |
| Se escala a N3 | Todos los usuarios `N3` + el asignado anterior |
| Escalamiento jerárquico (SLA 75 % / 100 %) | Todos los `gestor` + el asignado actual |
| P1 / incidente mayor | Nivel actual + todos los `gestor` + el `admin` |
| Se resuelve o cierra | Los que recibieron avisos de ese registro (nivel actual y anteriores) |

- Los niveles son los del rol del usuario (`N1`, `N2`, `N3`). El `admin` recibe solo P1 (y lo que se le asigne directamente).
- Cada usuario elige en su perfil qué canales tiene activos, pero **no puede desactivar todos** si tiene rol técnico o gestor (al menos uno queda activo).
- **Telegram por usuario:** cada persona registra y **verifica** su Telegram al crear la cuenta (ver "Registro de cuentas → Verificación de Telegram"). Se guarda su `chat_id`. No se usa `getUpdates` (obligaría a consultar periódicamente); todo llega por webhook.
- **Cuentas de Telegram fijas** (las que estén en la variable; hoy 4): configuradas por variable de entorno (`TELEGRAM_FIXED_CHAT_IDS`), no pasan por el registro ni la verificación y reciben **todos** los avisos (excepciones y advertencias) sin importar el nivel. Solo la API las conoce: los equipos no reciben chat IDs ni el token del bot.
- **Correo:** un mensaje por destinatario (Brevo cuenta cada destinatario contra los 300/día). Con un equipo pequeño y el enfriamiento de 30 min el consumo queda muy por debajo del límite; si se acerca al 80 % diario, las advertencias de correo se agrupan en un resumen.

| Canal | Implementación | Límite gratuito |
|---|---|---|
| Correo | Brevo API v3 (`/v3/smtp/email`), remitente verificado | 300/día |
| Telegram | `sendMessage` de la Bot API al `chat_id` de cada usuario; grupo general solo de respaldo | ~30 msg/s en total, 1 msg/s por chat |

Reglas:

- Se notifica al **abrir, escalar y cerrar**, nunca por cada lectura. P1 siempre notifica.
- **Enfriamiento** por `clave_dedup` (30 min, configurable) registrado en la colección `notificaciones`.
- Un fallo de un canal no bloquea los demás (Polly con reintentos cortos por canal).
- **Reintento:** si `POST /api/eventos` no responde en 90 s (arranque en frío o caída), el agente guarda el evento en SQLite y lo reintenta en los ciclos siguientes (hasta 24 h). El equipo no avisa por su cuenta: así nunca guarda el token del bot.
- Cada canal implementa `INotificador`; se habilita o deshabilita por configuración sin cambiar código.
- **Destinatarios siempre dinámicos:** la API lee los usuarios (rol, nivel, `chat_id` verificado) en el momento de notificar; un usuario nuevo recibe avisos desde su verificación, sin tocar agentes.
- **Configuración central de los agentes** (colección `configuracion`, documento `agentes` en Atlas): cuentas fijas y token de Telegram, URL de la API y clave del agente (`api_url`, `agent_api_key`), y umbrales personalizados (por métrica y, opcionalmente, por tipo de host). Al desplegar en Render basta con cambiar `API_URL` y volver a publicar: los agentes la toman solos. Cada agente la relee cada 5 min, la aplica en caliente (las alertas abiertas se conservan) y guarda una copia local (`config-central.json`) para arrancar sin internet. Localmente solo queda la cadena de conexión a Atlas. Hoy se publica con `SysPulse.AgentInstaller config-central`; luego la editará el admin desde la web.

---

## 8. Analítica

La analítica es lo que lleva el proyecto de "herramienta de soporte" a "plataforma de operaciones". Combina cuatro áreas: **capacidad**, **anomalías**, **operación ITIL** y **desarrollo (DORA)**, más una capa de **calidad estadística**.

### 8.1 Dónde se calcula (sin gastar la capa gratuita)

| Cálculo | Dónde | Por qué |
|---|---|---|
| Pronóstico y anomalías (ML.NET) | **Agente, en el equipo, una vez al día** (o al arrancar si quedó pendiente) con los datos de SQLite y el histórico | CPU local gratis; Render no se despierta |
| Resumen diario (min/avg/max/p95 por métrica, disponibilidad del día) | Agente → colección `resumen_diario` | Los paneles de 30–90 días leen ~90 documentos, no ~26 000 |
| Indicadores ITIL y DORA | API, **al consultar**, con aggregation pipeline (`$group`, `$setWindowFields`, `$percentile`) | Sin jobs periódicos |
| Caché de resultados | API, 5 minutos en memoria | Evita recalcular al refrescar |

### 8.2 Capacidad y pronóstico

- **Pronóstico de llenado de disco** con SSA (Singular Spectrum Analysis, `ForecastBySsa` de ML.NET): "Al ritmo actual, el disco llega al 90 % en 18 días (intervalo de confianza 14–23)".
- Tendencia de RAM y swap: crecimiento semanal, posible fuga de memoria (crecimiento sostenido sin bajar tras reinicios).
- Utilización por hora del día y día de la semana (heatmap) para planear ventanas de cambio.
- Si el pronóstico cruza el umbral en menos de N días → se propone un **problema proactivo**.

### 8.3 Detección de anomalías

- **Línea base dinámica** por hora de la semana en vez de umbrales fijos: "CPU 60 % a las 3 a. m. es anómalo aunque esté bajo el umbral de 90 %".
- ML.NET: `DetectSpikeBySsa` (picos) y `DetectChangePointBySsa` (cambios de nivel, típicos después de un despliegue).
- Cada anomalía se registra como evento **informativo o advertencia** (no excepción), con puntaje y explicación.
- Correlación automática: si una anomalía ocurre hasta 2 h después de un cambio, se marca "posible relación con el cambio vX".

### 8.4 Operación ITIL (indicadores)

| Indicador | Qué mide |
|---|---|
| MTTA | Tiempo medio hasta que alguien toma el registro |
| MTTR | Tiempo medio de resolución |
| MTBF | Tiempo medio entre fallas |
| Disponibilidad % | Tiempo en servicio / tiempo total |
| % SLA cumplido | Por prioridad (respuesta y resolución) |
| Resolución en primer nivel | % resuelto por N1 sin escalar |
| Tasa de escalamiento | % que pasa a N2/N3, por categoría |
| Tasa de reapertura | Registros reabiertos / resueltos |
| Antigüedad del backlog | Abiertos por rango de antigüedad |
| Eventos → incidentes | % de eventos de excepción que terminan en incidente (calidad de las alertas) |
| Efectividad de la KEDB | % de alertas que coinciden con un error conocido y se resuelven con su workaround |
| Problemas → errores conocidos | % de problemas con causa raíz documentada |
| Cumplimiento de solicitudes | Tiempo medio por ítem de catálogo |

### 8.5 Desarrollo: métricas DORA

Unen desarrollo y soporte con datos reales de los despliegues de GitHub Actions y los incidentes:

| Métrica DORA | Cálculo |
|---|---|
| Frecuencia de despliegue | Cambios estándar de tipo despliegue por semana |
| Lead time de cambios | Commit → despliegue en producción |
| Tasa de fallo de cambios | % de cambios con incidente o reversión en las 2 h siguientes |
| Tiempo de restauración | MTTR de los incidentes causados por cambios |

### 8.6 Calidad estadística y niveles de servicio

- **SLO y presupuesto de error**: por ejemplo, disponibilidad del 99,5 % en una ventana móvil de 30 días; muestra cuánto presupuesto queda y la **tasa de consumo** (burn rate). Regla: presupuesto agotado → congelar cambios normales.
- **Gráficos de control (SPC)**: carta X-mR para MTTR y tiempo de respuesta, con límites de control (±3σ) y reglas de Western Electric para detectar procesos fuera de control.
- **Pareto** de categorías de incidentes y de causas raíz (80/20).

### 8.7 Pantalla de analítica

Pestañas: **Capacidad** · **Anomalías** · **Operación ITIL** · **Cambios (DORA)** · **Calidad del servicio** (SLO, control, Pareto).

- El período se elige con **filtros dinámicos** (últimos 7, 30, 90 días o período personalizado). **Ninguna fecha ni mes fijo** en títulos, etiquetas o filtros.
- Cada gráfica tiene un texto de lectura en lenguaje natural ("El MTTR bajó 18 % frente al período anterior").

### 8.8 Reporte operativo

- Reporte PDF generado con **QuestPDF** (disponibilidad, incidentes, SLA, problemas, cambios, DORA, pronóstico de capacidad).
- Descarga bajo demanda y envío semanal por correo (Brevo). El envío semanal lo dispara **el propio agente (tarea programada en C#)**, que llama una vez a la API: Render se despierta una sola vez por semana.

---

## 9. Modelo de datos (MongoDB)

```
usuarios            { nombre, email, email_verificado, rol: N1|N2|N3|gestor|admin, password_hash,
                      mfa{ secreto_totp (cifrado), activo, codigos_recuperacion_hash[] },
                      intentos_fallidos, bloqueado_hasta, activo, avatar, ultima_conexion,
                      telegram{ usuario?, telefono_cifrado?, chat_id, verificado, verificado_en },
                      notificar{ correo: bool, telegram: bool } }
telegram_verificaciones { usuario_id, token_inicio, codigo_hash, intentos, expira }  ← TTL 1 hora
notificaciones      { clave_dedup, registro_id, nivel, motivo: apertura|escalamiento|jerarquico|p1|cierre,
                      usuario_id, canal, estado: enviado|fallido, enviado }  ← TTL 30 días
solicitudes_rol     { usuario_id, rol_solicitado: gestor, motivo, estado: pendiente|aprobada|rechazada|revocada,
                      resuelto_por, comentario, creado, resuelto }
auditoria           { usuario_id, accion, entidad, entidad_id, antes{}, despues{}, ip, ts }  ← solo inserción
salas               { tipo: general|evento|solicitud|problema|cambio|mayor|turno, ref_id, participantes[], estado }
mensajes            { sala_id, autor_id, tipo: humano|sistema|plantilla, texto, plantilla_id, campos{},
                      responde_a, menciones[], adjuntos[], visibilidad: interna|visible, editado, eliminado, creado }
salas_lectura       { sala_id, usuario_id, ultimo_entregado_id, ultimo_leido_id, actualizado }
eventos             { host, tipo, severidad: info|advertencia|excepcion, metrica, valor, umbral,
                      clave_dedup, estado, incidente_ref, cambio_relacionado, anomalia{puntaje, explicacion} }
incidentes          { codigo, titulo, descripcion, host, evento_origen_id, impacto, urgencia, prioridad,
                      estado, nivel: N1|N2|N3, asignado_id, coordinador_id, escalamientos[], historial_estados[],
                      sla{ respuesta_objetivo, resolucion_objetivo, avisos_enviados[] }, codigo_cierre, cambio_relacionado,
                      problema_id, creado }
solicitudes         { item_catalogo, solicitante, estado, aprobacion{}, historial_estados[], campos{} }
problemas           { origen: reactivo|proactivo, eventos_ref[], incidentes[], rca{porques[], ishikawa{}},
                      estado, error_conocido_id, historial_estados[] }
errores_conocidos   { codigo, sintoma, tipo_alerta, causa_raiz, workaround, estado_solucion, problema_id, usos }
cambios             { tipo: estandar|normal|emergencia, origen_ref, version, commit, autor, riesgo,
                      plan_vuelta_atras, aprobador, estado, historial_estados[], resultado }
plantillas          { id, nombre, tipo, campos_requeridos[], cuerpo }   ← semilla desde plantillas.yaml
catalogo            { id, nombre, requiere_aprobacion, procedimiento, tiempo_objetivo }
metricas_5m         { host, tipo_host, ts, cpu{}, ram{}, paginacion{}, disco{}, red{}, procesos{}, servicios{},
                      bateria{}, termico{} }  ← campos según el tipo de host; TTL 90 días
latencia_5m         { host, destino, ts, rtt_avg, rtt_p95, jitter, perdida }                     ← TTL 90 días
estado_actual       { host, ts, ...última lectura }                                              ← 1 doc por host
hosts               { id (anónimo: win-xxxxxxxxxx / and-xxxxxxxxxx), tipo: windows|android, version_agente, ultimo_arranque }
                    ← sin nombre del equipo ni fabricante/modelo: la plataforma muestra un nombre genérico por tipo
                      ("Equipo de escritorio", "Equipo portátil", "Celular"), numerado si hay varios
resumen_diario      { host, fecha, metricas{min,avg,max,p95}, disponibilidad, anomalias, pronostico{} } ← TTL 2 años
```

**Índices clave:** `mensajes(sala_id, _id)`, `salas_lectura(sala_id, usuario_id)` único, `eventos(clave_dedup, estado)`, `metricas_5m(host, ts)` + TTL, `cambios(estado, creado)`.

---

## 10. Reglas para no salir de las capas gratuitas

| Recurso | Riesgo | Regla |
|---|---|---|
| **Render: 750 h/mes compartidas por el workspace** | Un servicio despierto 24/7 agota las horas y suspende todos los servicios gratuitos | El agente escribe directo en Atlas; Render solo se despierta por uso humano, eventos de excepción o el reporte semanal. Revisar el consumo de horas del workspace antes de desplegar |
| **Render: 5 GB de ancho de banda compartido** | Adjuntos y WebSocket | Adjuntos por Cloudinary; WebSocket solo con valores que cambian; pausar con la pestaña oculta; cerrar tras 15 min inactivo |
| **Render: arranque en frío ~1 min** | Mala primera impresión | Pantalla "Iniciando servicio…" en el frontend |
| **Atlas M0: 512 MB** | Crecimiento sin límite | Agregados de 5 min, TTL 90 días, `resumen_diario` para períodos largos, top procesos solo como foto |
| **Equipo propio: disco y recursos** | El agente no debe estorbar el uso normal del equipo | Lotes cada 30 min; SQLite crudo 48 h + `VACUUM` semanal; logs de Serilog con rotación (7 archivos × 10 MB); prioridad de proceso `BelowNormal`; análisis ML.NET solo con el equipo inactivo o al arrancar |
| **Brevo: 300 correos/día** | Correo por lectura | Correo por registro (abrir, escalar, cerrar) y reporte semanal; enfriamiento de 30 min por clave; vía API HTTP (Render gratuito bloquea SMTP) |
| **Telegram Bot API** | 1 mensaje/s por chat, ~30/s en total | Agrupar avisos simultáneos en un solo mensaje por usuario; webhook solo para vincular cuentas |
| **IP cambiante** (internet de casa y Render) | Una lista de IPs permitidas se rompe cuando la IP cambia | **Sin restricción de IP** en Atlas (`0.0.0.0/0`). La seguridad se apoya en TLS obligatorio, usuarios de BD separados con contraseñas aleatorias de 32+ caracteres y mínimo privilegio (ver sección 12) |
| **Servicios que se duermen** | Despertarlos desde SysPulse | No se chequean desde SysPulse (lo hace Portfolio Status) |
| **GitHub Releases** | — | Gratuito; aloja el instalador del agente de Windows (`win-x64`) y el APK de Android |
| **Celular 24/7** | Desgaste de batería por estar siempre cargando; consumo de datos móviles | Protección de carga al 80–85 % si el teléfono la ofrece; alerta de temperatura; lotes cada 30 min (≈ pocos MB al mes); chequeos de red livianos |

---

## 11. Gestión de incidentes y SLA (propia)

SysPulse es **independiente**: registra sus propios incidentes, calcula el SLA y lleva el escalamiento. **No se integra con GestorIncidentesTI** (las consultas de SysPulse agotarían la cuota de su base Azure SQL) ni con ninguna otra aplicación del portafolio.

### Ciclo de vida del incidente (Stateless)

Nuevo → Asignado → En progreso ⇄ En espera (pausa el SLA) → Resuelto → Cerrado. Desde Resuelto puede pasar a **Reabierto** (dentro de 7 días), que vuelve a En progreso.

- Se crea desde un evento de excepción (`Asociar a incidente`) o manualmente (N1+).
- Nivel actual (`N1`/`N2`/`N3`) y cada escalamiento con marca de tiempo, motivo y quién lo hizo.
- Al resolver se exige código de cierre y descripción (plantilla "Resolución y cierre").

### SLA por prioridad (valores por defecto, configurables por el admin)

| Prioridad | Primera respuesta (tomarlo) | Resolución |
|---|---|---|
| P1 | 15 min | 4 h |
| P2 | 30 min | 8 h |
| P3 | 2 h | 24 h |
| P4 | 8 h | 72 h |
| P5 | 24 h | 5 días |

- Calendario 24/7 (los hosts monitoreados funcionan todo el día).
- El tiempo **se calcula al consultar** a partir del historial de estados: `vence = creado + objetivo + tiempo total en "En espera"`. No hay procesos en segundo plano.
- Los avisos del 75 % y 100 % (escalamiento jerárquico) se evalúan cuando alguien consulta o cuando llega un evento a la API. Para que un SLA no venza sin aviso aunque nadie use la plataforma, el **agente de Windows** lee cada 5 min los incidentes abiertos **directamente en Atlas** (sin despertar a Render), calcula el porcentaje consumido con la misma regla del dominio (`SysPulse.Domain`, compartido) y **solo cuando un incidente cruza el 75 % o el 100 %** llama a `POST /api/sla/umbral`. Render se despierta únicamente en esos cruces; la API vuelve a validar el cálculo, notifica y registra el aviso para no repetirlo.
- Cambiar la prioridad recalcula los objetivos desde el momento del cambio y queda auditado.

---

## 12. Seguridad

### Roles

JWT con cinco roles. **Todos ven todo** (eventos, salas, métricas, analítica y reportes) y pueden escribir en el chat.

| Rol | Tipo | Responsabilidad | Escala por |
|---|---|---|---|
| `N1` | Técnico | Primer nivel: recibe, clasifica, aplica runbooks y workarounds de errores conocidos | — |
| `N2` | Técnico | Segundo nivel: diagnóstico avanzado, análisis de causa raíz, propone cambios | Conocimiento (funcional) |
| `N3` | Técnico | Tercer nivel: especialista; documenta errores conocidos, implementa cambios, coordina técnicamente un P1 | Conocimiento (funcional) |
| `gestor` | Gestión del servicio | Decide sobre **riesgo, prioridad y aprobación**. No resuelve técnicamente | Riesgo (jerárquico) |
| `admin` | Plataforma | Administra la **configuración** de SysPulse. Es superusuario (tiene todos los permisos) y es la cuenta de testeo | — |

### Funciones del gestor (exclusivas, junto con el admin)

1. **Recibe el escalamiento jerárquico:** aviso con el SLA al 75 %, escalamiento automático al 100 % y aviso inmediato de todo P1 (en la sala, por correo y Telegram).
2. **Declara el incidente mayor (P1)** y **asigna al coordinador** del incidente (normalmente un N3; puede ser él mismo).
3. **Aprueba o rechaza cambios normales** con su evaluación de riesgo y plan de vuelta atrás, y da la **aprobación posterior** de los cambios de emergencia.
4. **Aprueba o rechaza solicitudes del catálogo** marcadas con `requiere_aprobacion`.
5. **Autoriza subir o bajar un registro a P1** (los N2/N3 pueden cambiar entre P2 y P5 con justificación).
6. **Reasigna** cualquier registro a cualquier persona (`/asignar`), sin importar el nivel.
7. **Cierra la revisión post-incidente** de los P1 (sin ella el incidente mayor no se cierra).
8. **Congela o descongela cambios normales** cuando se agota el presupuesto de error del SLO.
9. Recibe el **reporte operativo semanal** por correo.

**El gestor no** toma ni resuelve registros técnicamente, no implementa cambios y no configura la plataforma.

**Segregación de funciones:** nadie aprueba un cambio o una solicitud que creó. Excepción: la cuenta `admin`, para poder probar el flujo completo con un solo usuario (queda marcado en la auditoría como "autoaprobado por admin").

### Funciones del admin (exclusivas)

Desactivar o reactivar cuentas (no crea cuentas, no define niveles N1/N2/N3 ni contraseñas); **aprobar o rechazar las solicitudes de rol gestor** (único caso en que el admin asigna un rol); anular o archivar registros (borrado lógico, ver "Protección contra daños"); umbrales de alerta; plantillas; catálogo de solicitudes; runbooks; canales y destinatarios de notificación; API keys del agente y de la integración. Además puede ejecutar cualquier acción de los demás roles.

### Matriz de permisos

Los niveles técnicos son acumulativos (N3 incluye lo de N2, N2 lo de N1). El gestor no hereda permisos técnicos. Policies: `N1+`, `N2+`, `N3+`, `Gestor`, `Admin`.

| Acción | N1 | N2 | N3 | gestor | admin |
|---|:-:|:-:|:-:|:-:|:-:|
| Ver todo y escribir en el chat | ✓ | ✓ | ✓ | ✓ | ✓ |
| Tomar eventos (`/tomar`), reconocer, descartar | ✓ | ✓ | ✓ | | ✓ |
| Registrar incidente, solicitud y requerimiento | ✓ | ✓ | ✓ | ✓ | ✓ |
| Ejecutar solicitudes del catálogo, aplicar runbooks y workarounds | ✓ | ✓ | ✓ | | ✓ |
| Resolver y cerrar registros asignados (`/resolver`), pausar/reanudar SLA | ✓ | ✓ | ✓ | | ✓ |
| Escalamiento funcional (`/escalar`) | → N2 | → N3 | — | | ✓ |
| Cambiar prioridad entre P2 y P5 con justificación (`/prioridad`) | | ✓ | ✓ | ✓ | ✓ |
| Subir o bajar a P1, declarar incidente mayor, asignar coordinador | | | | ✓ | ✓ |
| Reasignar cualquier registro (`/asignar`) | | | | ✓ | ✓ |
| Crear problemas y RCA (`/problema`), crear RFC (`/cambio`) | | ✓ | ✓ | | ✓ |
| Documentar errores conocidos, implementar cambios, ejecutar cambio de emergencia | | | ✓ | | ✓ |
| Coordinar técnicamente un P1, comunicados (`/comunicado`) | | | ✓ | ✓ | ✓ |
| Aprobar cambios normales y aprobación posterior de emergencia | | | | ✓ | ✓ |
| Aprobar solicitudes del catálogo que requieren aprobación | | | | ✓ | ✓ |
| Recibir escalamiento jerárquico (SLA 75 % / 100 %, P1) | | | | ✓ | ✓ |
| Cerrar la revisión post-incidente | | | | ✓ | ✓ |
| Congelar / descongelar cambios normales (presupuesto de error) | | | | ✓ | ✓ |
| Solicitar el rol gestor | ✓ | ✓ | ✓ | | |
| Aprobar o rechazar solicitudes de rol gestor | | | | | ✓ |
| Desactivar/reactivar cuentas; gestionar umbrales, plantillas, catálogo, runbooks, notificaciones y API keys | | | | | ✓ |
| Anular o archivar registros (borrado lógico, con motivo, reversible) | | | | | ✓ |
| Borrado físico de cualquier dato | | | | | |

- El nivel N1/N2/N3 del usuario determina qué incidentes puede recibir por escalamiento y qué avisos le llegan.

### Rol gestor: por solicitud, lo promueve solo el admin

1. Un usuario N1, N2 o N3 envía desde su perfil una **solicitud de rol gestor** con un motivo.
2. El admin la ve en su bandeja y la **aprueba o rechaza** con un comentario. Se notifica por correo al solicitante.
3. Al aprobarla, el usuario pasa a `gestor`, se invalida su sesión (nuevo JWT con el rol) y queda en la auditoría quién la aprobó y cuándo.
4. El admin también puede **revocar** el rol gestor; el usuario vuelve al nivel N1/N2/N3 que tenía.
5. No hay otra forma de llegar a gestor: ni el registro ni la API lo permiten.

### Protección contra daños

Objetivo: que ningún usuario, por error o mala intención, pueda causar daños serios a los datos ni a la plataforma.

- **Nadie borra datos físicamente desde la aplicación.** No existe ningún endpoint `DELETE` que elimine documentos de negocio. Solo el admin puede **anular o archivar** un registro (borrado lógico con motivo, reversible); el registro sigue existiendo y visible en la auditoría.
- **N3 (y N1, N2, gestor) no pueden:**
  - Borrar, anular ni archivar eventos, incidentes, solicitudes, problemas, errores conocidos, cambios, salas, plantillas ni runbooks.
  - Borrar o editar mensajes de otras personas (cada quien puede editar o retirar **solo su propio** mensaje dentro de 15 minutos; queda "mensaje eliminado por @usuario").
  - Modificar umbrales, plantillas, catálogo, runbooks, canales de notificación, API keys ni cuentas.
  - Descartar un evento de excepción P1 o P2 (lo decide el gestor).
- **Cambios de emergencia del N3:** solo vinculados a un P1 abierto, con plan de vuelta atrás obligatorio, aviso inmediato al gestor y aprobación posterior obligatoria.
- **Errores conocidos y runbooks con versiones:** cada edición crea una versión nueva; nunca se sobrescribe la anterior.
- **Auditoría inmutable:** colección `auditoria` de solo inserción (quién, qué, cuándo, antes/después). Nadie la modifica desde la aplicación, ni el admin.
- **Mínimo privilegio en la base:** el usuario de Atlas de la API tiene acceso solo a la base `syspulse`, sin permisos de administración del clúster. Si M0 permite roles personalizados, se le quitan `dropCollection` y `dropDatabase`.
- **API key del agente** solo puede escribir eventos y métricas; no lee ni modifica nada más.
- **Límite de escrituras** por usuario (rate limiting) para evitar que un error o un script llene la base.
- **Respaldo semanal gratuito:** Atlas M0 no tiene backups; el agente exporta en C# las colecciones de negocio (no métricas) a JSON comprimido en `%ProgramData%\SysPulse\respaldos` y conserva las últimas 4 copias.

### Registro de cuentas, contraseña y MFA

- **Registro propio:** cada persona crea su cuenta. En el formulario de registro **elige su nivel: solo `N1`, `N2` o `N3`**. El admin no crea cuentas ni asigna esos niveles.
- **Contraseña propia:** la define cada usuario. Mínimo 12 caracteres, se rechazan contraseñas comunes, se guarda solo el hash (`PasswordHasher` de ASP.NET Core Identity, PBKDF2). Nadie, ni el admin, puede ver ni fijar la contraseña de otro.
- **Verificación de correo** al registrarse (enlace por Brevo API). Sin verificar, la cuenta no puede iniciar sesión.
- **Telegram obligatorio en el registro.** El formulario pide **una** de estas dos opciones:
  - **Nombre de usuario** de Telegram (ej. `@ana_perez`; 5–32 caracteres: letras, números y `_`).
  - **Número de teléfono con código de país** (selector de país, Colombia `+57` por defecto; validado y normalizado a formato E.164 con `libphonenumber-csharp`, gratuita).

### Verificación de Telegram (código de validación)

La API de bots de Telegram **no permite que un bot escriba primero** a un @usuario o a un número: solo puede escribir a quien antes inició una conversación con el bot. Por eso la verificación arranca con un clic del usuario:

1. Tras el formulario, la plataforma muestra el botón **"Verificar en Telegram"**, que abre `t.me/<bot>?start=<token>` (token de un solo uso, válido 15 min). El usuario pulsa **Iniciar** en Telegram.
2. Telegram envía ese inicio al webhook de la API (protegido con `TELEGRAM_WEBHOOK_SECRET`), que comprueba la identidad según lo que se registró:
   - **@usuario:** el `username` que llega de Telegram debe coincidir (sin distinguir mayúsculas) con el registrado. Si no coincide, se rechaza y se avisa en la plataforma.
   - **Número:** el bot responde con un botón **"Compartir mi número"** (`request_contact`). El contacto recibido debe ser **del propio usuario** (`contact.user_id == from.id`) y su número normalizado debe coincidir con el registrado.
3. Si coincide, el bot envía un **código de 6 dígitos** por Telegram (válido por pocos minutos, con intentos y envíos limitados).
4. El usuario escribe el código en la plataforma. Al acertar, se guarda el `chat_id` y la cuenta queda con **Telegram verificado**.

- Sin Telegram verificado la cuenta no se activa (es el canal de avisos por nivel).
- Si una persona no tiene `@usuario` en Telegram, debe usar el número.
- Cambiar el Telegram desde el perfil repite la verificación completa.
- El número se guarda cifrado y en pantalla solo se muestra enmascarado (`+57 ••• ••• 1234`).
- El código de Telegram **solo verifica el canal**; el inicio de sesión sigue usando contraseña + MFA con app autenticadora.
- **MFA obligatorio con TOTP** (Google Authenticator, Microsoft Authenticator, Authy, etc.), librería `Otp.NET` (gratuita):
  - Al primer inicio de sesión se muestra el QR y se exige confirmar un código antes de entrar.
  - Se entregan **10 códigos de recuperación** de un solo uso (guardados como hash).
  - **Sin MFA por SMS** (tiene costo y es menos seguro).
- **Recuperar contraseña:** enlace de un solo uso por correo (Brevo API), válido 30 minutos; sigue exigiendo el MFA.
- **Bloqueo temporal** ante intentos fallidos repetidos y límite de peticiones en la autenticación.
- **Cuenta admin semilla:** se crea al arrancar si no existe, con correo y contraseña temporal de variables de entorno. En el primer inicio de sesión se obliga a cambiar la contraseña y configurar el MFA. Es también la cuenta de testeo.
- Nada de esto se sube al repositorio ni aparece en el README.

### Otras reglas
- Secretos por variables de entorno (Render), configuración cifrada con DPAPI en el equipo y GitHub Secrets; `.env.example` sin valores reales.
- API keys distintas para el agente y para GitHub Actions (registro de cambios).
- El agente corre como cuenta virtual de servicio `NT SERVICE\SysPulseAgent` (sin privilegios de administrador), miembro solo de "Performance Monitor Users" y "Event Log Readers". Su configuración (`%ProgramData%\SysPulse\agent.json`) va cifrada con DPAPI y con permisos solo para esa cuenta y Administradores.
- README sin datos de base de datos, usuarios ni contraseñas.
- **Sin restricciones de IP** (la IP de casa y la de Render pueden cambiar). Compensación:
  - Atlas Network Access `0.0.0.0/0`, con TLS obligatorio (por defecto en Atlas).
  - Dos usuarios de BD distintos, `syspulse-api` y `syspulse-agent`, con contraseñas aleatorias de 32+ caracteres, acceso solo a la base `syspulse`, sin permisos de administración del clúster. Rotación si hay sospecha de filtración.
  - Si M0 permite roles personalizados, `syspulse-agent` solo puede insertar y actualizar en `metricas_5m`, `latencia_5m`, `estado_actual` y `resumen_diario`.
  - La API se protege con JWT + MFA para personas y API keys por cliente (agente y CI), más rate limiting.

---

## 13. Despliegue

| Pieza | Plataforma (gratuita) | Cómo |
|---|---|---|
| Código y CI/CD | GitHub (repositorio **público**: minutos de Actions ilimitados) | Secretos en GitHub Secrets |
| Base | MongoDB Atlas M0, proyecto nuevo `syspulse`, región AWS `us-east-1` (cercana a Colombia y a Render) | Dos usuarios de BD separados: `syspulse-api` y `syspulse-agent`; sin restricción de IP |
| API | Render web service gratuito, región Oregon, desde `deploy/Dockerfile.api` | Deploy hook llamado desde GitHub Actions; health check `/health` |
| Frontend (web + PWA) | Vercel Hobby, raíz `web/`, framework Vite | Integración con GitHub; `vercel.json` con cabeceras de seguridad y del service worker |
| Agente Windows | Cada equipo Windows que se agregue (PC, portátil, tablet), servicio de Windows con inicio automático | `dotnet publish` autocontenido, un solo archivo, `win-x64` → GitHub Releases → `SysPulse.AgentInstaller` lo instala o actualiza |
| Agente Android | Cada celular Android que se agregue, 24/7 | `dotnet publish -f net10.0-android` → APK firmado con keystore propio (gratis) → GitHub Releases → instalación manual |
| Dominio | Subdominios gratuitos `*.onrender.com` y `*.vercel.app` | Dominio propio omitido (tiene costo) |

### Alta de equipos y seguridad de los instaladores

- **Alta con código:** un técnico N2 o N3 o el admin genera en la web (Salud de hosts → Agregar equipo, o Administración → Equipos) un código de un solo uso que vence en 24 h; solo se guarda su huella. El instalador de Windows (`install --codigo`) y la app Android (Vincular) lo canjean en `POST /api/agentes/alta` (limitado por IP) y reciben su configuración, que guardan cifrada (DPAPI / Android Keystore).
- **Instaladores públicos sin secretos ni funciones de administración:** las herramientas de administración (`config-central`, `publicar-version`, `mongo-estandar`) viven en `tools/SysPulse.Herramientas`, que no se publica. Los binarios se compilan sin rutas del equipo de compilación y el APK sin modo de depuración. `tools/escanear-secretos.ps1` es obligatorio antes de cada publicación.
- **Nombres estables:** cada equipo recibe un nombre genérico con número fijo por tipo (colección `hosts_alias`); agregar equipos no renombra a los existentes. Retirar un equipo lo oculta sin borrar su historial.
- **Usuario de base exclusivo para agentes (obligatorio):** las altas entregan solo `MONGO_URI_AGENTES` y `MONGO_URI_AGENTES_ANDROID`; sin ellas, `POST /api/agentes/alta` responde 503 y no gasta el código. Nunca se entrega `MONGO_URI` (la de la API).

### Orden de despliegue

1. **Repositorio:** `git init`, repo público en GitHub, ramas protegidas (`main` solo por PR con CI en verde).
2. **Atlas:** proyecto `syspulse` → clúster M0 → usuarios `syspulse-api` y `syspulse-agent` (acceso solo a la base `syspulse`, contraseñas aleatorias) → Network Access: `0.0.0.0/0` (sin restricción de IP).
3. **API en Render:** web service desde Docker, instancia Free, variables de entorno (ver tabla de abajo). CORS solo para el dominio de Vercel. SignalR se conecta **directo** a Render (las rewrites de Vercel no transportan WebSocket); las llamadas REST también van directo a Render con CORS.
4. **Frontend en Vercel:** importar el repo, raíz `web/`, variable `VITE_API_URL` con la URL de Render.
5. **Agente en el equipo:** descargar `SysPulse.AgentInstaller.exe` del último release y ejecutarlo como administrador **una sola vez**. El instalador:
   - copia el agente a `%ProgramFiles%\SysPulse\Agent`;
   - crea el servicio `SysPulseAgent` (inicio automático retrasado, reinicio automático si falla) con la cuenta `NT SERVICE\SysPulseAgent`;
   - agrega esa cuenta a "Performance Monitor Users" y "Event Log Readers";
   - pide los secretos (`MONGO_URI`, `API_URL`, `AGENT_API_KEY`, Telegram) y los guarda cifrados con DPAPI en `%ProgramData%\SysPulse\agent.json`.
   - Actualizaciones: el agente revisa una vez al día el último release de GitHub (API pública, sin token); si hay versión nueva, verifica el SHA-256 y se reemplaza con el instalador.
6. **Agente en el celular:** descargar el APK firmado del último release e instalarlo (permitir "instalar apps desconocidas" solo para el navegador usado). Al abrir la app:
   - se ingresan `MONGO_URI` (formato estándar, sin `+srv`), `API_URL`, `AGENT_API_KEY` y Telegram, guardados en `SecureStorage`;
   - se conceden los permisos de notificación y de exclusión de la optimización de batería;
   - el servicio en primer plano arranca y queda configurado para iniciar con el teléfono.
   - Actualizaciones: la app revisa el último release una vez al día y, si hay versión nueva, muestra una notificación para instalarla (Android no permite actualizar sin confirmación fuera de Google Play).
7. **Notificaciones:** cargar en Render las claves de Brevo y Telegram; probar con `POST /api/notificaciones/prueba` (solo admin).
8. **Admin semilla:** primer arranque de la API crea el admin; iniciar sesión, cambiar contraseña y configurar MFA.
9. **CI/CD:** activar los workflows (ver abajo).

### Workflows de GitHub Actions

| Workflow | Disparo | Pasos |
|---|---|---|
| `ci.yml` | PR y push | `dotnet build` + `dotnet test` · `npm ci` + `npm run build` + Playwright en `web/` |
| `deploy-api.yml` | push a `main` con cambios en `src/` | CI → llamada al deploy hook de Render → espera `/health` → `POST /api/cambios` (cambio estándar con versión, autor y commit) |
| `release-agent.yml` | tag `agent-v*` | `dotnet publish` `win-x64` (agente + instalador) y APK Android firmado → SHA-256 → GitHub Release → registro del cambio estándar |
| Frontend | push a `main` | Lo despliega Vercel automáticamente |

### Variables de entorno (sin valores en el repositorio)

| Dónde | Variables |
|---|---|
| Render (API) | `MONGO_URI`, `JWT_SECRET`, `MFA_ENCRYPTION_KEY`, `AGENT_API_KEY`, `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SENDER_NAME`, `TELEGRAM_BOT_TOKEN`, `TELEGRAM_FIXED_CHAT_IDS` (2 cuentas fijas), `TELEGRAM_WEBHOOK_SECRET`, `CLOUDINARY_URL`, `CORS_ORIGIN`, `ADMIN_EMAIL`, `ADMIN_TEMP_PASSWORD` |
| Equipo (agente, cifrado DPAPI) | `MONGO_URI` (usuario `syspulse-agent`), `API_URL`, `AGENT_API_KEY` |
| Celular (agente, `SecureStorage`) | `MONGO_URI_ANDROID` (usuario `syspulse-agent`, formato `mongodb://`; se genera desde `MONGO_URI` con `SysPulse.AgentInstaller mongo-estandar`), `API_URL`, `AGENT_API_KEY` |
| Vercel (web) | `VITE_API_URL` |
| GitHub Secrets | `RENDER_DEPLOY_HOOK`, `API_URL`, `CI_API_KEY`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD` |


**Nunca** se usa un servicio de "ping" externo para mantener Render despierto: consumiría las 750 h del workspace.

---

## 14. Fases

### v1 — Monitoreo y sala de incidentes (publicable)
- Agente de Windows completo (métricas, chequeos de red, alertas, SQLite, envío a Atlas)
- Agente Android (sonda móvil) sobre el mismo núcleo `SysPulse.Agent.Core`; vista de todos los hosts en "Salud de hosts" (sin límite de cantidad)
- API: eventos + plantillas + autenticación (roles `N1` / `N2` / `N3` / `gestor` / `admin` , registro propio con elección de N1/N2/N3, contraseña propia, MFA TOTP y admin semilla)
- Notificaciones: Brevo API y Telegram con enfriamiento, enviadas solo por la API
- Chat SignalR: `#operaciones`, salas, vistos, presencia, comandos básicos
- Incidentes propios: registro, escalamiento N1 → N2 → N3 y SLA por prioridad
- Frontend React + PWA instalable: registro/MFA, Mi turno, Salud del host, Sala

### v2 — ITIL completo
- Solicitudes + catálogo, problemas + RCA, errores conocidos, cambios + registro automático desde CI/CD
- Pantallas: Problemas y KEDB, Cambios
- Incidente mayor y entrega de turno

### v3 — Analítica
- Resumen diario, pronóstico y anomalías con ML.NET en el agente
- Indicadores ITIL, DORA, SLO y presupuesto de error, gráficos de control, Pareto
- Pantalla de Analítica y reporte PDF semanal

---

## 15. Lo que refuerza

- **C# / .NET 10:** Worker Services como servicio de Windows, ASP.NET Core, SignalR, ML.NET, async/concurrencia (`Task.WhenAll`, `Channel<T>`, `CancellationToken`), diseño por capas, máquinas de estado, Polly, Docker.
- **Windows y redes:** contadores de rendimiento, WMI, Event Log, servicios de Windows, DPAPI, cuentas virtuales de servicio, TCP/ICMP/DNS.
- **ITIL v4:** eventos, incidentes, solicitudes, problemas, conocimiento, cambios, niveles de servicio, mesa de servicio.
- **Analítica:** series de tiempo, pronóstico, detección de anomalías, indicadores operativos, DORA, SLO, control estadístico de procesos.
- **Diseño de sistemas distribuidos:** contratos compartidos entre agentes y API, store-and-forward, resiliencia con Polly, notificaciones multicanal.

---

## 16. Entrada para el README del perfil

```markdown
- **SysPulse** (C#, .NET 10, ASP.NET Core, SignalR, ML.NET, Worker Service, MongoDB, React, PWA). Plataforma: Render, Vercel, MongoDB Atlas y agente como servicio de Windows. Plataforma de operaciones TI: monitoreo de host desde adentro (CPU, memoria, paginación, disco, red, procesos, servicios y Event Log) con buffer local tolerante a cortes; prácticas ITIL v4 de eventos, solicitudes, problemas con análisis de causa raíz, errores conocidos y cambios; salas de coordinación en tiempo real con confirmación de lectura por usuario y plantillas operativas; y analítica de capacidad, detección de anomalías con ML.NET, indicadores ITIL, métricas DORA y SLO. Gestiona sus propios incidentes, SLA y escalamientos, y notifica por correo y Telegram según el nivel.
```
