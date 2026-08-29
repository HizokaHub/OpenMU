# GAMEDESIGN — Modo de juego custom sobre OpenMU (S6E3)

> Documento de diseño. Contexto trasladado desde una conversación previa para
> centralizar el trabajo en este repositorio. A partir de ahora el diseño y el
> desarrollo avanzan únicamente aquí.

---

## Visión general

Un modo de juego custom estilo **MOBA** (inspirado en League of Legends)
construido sobre el motor y los assets de **MU Online Season 6**, pensado para
eventualmente abrirse a un **servidor comunitario público**.

El desarrollo es **por fases**, empezando por un **ARAM simple** y escalando
hacia un **MOBA completo** si el resultado funciona bien.

---

## Restricciones técnicas confirmadas

- El servidor corre sobre **OpenMU** con protocolo **Season 6 Episodio 3
  (S6E3)**. No es posible usar clientes de seasons posteriores (S19, etc.)
  porque el protocolo de red es **incompatible**.
- Personajes / skills / ítems de seasons posteriores a S6 **se pueden recrear
  manualmente** (stats, lógica, mecánica) pero corriendo bajo cliente y
  protocolo S6 — **reutilizando assets visuales existentes del cliente S6** en
  vez de assets de esas seasons (por compatibilidad de formato y por copyright).
- El desarrollo se hace **100% local primero** (sin costos). Solo se contratará
  un **VPS** cuando el modo de juego esté listo para abrirse al público.

---

## Cliente base — decidido: MuMain (open source)

**Repo:** <https://github.com/sven-n/MuMain> (mantenido por un dev núcleo de OpenMU).

- Fork modernizado del cliente de MU Online (base S5.2) llevado a paridad con
  **S6E3** (casi completo; solo faltan "Lucky Items"). **C++ + OpenGL 3.3** para
  render + librería de red en **C# .NET 10** (Native AOT). Conecta
  **exclusivamente a OpenMU** por el protocolo extendido (**puerto 44406**).
- **Cámara con distancia de zoom configurable** (valor por defecto 1735) — es
  un setting, no un parche. Resoluciones múltiples, modo ventana, V-Sync, FPS.
- **Por qué este y no el cliente retail 1.04d:** el modo MOBA necesita muchos
  cambios de cliente (HUD de cooldowns / timer / marcador, UI de tienda,
  indicador de reducción de CD por nivel, minimapa de arena…). Nada de eso se
  puede hacer sobre un binario cerrado. El `main.exe` del repack S6 descargado
  difiere ~7,8 MB del original (probablemente *packed*) → cada mod sería
  ingeniería inversa desechable.
- **El cliente retail descargado NO se descarta:** MuMain carga los assets
  (`Data/`: modelos, mapas, efectos, sonidos) de un cliente MU real. Se siguen
  usando los del cliente extraído en
  `C:\Users\aruiz\Proyectos\mu-client-s6\...\Data`. El repack queda como fuente
  de assets y para pruebas de sanidad rápidas.
