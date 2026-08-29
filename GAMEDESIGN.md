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

## Fase 1 — moba básico (diseño cerrado, listo para implementar)

Arena: **mapa dedicado #200** (ver *Arquitectura de mapa*), corrido como
**instancia por partida**. Formato objetivo **5v5**; con demanda para varios
equipos se abren **varias instancias simultáneas** del mismo mapa, cada una con
sus propios jugadores (modelo Blood Castle / Devil Square).

### Elegibilidad

- Solo personajes con **nivel de cuenta real = 400** pueden entrar a la partida
  (no se admite nivel inferior), y con **Master Skill activo**.

### Al entrar a la partida (setup automático)

- **Inventario real** del jugador → se guarda como **snapshot temporal**.
- Se le entrega solo un **arma básica acorde a su clase** (ej. espada básica para
  Dark Knight, staff básico para Dark Wizard) — **sin armadura, alas ni
  accesorios**.
- **Árbol de Master Skill Tree** → se **resetea a vacío**, **Master Level vuelve
  a 1** (temporal, solo dentro de la instancia).
- El jugador elige un **loadout de 4 a 6 skills activas** (excluyendo buffs) de
  entre todas las que su clase ya tenía **desbloqueadas a nivel 400** en su
  progreso real.

### Progresión dentro de la partida

- **Master Level** sube de 1 hasta **~30** durante el match (ganando **5 Master
  Point por Master Level**).
- **No** se espera ni se busca completar el árbol de Master Skill Tree en una
  partida — la idea es que cada match resulte en una **build parcial /
  estratégica** distinta.
- **Master EXP** se gana matando **mobs de oleada**, **jugadores rivales**, y por
  **objetivos** (torretas / base, cuando lleguemos a Fase 2).

### Economía (oro de partida, separado del Zen del servidor)

Fuentes de oro:

- **Farmeo de mobs / oleadas** (recompensa individual, tipo *last hit* de LoL).
- Cada vez que **sube de Master Level** (recompensa de progresión general).
- **Bono por matar a un jugador rival.**
- **Shutdown gold**: bono extra por matar a un rival que viene en **racha de
  kills** (mecánica anti-snowball).
- **Ingreso pasivo de oro por tiempo** transcurrido, **mayor para el equipo que
  va perdiendo** (anti-snowball adicional).

### Tienda de ítems

- **Sin requisito de nivel** — todo se desbloquea solo con **oro de partida**.
- **3 tiers de precio**: Tier 1 (barato, stats bajos-medios), Tier 2 (medio, con
  opciones / excelente), Tier 3 (caro, ítems raros / ancestrales o custom).
  Precio calculado con una **fórmula proporcional al total de stats** del ítem,
  no asignado a mano ítem por ítem.
- Los **buffs** (defensa, ataque, etc.) se compran en la tienda con oro; **NO**
  forman parte del loadout de skills elegido al inicio.
- Todos los ítems comprados en partida son **instance-bound** (se pierden al
  salir, no se transfieren al inventario real del servidor).

### Al salir de la partida (cleanup automático)

- Se **restaura el inventario real** guardado al entrar.
- Se **restauran el árbol de Master Skill Tree y el Master Level reales**, tal
  como estaban antes de entrar.
- Todo el **oro, ítems comprados y progreso de Master Level** ganado en la
  partida se **descarta**.

### Duración y timers

- El **tiempo de respawn tras morir** escala con la **duración de la partida**
  (mecánica estándar anti-snowball).

### Pendiente de definir en desarrollo (no bloqueante para empezar)

- Balance específico de **daño / cooldown por clase** para este modo.
- Sistema **anti-AFK / abandono** de partida.
- Si se **restringe o no** tener clases duplicadas en el mismo equipo.

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

## Decisiones cerradas

El **alcance de la Fase 1 está cerrado** (ver *Fase 1 — moba básico*). Lo que
queda abierto es balance fino y anti-abuso, listado en *Pendiente de definir en
desarrollo* dentro de esa misma sección — nada de eso bloquea empezar a
programar.

### Registro de decisiones cerradas

| # | Decisión | Valor acordado | Fecha |
|---|----------|----------------|-------|
| 1 | Cliente base para el desarrollo del modo | **MuMain** (open source, C++/OpenGL + red .NET 10). Conexión por puerto 44406. Cámara/zoom configurables (F9/F10/F11, `config.ini [Camera] Zoom`). Trae su propio `Data\` completo (~739 MB, World1–82 salvo 30/33/37) — no requiere fusionar assets del cliente retail. Instalado y compilado OK. | 2026-08-28 |
| 2 | Mapa de la arena MOBA | **Mapa dedicado #200** (número provisional), `TerrainData` = copia de Crywolf (34), corrido como **instancia** por partida. Crywolf real (34) intacto. Cliente: alias en MuMain `World200 → assets de World34`. | 2026-08-28 |
| 3 | Cliente MOBA — features de UI/cámara ya implementadas en MuMain (rama `moba-camera`, fork HizokaHub/MuMain) | Cámara MOBA (F9, edge-pan con foco de mundo, zoom de rueda 0.7×–1.8×, `Y` snap/follow, F11 reset), walk-to-far-click + chase de click derecho, mapa de Tab completo, y **minimapa fijo estilo LoL en Crywolf** (esquina inferior derecha). Falta: apuntar todo esto al mapa #200 en vez de al 34. | 2026-08-28 |
| 4 | Alcance completo de la Fase 1 (moba básico) | Entrada solo con **cuenta nivel 400 + Master Skill activo**; al entrar: snapshot de inventario, **arma básica por clase** sin equipo, **Master Tree a vacío / Master Level 1**, **loadout de 4–6 skills activas** de las ya desbloqueadas a 400. Progresión: Master Level 1→~30 (**5 MP/nivel**), Master EXP por mobs/kills/objetivos. **Oro de partida** (separado del Zen) por farmeo *last-hit*, subir de Master Level, kills, **shutdown gold** y **renta pasiva mayor para el que pierde**. **Tienda sin requisito de nivel**, **3 tiers** con precio por **fórmula de stats totales**, **buffs se compran con oro** (no van en el loadout), ítems **instance-bound**. Al salir: restaurar inventario + Master Tree/Level reales, descartar oro/ítems/Master Level de la partida. **Respawn escala con la duración** del match. Formato **5v5** en **instancias simultáneas** del mapa #200. | 2026-08-29 |

### Notas de diseño relacionadas

- **Límites de la arena:** no dependemos de la forma nativa del mapa. Se recorta
  la zona jugable con (A) un **plugin de borde** en el servidor que rebota al
  jugador si sale del polígono de arena + mensaje, y opcionalmente (B) "hornear"
  el `TerrainData` del mapa para que oleadas/torretas/spawns respeten el mismo
  límite (Fase 2), y (C) editar el `.att` del cliente para el muro visual
  (pulido posterior). Los límites viven en config → ajustables sin recompilar.
  Consecuencia: se puede tallar un carril alargado dentro de *cualquier* mapa,
  así que la elección de mapa pesa más por ambiente/assets que por geometría.
