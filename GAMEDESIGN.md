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

### Entrada y matchmaking

**NPC de cola en Lorencia** con 3 opciones:

1. **Buscar partida solo** — entrás a la cola individual.
2. **Buscar partida con party (2–4)** — tu party ya formado entra junto a la cola.
3. **Buscar partida por equipo de 5** — party de exactamente 5.

**Emparejado:**

- Opciones **1 y 2 comparten pool**: el matchmaker combina solos + parties de 2–4
  hasta armar dos equipos de 5 (ej. party de 3 + party de 2 vs party de 4 + 1
  solo; cualquier combinación que sume 5 por lado). Los integrantes de un mismo
  party siempre caen en el **mismo equipo**.
- Opción **3 tiene pool propio**: solo empareja **5 preformados vs 5
  preformados**. Nunca se mezcla con el pool 1+2.

**Confirmación de partida (ready-check):**

- Al completar los 10, a cada jugador le llega un prompt de "unirse a la
  partida".
- Ventana de respuesta: **10 segundos**.
- Si alguien **rechaza o no responde** en 10 s:
  - La partida **no arranca**.
  - Ese jugador recibe **una advertencia**.
  - Se lo saca y se busca un **reemplazo** para su slot; el resto vuelve al frente
    de la cola.
  - *(A definir en dev: si los 9 que ya aceptaron quedan pre-confirmados un rato o
    si el ready-check se reemite completo.)*

**Penalización por no responder / rechazar (solo afecta la cola MOBA):**

- **3 advertencias → bloqueo de 1 hora** (no puede entrar a cola ni por party).
- Tras cumplir el bloqueo, si **vuelve a fallar** el siguiente bloqueo **escala**
  (1 h → 2 h → …, curva exacta a definir en dev).
- Si tras cumplir un bloqueo **vuelve a la cola y esta vez acepta**, las
  advertencias se **resetean a 0**.
  - *(A definir en dev: si un accept exitoso resetea las advertencias siempre, o
    solo después de haber cumplido un bloqueo.)*
- Contador de advertencias y tiempo de bloqueo se **persisten en BD** (sobreviven
  reinicio del server y relog).

### Dónde vive el estado del match (RAM, no BD)

- El clon y todo el estado de partida (inventario, oro, Master Level, posición,
  cooldowns) los posee un **objeto de match server-side**, uno por partida activa
  (modelo `MiniGameContext` de los mini-juegos), **no** la conexión del jugador.
  Vive en RAM toda la partida y se descarta al terminar.
- **Desconexión / reconexión:** al perder conexión el clon **no se destruye** —
  el match lo mantiene y corre el anti-AFK (15 s tomable por aliados, 20 s recall
  a base). Los aliados que lo controlan mutan **ese mismo clon en RAM**. Al
  reconectar, el jugador loguea normal, y el server detecta que su cuenta tiene
  un match activo y **re-vincula la sesión al clon** en el estado que tenga en
  ese momento (sus compras + las de los aliados + muertes / posición). No vuelve
  a town con el personaje real.
- Lo único que se persiste en BD del modo son las **advertencias / bloqueos del
  matchmaking**. Nada del estado de partida.

### Al entrar a la partida (setup automático)