- **Coste asumido:** toolchain de C++ (Visual Studio 2022 + CMake/Ninja).
  Estado: **hecho** — VS Community 2022 (workloads C++ y .NET) + CMake 4.4.3
  instalados; MuMain clonado en `C:\Users\aruiz\Proyectos\mu-main`, configurado
  (`cmake --preset windows-x64`) y compilado (`cmake --build --preset
  windows-x64-release`, ~7 min). Binario en
  `mu-main\out\build\windows-x64\src\Release\Main.exe` (con `config.ini`
  sembrado, `MUnique.Client.Library.dll` y `Data\` copiados al lado).
  `config.ini`: `ServerIP=127.127.127.127`, `ServerPort=44406`, `Locale=es`,
  ventana 1366×768. Arranca OK contra el servidor local.
- **Recompilar MuMain** (desde `C:\Users\aruiz\Proyectos\mu-main`, en una
  *Developer PowerShell for VS 2022* con `C:\Program Files\dotnet` en el PATH):
  `cmake --build --preset windows-x64-release`. Preset con editor in-game
  (ImGui, tecla F12): `windows-x64-mueditor`.

---

## Arquitectura de mapa — decidido: mapa dedicado + instancias

El MOBA **no corre sobre el mapa público de Crywolf (34)**. Se crea un mapa
propio, número alto para no chocar nunca con un mapa oficial de OpenMU
(**mapa 200 = "Arena MOBA"**, provisional):

- **Servidor (OpenMU):** nuevo `GameMapDefinition` #200 cuyo `TerrainData` es
  una copia del de Crywolf (34) — o una versión ya acordonada. A ese mapa se le
  enganchan **solo** los plugins del MOBA (oleadas, torretas, límites de arena,
  estado de partida, timer, equipos, tienda). El Crywolf real (mapa 34) queda
  **intacto**, con su evento nativo.
- **Instancias:** cada partida MOBA es una **instancia aislada** del mapa 200
  (mismo modelo que Blood Castle / Chaos Castle / Devil Square). Estado propio
  por instancia; varias partidas en paralelo sin interferencia. OpenMU ya trae
  la infraestructura de instancias.
- **Cliente (MuMain):** el cliente carga `Data/World<N>/`. Se resuelve con un
  **alias en MuMain**: `WorldActive == 200` → cargar los assets de `World34`
  (evita duplicar la carpeta de assets; MuMain es nuestro). Alternativa simple:
  copiar `Data/World34/` → `Data/World200/`.
- Esto es también el **primer ladrillo de la Fase 3** (multi-mapa + instancias).
  Para una sola arena no hace falta sincronizar estado entre instancias todavía.

---

## Fase 1 — ARAM básico (prioridad actual)

- **Un solo mapa dedicado** (mapa 200, ver *Arquitectura de mapa*): copia del
  terreno de Crywolf, acordonado como arena vía plugin de borde.
- **Cooldowns y daño de skills editables** vía configuración / código para
  balancear el modo.
- Los personajes **empiezan en nivel 100** (temporal, dentro de la instancia),
  con **snapshot** del personaje real que se **restaura al salir**.
- **Experiencia acelerada** dentro de la partida para subir de nivel y escalar
  el daño de las skills.
- **Reducción de cooldown por nivel** dentro de la partida (regla custom, no
  nativa de MU).
- **Tienda de ítems**: moneda de partida **separada del Zen normal**, catálogo
  de ítems reescalados o custom, vendida vía **NPC vendedor** (reutilizando el
  sistema de NPC vendedor existente).

---

## Fase 2 — Oleadas de mobs (push de línea)

- **Mobs de oleada** con **ruta de waypoints fija** que avanzan por un carril,
  atacando solo si detectan enemigos en el camino (reutilizando la **IA de
  agresividad** existente de OpenMU).
- **Torretas**: NPCs estáticos (velocidad de movimiento 0) con **skill de
  ataque a rango**, agrediendo automáticamente a lo que entre en su radio según
  **facción / bando**.
- **Base enemiga**: reutilizar la lógica de **puertas de Castle Siege**
  (estructura con HP que dispara un evento de victoria / derrota al ser
  destruida). Confirmado que el plugin de **Castle Siege ya viene activo por
  defecto** en OpenMU.
- **Colisión / pathing custom** solo aplica a la **IA de los mobs de oleada**
  (para que sigan su carril). Los **jugadores se mueven libres** por todo el
  terreno caminable normal, **sin muros artificiales** — permite roams / ganks
  entre líneas como en un MOBA real.

---

## Fase 3 — Escalado a MOBA completo (visión a futuro, sin decidir aún)

- En vez de forzar **3 carriles en un solo mapa** (los mapas de MU no tienen ese
  diseño geométrico), usar **varios mapas de MU distintos, uno por carril / zona**
  (top / mid / bot / jungla), conectados por **portales tipo Lost Tower**
  (reutilizando el sistema de **warp existente entre pisos**).
- **Jungla** como mapa central propio, con **mobs estáticos que darían buffs al
  morir** (mecánica custom, no existe nativamente en MU).
- **Alternativa explorada y descartada por alta complejidad**: crear un mapa
  único con terreno editado a mano combinando zonas temáticas (requiere
  herramientas de edición 3D; esfuerzo mucho mayor que la opción de
  multi-mapa + portales).
- **Pendiente resolver**: sincronización de estado (oleadas, torretas caídas,
  temporizador de partida) entre **múltiples instancias de mapa simultáneas**.

---

## Decisión pendiente

Aún **no se ha cerrado el alcance exacto de la Fase 1**:

- ~~Qué mapa de S6 reciclar.~~ → Cerrado: mapa dedicado #200 con terreno de
  Crywolf (34), como instancia. Ver *Arquitectura de mapa*.
- Valores exactos de nivel / daño / cooldown.
- Diseño de la tienda (moneda, catálogo, precios, cómo se gana la moneda).

Antes de empezar a programar, se cierran estas decisiones **una por una**.

### Registro de decisiones cerradas

| # | Decisión | Valor acordado | Fecha |
|---|----------|----------------|-------|
| 1 | Cliente base para el desarrollo del modo | **MuMain** (open source, C++/OpenGL + red .NET 10). Conexión por puerto 44406. Cámara/zoom configurables (F9/F10/F11, `config.ini [Camera] Zoom`). Trae su propio `Data\` completo (~739 MB, World1–82 salvo 30/33/37) — no requiere fusionar assets del cliente retail. Instalado y compilado OK. | 2026-08-28 |
| 2 | Mapa de la arena MOBA | **Mapa dedicado #200** (número provisional), `TerrainData` = copia de Crywolf (34), corrido como **instancia** por partida. Crywolf real (34) intacto. Cliente: alias en MuMain `World200 → assets de World34`. | 2026-08-28 |
| 3 | Cliente MOBA — features de UI/cámara ya implementadas en MuMain (rama `moba-camera`, fork HizokaHub/MuMain) | Cámara MOBA (F9, edge-pan con foco de mundo, zoom de rueda 0.7×–1.8×, `Y` snap/follow, F11 reset), walk-to-far-click + chase de click derecho, mapa de Tab completo, y **minimapa fijo estilo LoL en Crywolf** (esquina inferior derecha). Falta: apuntar todo esto al mapa #200 en vez de al 34. | 2026-08-28 |

### Notas de diseño relacionadas

- **Límites de la arena:** no dependemos de la forma nativa del mapa. Se recorta
  la zona jugable con (A) un **plugin de borde** en el servidor que rebota al
  jugador si sale del polígono de arena + mensaje, y opcionalmente (B) "hornear"
  el `TerrainData` del mapa para que oleadas/torretas/spawns respeten el mismo
  límite (Fase 2), y (C) editar el `.att` del cliente para el muro visual
  (pulido posterior). Los límites viven en config → ajustables sin recompilar.
  Consecuencia: se puede tallar un carril alargado dentro de *cualquier* mapa,
  así que la elección de mapa pesa más por ambiente/assets que por geometría.