Se juega con un **clon efímero** del personaje (ver decisión #6): un `Character`
**desprendido** (construido con `new`, nunca metido en el change-tracker de EF) +
un **flag transitorio por jugador** que hace `SaveProgressAsync` un no-op
mientras dura el match (misma lógica que `Account.IsTemplate` pero en RAM, sin
tocar la cuenta real). El personaje real **no se toca en ningún momento** — la
sesión se aleja de él, no lo edita. Sobre el clon se aplica:

- **Stats completos e idénticos por clase**: el clon entra con el reparto full de
  puntos de nivel 400, **igual para todos los jugadores de esa clase** (nadie
  arrastra el build de su personaje real). La distribución exacta STR/AGI/VIT/
  ENE/CMD por clase se afina en balanceo.
- **Sin inventario heredado**: se le entrega solo un **arma básica acorde a su
  clase** (ej. espada básica para Dark Knight, staff básico para Dark Wizard) —
  **sin armadura, alas ni accesorios**.
- **Árbol de Master Skill Tree** → **vacío**, **Master Level = 1**.
- El jugador elige un **loadout de 4 a 6 skills activas** (excluyendo buffs) de
  entre todas las que su clase ya tenía **desbloqueadas a nivel 400** en su
  progreso real.

### Fin de partida / victoria

- Se gana **destruyendo el nexo del equipo rival** (estructura con HP; reutiliza
  la lógica de **puertas de Castle Siege**, ver *Fase 2 — Base enemiga*).
- **Duración indefinida**: no hay timer, la partida dura hasta que cae un nexo.
- *Provisional para desarrollo:* mientras la estructura del nexo no exista, el
  match se corta con un **comando de GM** (`/mobaend <equipo>`) o un tope de
  kills configurable. No bloquea el bloque 1.

### Progresión dentro de la partida

- **Master Level** sube de 1 hasta **~30** (tope práctico) durante el match,
  ganando **5 Master Point por Master Level**.
- **Objetivo de ritmo:** un jugador con buen desempeño (kills + *last hit* de
  mobs + objetivos) llega a **~ML 30 en el minuto 30–40**. Un jugador flojo
  llega bastante menos. La curva de **Master EXP** (valor por kill, por last-hit,
  por objetivo, y EXP requerida por nivel) es **config afinable** y se calibra
  jugando.
- **No** se espera ni se busca completar el árbol de Master Skill Tree en una
  partida — la idea es que cada match resulte en una **build parcial /
  estratégica** distinta.
- **Master EXP** se gana matando **mobs de oleada**, **jugadores rivales**, y por
  **objetivos** (torretas / nexo, cuando lleguemos a Fase 2).

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

- El **clon se descarta** (nunca se persistió). El personaje real vuelve a
  cargarse **exactamente como estaba**: inventario, Master Skill Tree y Master
  Level intactos.
- Todo el **oro, ítems comprados y progreso de Master Level** de la partida se
  pierde con el clon.

### Duración y timers

- La partida es de **duración indefinida** (termina al caer un nexo, ver *Fin de
  partida*).
- El **tiempo de respawn tras morir** escala con el **tiempo transcurrido de
  partida** (mecánica estándar anti-snowball).

### Sistema anti-AFK / desconexión

Cuando un personaje (el clon del jugador) queda **sin uso** — sin input del
dueño, ya sea porque se desconectó o porque lo dejó quieto ("soltado") — se
aplican dos etapas **escalonadas**, no simultáneas:

1. **A los 15 s sin uso** → el personaje queda **disponible para que cualquier
   aliado lo tome**. El primer input de un compañero gana el control (regla
   "el primer input gana"). El personaje sigue en el lugar donde quedó.
2. **Si pasan 5 s más sin uso (20 s totales)** → recién ahí el personaje se
   **auto-retorna a la base del equipo** (recall automático a la zona segura de
   spawn).

El retraso extra entre las dos etapas es deliberado: le da al dueño original una
ventana para **reconectarse y retomar el personaje en el punto donde quedó**,
antes de que el servidor ya lo haya movido a base.

### Pendiente de definir en desarrollo (no bloqueante para empezar)

- Balance específico de **daño / cooldown por clase** para este modo.
- **Distribución exacta de puntos** STR/AGI/VIT/ENE/CMD del baseline por clase.
- Detalles finos del anti-AFK: qué cuenta exactamente como "uso" (mover, atacar,
  castear), si el takeover por un aliado es exclusivo o cooperativo, y qué pasa
  al reconectar si un aliado ya tomó el personaje.
- Si se **restringe o no** tener clases duplicadas en el mismo equipo.

---

## Fase 2 — Oleadas de mobs (push de línea)

- **Mobs de oleada** con **ruta de waypoints fija** que avanzan por un carril,
  atacando solo si detectan enemigos en el camino (reutilizando la **IA de
  agresividad** existente de OpenMU).

### IA de creeps de oleada — targeting estilo LoL (diseño cerrado)

Cada creep, jugador (clon), torreta y nexo pertenece a un **bando** (Azul / Rojo).
Un creep **marcha su carril** (W1) mientras no tenga objetivo válido; cuando lo
tiene, ataca; al perderlo (muere / sale de rango / termina la persecución),
reanuda la marcha.

**Prioridad de adquisición** (de un objetivo nuevo, de mayor a menor):

1. **Campeón enemigo que está atacando a un aliado** (campeón o creep) cerca —
   *evento de aggro, temporal* (ver abajo).
2. **Algo enemigo que está atacando a un creep aliado** cerca → el creep se suma
   al **foco de fuego** (regla #5/#6 de LoL, incluida en v1). Si son varios, el
   más cercano de ellos.
3. **Creep enemigo más cercano** en rango de adquisición.
4. **Campeón enemigo más cercano** en rango de adquisición.
5. **Estructura enemiga más cercana** (torreta → nexo).

**Reglas de estado:**

- **Lock:** un creep que ya está atacando algo **no cambia de objetivo** por ver
  aparecer algo de mayor prioridad. Solo re-adquiere cuando su objetivo muere o
  sale de rango. **Excepción:** el evento de aggro de campeón (#1) **sí
  interrumpe** el ataque actual.
- **Lock sobre estructura:** cuando un creep empieza a pegarle a una torreta o al
  nexo, se **queda ahí** hasta que la estructura muera o el creep salga de rango,
  ignorando creeps y campeones enemigos que lleguen.
- **Evento de aggro de campeón (#1):** se dispara cuando un campeón enemigo
  ejecuta **cualquier acción dañina** (auto, skill, DoT) sobre un aliado del creep
  (campeón o creep) dentro del rango del creep. Dura **3 s** desde la última
  acción dañina de ese campeón; al expirar, el creep **revierte a la prioridad
  normal** (creep más cercano primero, **no** el campeón).
- **Persecución (leash):** el creep persigue a su objetivo hasta **10 tiles**
  fuera de su carril; si el objetivo se aleja más, abandona y vuelve al carril.
  (v1 sin invulnerabilidad ni boost de velocidad en el regreso, a diferencia de
  LoL.)
- **Creeps se atacan entre sí:** creep Azul vs creep Rojo se pelean al cruzarse
  (choque de oleada en el punto medio del carril), como en LoL.
- **Rango de adquisición:** rango de ataque del creep **+ 6 tiles** (para caminar
  hacia el objetivo antes de estar en rango).
- **Cadencia de re-targeting:** la IA re-evalúa su objetivo cada **~250 ms** (no
  cada frame): suficiente para reaccionar sin verse errático ni costar CPU.

**Diferido a después de v1** (impacto bajo): sub-prioridades finas #3/#4 de LoL
("prioriza a quien me ataca a mí"), leash con invulnerabilidad + boost, tipos de
minion (melee / caster / cañón / super), y requisito de visión (relevante recién
con arbustos / jungla en Fase 3).
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
| 4 | Alcance completo de la Fase 1 (moba básico) | Entrada solo con **cuenta nivel 400 + Master Skill activo**; al entrar: **arma básica por clase** sin equipo, **Master Tree a vacío / Master Level 1**, **loadout de 4–6 skills activas** de las ya desbloqueadas a 400. Progresión: Master Level 1→~30 (**5 MP/nivel**), Master EXP por mobs/kills/objetivos. **Oro de partida** (separado del Zen) por farmeo *last-hit*, subir de Master Level, kills, **shutdown gold** y **renta pasiva mayor para el que pierde**. **Tienda sin requisito de nivel**, **3 tiers** con precio por **fórmula de stats totales**, **buffs se compran con oro** (no van en el loadout), ítems **instance-bound**. Al salir se descarta todo lo de la partida. **Respawn escala con la duración** del match. Formato **5v5** en **instancias simultáneas** del mapa #200. | 2026-08-29 |
| 5 | Entrada y matchmaking de la Fase 1 | **NPC de cola en Lorencia** con 3 opciones: (1) solo, (2) party de 2–4, (3) equipo de 5. Pools: 1+2 se combinan hasta armar equipos de 5 (party siempre junto); 3 es pool aparte, solo 5-preformados vs 5-preformados. **Ready-check** al completar los 10, ventana **10 s**; rechazo/timeout → la partida no arranca, el jugador se reemplaza y recibe **1 advertencia**. **3 advertencias → bloqueo 1 h** (cola y party); reincidencia tras cumplir → bloqueo escala (1 h → 2 h → …); aceptar tras cumplir un bloqueo resetea advertencias a 0. Advertencias/bloqueos persistidos en BD. | 2026-08-29 |
| 6 | Aislamiento del personaje real durante la partida | **Clon efímero por partida** (Opción B). Impl: `Character` **desprendido** (`new`, nunca en el change-tracker de EF) + **flag transitorio por jugador** que hace `SaveProgressAsync` un no-op durante el match (misma idea que `Account.IsTemplate`, en RAM). El clon + estado de partida los posee un **objeto de match server-side** (uno por partida, estilo `MiniGameContext`), no la conexión — sobrevive DC del jugador; al reconectar se re-vincula la sesión al clon en RAM. El personaje real jamás se muta ni se persiste el clon. | 2026-08-29 |
| 7 | Condición de victoria de la Fase 1 | Se gana **destruyendo el nexo rival** (estructura con HP, lógica de puertas de Castle Siege). **Sin timer**, duración indefinida. Ritmo de progresión objetivo: **~Master Level 30 en el minuto 30–40** para un jugador con buen desempeño; curva de Master EXP (kill / last-hit / objetivo / EXP por nivel) queda como **config afinable**. Provisional en dev hasta tener la estructura: corte por comando de GM o tope de kills. | 2026-08-29 |

### Notas de diseño relacionadas

- **Límites de la arena:** no dependemos de la forma nativa del mapa. Se recorta
  la zona jugable con (A) un **plugin de borde** en el servidor que rebota al
  jugador si sale del polígono de arena + mensaje, y opcionalmente (B) "hornear"
  el `TerrainData` del mapa para que oleadas/torretas/spawns respeten el mismo
  límite (Fase 2), y (C) editar el `.att` del cliente para el muro visual
  (pulido posterior). Los límites viven en config → ajustables sin recompilar.
  Consecuencia: se puede tallar un carril alargado dentro de *cualquier* mapa,
  así que la elección de mapa pesa más por ambiente/assets que por geometría.
