# GAMEDESIGN — Modo de juego custom sobre OpenMU (S6E3)

> Documento de diseño. Contexto trasladado desde una conversación previa para
> centralizar el trabajo en este repositorio. A partir de ahora el diseño y el
> desarrollo avanzan únicamente aquí.

> **Regla de proceso — servidor:** siempre que haya que reiniciar o levantar el
> servidor (tras cambios de código, recompilar, etc.), lo hace Claude, no el
> usuario: cerrar el proceso `MUnique.OpenMU.Startup`, compilar el Startup en
> Debug y levantarlo con `dotnet run --no-build --project
> src\Startup\MUnique.OpenMU.Startup.csproj -c Debug -- -autostart
> -resolveIp:loopback` (en segundo plano), y avisar cuando esté listo.

> **Regla de proceso (desde 2026-09-30):** si el usuario pide un prompt para
> arrancar una tarea en una conversación nueva, este documento tiene que estar
> al día con todo lo decidido/hecho hasta ese momento **antes** de dar el
> prompt — si hay cambios de la conversación actual sin registrar acá,
> actualizar este .md primero, recién después entregar el prompt. Y el prompt
> en sí siempre le tiene que decir a la conversación nueva que lea este
> GAMEDESIGN.md como guía / fuente de verdad del proyecto.

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

**Implementado (2026-09-30, `MobaGold.cs`)** — antes era solo diseño, no existía
ningún código que otorgara oro (el clon arrancaba y se quedaba en 0 Zen toda la
partida). Fuentes de oro:

- **Farmeo de mobs / oleadas** (recompensa individual, tipo *last hit* de LoL):
  20 oro al que hace el último golpe, 10 a los aliados cercanos sin last-hit.
- Cada vez que **sube de nivel de campeón** (recompensa de progresión general):
  +40 oro por nivel, además del punto de skill.
- **Bono por matar a un jugador rival**: 150 + 8 por nivel de la víctima.
- **Shutdown gold**: desde la 3ª muerte seguida de un rival sin morir él mismo,
  +35 oro extra por cada racha adicional (mecánica anti-snowball).
- **Ingreso pasivo de oro por tiempo** transcurrido (10 cada 5 s), **×1,5 para
  quien va 2+ niveles detrás del líder de la partida** (anti-snowball
  adicional, mismo umbral que el catch-up de EXP).
- El bonus de ítem "Zen tras matar monstruo" (`Stats.MoneyAmountRate`, opción
  excelente de armadura) multiplica **todo** el oro que gana ese campeón, no
  solo el de farmeo.

Números de primera pasada — con 1 oleada de 6 creeps cada 48 s compartida entre
5 jugadores por equipo, un jugador promedio ronda ~11.000 Zen en 40 minutos
(un par de piezas T1/T2), y uno bien farmeado ~18.000-22.000 (T2 completo +
alguna pieza T3) — a validar y afinar con `/mobabotfight`.

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

### Sistema de tienda de ítems (NPC vendedor)

**Estructura general:**

- Un **único NPC vendedor** (no uno por raza ni por categoría) atiende a todos
  los jugadores en la base.
- Al hablarle, se despliega un **menú de categorías** mostradas por **nombre**
  (ej. "Armas", "Sets", "Alas", "Accesorios", "Buffs/Consumibles"), nunca
  numeradas.
- Al elegir una categoría, se abre la **ventana estándar de vendedor** (la del
  protocolo/cliente ya existente), mostrando **solo** los ítems de esa categoría.
- Los ítems de cada categoría se **filtran automáticamente por raza/clase** del
  personaje que abrió el menú — cada jugador solo ve lo que su clase puede usar,
  sin vendedores separados por raza.
- El jugador puede cerrar la ventana y volver a hablarle al NPC para elegir otra
  categoría.
- Cada categoría puede contener ítems de **varios tiers** (T1/T2/T3, ver *Tienda
  de ítems*).
- Es parte de la **Fase 1** y vive en la **instancia del match**: todo lo
  comprado es **instance-bound**.
- **Se edita el cliente (MuMain):** el protocolo S6 no trae un menú de texto
  servidor→cliente ni precios enviados por el servidor, y las alternativas sin
  tocar el cliente (ítems-ícono, varios NPCs, tiendas personales) se descartaron
  por verse peor. Decisión del 2026-09-23 (ver decisión #8).

#### Diseño implementado (2026-09-23)

- **Vendedor:** **Hanzo el herrero** (NPC 251), uno en la base de cada equipo
  (azul ~(112,57), rojo ~(112,208), provisional). Aparece al entrar el primer
  clon a la arena o con `/mobabotfight`. El Hanzo de Lorencia no cambia.
- **Menú de categorías:** la **ventana nativa de diálogo de NPC** de S6 (la de
  las quests) con un modo nuevo en que título, texto y opciones vienen del
  servidor. Opciones por nombre, sin numerar: *Armas · Sets · Alas ·
  Accesorios · Buffs / Consumibles*. Reutilizable para otros NPCs (p. ej. el de
  cola de Lorencia).
- **Ventana de tienda:** la ventana de vendedor nativa, con solo la categoría
  elegida, filtrada por **familia de clase** del catálogo + clase habilitada
  del ítem, ordenada T1 → T2 → T3 (grilla 8×15).
- **Precios del servidor:** el cliente recibe la tabla de precios al abrir la
  tienda y el tooltip muestra ese precio (compra) o el 70 % (venta) en vez del
  precio nativo. El precio se identifica por tipo + nivel + nivel de opción +
  luck + nº de opciones excelentes, así que sigue al ítem en el inventario.
- **Moneda:** el **Zen del clon** (arranca en 0 y se descarta con el clon).
- **Reventa:** se puede **vender** al vendedor por el **70 %** del precio.
  **No** se puede tirar al suelo ni tradear entre campeones.
- **Sin requisitos de stats/nivel** para equipar dentro del match (servidor y
  cliente): solo decide la clase.
- **Tiers** (actualizado 2026-09-30, se aplican a cada ítem del catálogo,
  limitados al nivel máximo del ítem, p. ej. +4 en anillos): cada tier ahora
  también elige **cuáles** de las 6 opciones excelentes posibles entrega, por
  prioridad fija (`MobaShop.ExcellentPickPriority`, no al azar): primero daño
  puro / %HP, después mitigación / golpe excelente, después velocidad de
  ataque / reflejo, y al final vida-maná-al-matar y el bono de Zen — así un
  ítem T1/T2 ya tiene identidad de build, no una mezcla arbitraria.
  - **T1:** +13, luck, opción +8, **3** opciones excelentes (las de más
    prioridad).
  - **T2:** +14, luck, opción +12, **4** opciones excelentes, skill.
  - **T3:** +15, luck, opción +16, **6 (todas)**, skill — el tier tope
    "iguala" a cualquier build, ya no hay tradeoff de cuáles elegir.
- **Fórmula de precio** (sin cambios, nunca a mano):
  `puntos = (drop level + 10) × (1 + nivel × 0,1) + opción × 8 + luck 15 +
  excelentes × 25`; equipo = puntos × 10 Zen (redondeado a 10), consumibles =
  puntos × 2 Zen por unidad. El *drop level* es el grado del ítem en MU (sus
  stats base escalan con él). Con los tiers nuevos (más nivel + opciones),
  los precios resultantes suben y se acercan entre tiers: T1 ≈ 1.750–5.400
  Zen, T2 ≈ 2.100–6.000, T3 ≈ 2.700–6.700 por pieza (loadout T1 completo,
  ~9 piezas, ≈ 16.000-25.000; T3 completo ≈ 25.000-42.000 — con la economía
  de oro actual (~11.000 Zen/40 min promedio, ~18.000-22.000 bien farmeado)
  un T3 completo queda como objetivo aspiracional, no de cada partida — a
  revalidar jugando. Pociones sin cambios, 120–770 por stack de 3.
- **Catálogo inicial (provisional, se afina jugando):** por familia, un arma,
  un set completo y unas alas por tier; accesorios (anillos/pendientes) y
  consumibles (pociones, Ale, Potion of Bless/Soul) comunes a todos.
- **Drop de ítems de los minions (implementado 2026-09-30, `MobaCreepDrops.cs`):**
  cada creep que muere tiene **8 % de chance** de dejar tirado un ítem del
  catálogo (arma/set/alas/accesorio), filtrado por la clase de quien hizo el
  último golpe — prioridad de recogida para él y los aliados cercanos que
  compartieron la EXP/oro de esa muerte, libre para cualquiera a los 10 s.
  - **Solo caen ítems del tier de la fase actual de la partida** (nunca T3 en
    fase T1). La fase avanza cuando **2 campeones de cada equipo** llegan al
    nivel de campeón 11 (→ fase T2) o 21 (→ fase T3) — los mismos umbrales
    que separan los tramos rápido/meseta/difícil de la curva de EXP, nunca
    retrocede.
  - **Nivel del ítem**: el nivel garantizado del tier menos un offset —
    offset 0 con 10 %, -1 con 20 %, -2 con 30 %, -3 con 25 %, -4 con 15 %.
  - **Cantidad de opciones excelentes**: todas (el máximo del tier) con
    10 %, 3 con 20 %, 2 con 30 %, 1 con 25 %, 0 con 15 % — mismas
    prioridades de elección (`ExcellentPickPriority`) que la tienda.
  - Los dos rolls son independientes entre sí. Números de primera pasada;
    a afinar jugando / con `/mobabotfight`.
- **Protocolo propio** (canal MOBA `0xD5`):
  - `C2 D5 06` servidor→cliente: menú (id, título, texto, opciones UTF-8).
  - `C1 D5 07` cliente→servidor: opción elegida (id de menú, índice).
  - `C2 D5 08` servidor→cliente: tabla de precios + % de reventa.

#### Pendiente / siguiente paso

- **Los ítems todavía no cambian el combate MOBA (confirmado en código,
  2026-09-28):** el pipeline de daño/defensa (`AttackableExtensions.cs` líneas
  ~86-337) para un atacante `IsMobaClone` ignora por completo el equipo — el
  daño sale 100 % de `MobaSkillDamage.GetSkillBaseDamage/GetComboBonus` (tabla
  por skill + stat primario invertido), la mitigación 100 % de
  `MobaDefense.MitigationOf` (VIT invertida), y crit/vamp/daño especial de
  `MobaCombatStats` / `MobaVampAndSpecial` (AGI invertida / constante por
  clase). **Actualización 2026-09-30: ya conectado** — ver la sección
  *Balance del MOBA* más abajo para el detalle completo (arquitectura A/B,
  economía de oro, tiers de tienda, drop de creeps) y lo que sigue pendiente.
- Buffs reales de ataque/defensa comprables (hoy solo Ale y Potion of
  Bless/Soul) y calibrar el oro que se gana contra estos precios.
- Posición exacta de los vendedores y spawn por instancia cuando exista el
  ciclo real de partida.

### Balance del MOBA — sesión 2026-09-30 (implementado + pendiente)

Pasada de balance completa, en la rama `moba/phase1`. Commits (en orden):
`359d765f0` doc pendiente ítems, `c8b209775` margen de daño/mitigación +
curva EXP 3 tramos, `05896df66` arquitectura A/B daño/crit/mitigación,
`6200aa733` topes de velocidad/HP/maná, `9f936d154` economía de oro,
`211f49ed9` tiers de tienda con prioridad temática, `669b83a40` drop de
creeps por fase, más el cambio de pesos A/B a 60/40 (sin commitear al
cerrar esta nota — hacerlo antes de seguir). Toda la suite de tests pasa
(796/797 — la única falla, `DescriptionMatchesWhatThePlugInRequires`, es
**preexistente**, no relacionada a este trabajo, confirmado con
`git diff` contra el commit anterior a esta sesión; queda anotada aparte).

#### Arquitectura A/B (stats vs ítems) — implementado

Daño (`MobaSkillDamage.BlendedFraction`), mitigación
(`MobaDefense.FinalMitigationOf`) y crítico
(`MobaCombatStats.FinalCritChanceOf`) mezclan un término A (el build de
stats, la fórmula que ya existía) con un término B (equivalente por
ítems), `Final = A × StatWeight + B × (1 − StatWeight)`.
**`StatWeight` = 0,60 / 0,40 para ítems** (constante en los 3 archivos,
bajado de 0,75/0,25 el 2026-09-30 a pedido del usuario — dar más peso real
a los ítems). Techos duros ya en código: excelente 50 %, crítico final
60 %, mitigación final 70 % (heredado de `MaxMitigation`), velocidad de
ataque ≤70, HP/Maná ≤ curva de nivel × 1,20 — todo en `MobaItemCaps.cs`
salvo crítico/excelente que están inline. Reflejo de daño (`Stats.DamageReflection`)
ya funcionaba nativo en `Player.HitAsync` — no hizo falta cablearlo, solo
confirmar que los ítems T2+/T3 lo pueden llevar como opción excelente.

#### Economía de oro — implementado, con curva por fase (2026-09-29)

`MobaGold.cs`. Fuentes base (planas, por evento):

| Fuente | Valor base | ¿Escala con la fase? |
|---|---|---|
| Last-hit de creep | 20 | sí |
| Proximidad de creep | 10 | sí |
| Matar campeón | 150 + 8 × nivel de la víctima | **no** (ya escala con el nivel) |
| Shutdown (racha 3+) | +35 por racha adicional | **no** |
| Asistencia | 60 | sí |
| Subir de nivel de campeón | 40 | sí |
| Goteo pasivo | 10 cada 5 s (×1,5 si vas 2+ niveles detrás del líder) | sí |

**Curva por fase (decidida con el usuario el 2026-09-29, opción "más
agresiva"):** multiplicador según `MobaMatchPhase.Current` —
**T1 ×1,0 / T2 ×1,5 / T3 ×2,0** (`MobaGold.PhaseMultiplierOf`), aplicado en
`GrantAsync` (`scaleByPhase`, por defecto sí; kill/shutdown lo desactivan).
Se combina multiplicando con el bono de ítem de Zen (`MoneyAmountRate`) y el
catch-up. Las estimaciones anteriores (~11.000 Zen/40 min plano) no aplican
más: **hay que re-medir con `/mobabotfight`** y afinar los factores.

#### Segunda ventana de tienda — **descartada como ventana; resuelta con páginas de menú** (2026-09-29)

Se revisó el cliente MuMain (`WSclient.cpp`, `ReceiveTalk`, packet `0x30`): los
valores 0 (`Merchant`) y 1 (`Merchant1`) caen **ambos** en el `default:` y abren
la **misma** ventana `INTERFACE_NPCSHOP` — no existe una segunda ventana de
vendedor distinta en el cliente. Usar `Merchant1` no daría espacio extra (y
hacer una ventana nueva sería trabajo de UI en C++ para nada).

Como lo que se buscaba era **duplicar el espacio de variantes**, se resolvió sin
tocar el cliente: el menú de categorías del NPC (server-driven, hasta 20
opciones, hoy 7) ahora tiene **páginas extra**, cada una con su propia grilla
8×15: `Sets (recursos)` (mezcla de vida/maná/Zen de los sets T1 y T2) y
`Alas (utilidad)` (wing option de reflejo/maná de las alas T2 y T3). Se agregan
como `MobaShopCategory.SetsSustain` / `WingsUtility` (el orden del enum es el
orden del menú). Si hicieran falta más variantes, se agrega otra página igual.

#### Variantes múltiples de ítems por clase/tier — implementado (2026-09-29)

**Helper de curación:** `tests/.../MobaShopCandidatesReport.cs` (`[Explicit]`, no
corre con la suite). Carga la `GameConfiguration` real y, por familia/tier, lista
los candidatos filtrados por `QualifiedCharacters` y `DropLevel` cercano al ítem
del catálogo; vuelca a `%TEMP%\moba-shop-candidates.txt`. Correr con
`dotnet test --filter "FullyQualifiedName~MobaShopCandidatesReport"`.

**Hallazgos del reporte (datos reales, no supuestos):**
- **Sets completos alternativos: no existen** en el mismo tramo de grado — casi
  todos los demás sets son piezas sueltas (p. ej. Eclipse solo armadura/pantalón/
  botas). No se puede variar un set por "otro set".
- **T3 ya trae las 6 excelentes** → cambiar la mezcla no cambia nada. Las
  variantes de mezcla solo tienen sentido en T1 (3 de 6) y T2 (4 de 6).
- **Las alas no tienen opciones excelentes**; tienen 1 *wing option* (una de
  3-4: ignorar defensa, reflejo total, vida total, maná total / vida máx.,
  maná máx.). Las alas de 1ª generación (Heaven, Satan, Elf, Curse) **no**
  tienen wing option → no hay variante posible en T1 (se ofrece una sola).
- Armas: sí hay 1-2 armas nativas alternativas en T3 para 5 de 7 familias.

**Diseño:** `MobaShopVariant` (Standard / Aggressive / Sustain) en cada
`MobaShopEntry`. La pieza y la *cantidad* de opciones las fija el tier (mismo
precio); la variante decide *cuáles* (`MobaShop.ExcellentPickPriority` por
variante; alas: `WingOptionPreference`).
- **Armas:** T1 y T2 → 3 mezclas (estándar dmg / **Aggressive** crítico+velocidad
  +vida-al-matar / **Sustain** vida y maná al matar + crítico). T3 → 1 mezcla +
  **arma alternativa real** para Wizard (Chromatic Staff), Knight (Knight Blade),
  Elf (Sylph Wind Bow), MG (Dark Reign Blade), DL (Soleil Scepter). Summoner y
  RF no tienen alternativa nativa.
- **Sets:** página "Sets": T1 y T2 → 2 mezclas (estándar tanque / **Aggressive**
  reflejo+defensa+vida+maná); T3 → 1 (T3 lleva las 6 opciones, no hay mezcla que
  variar). Página **"Sets (recursos)"**: 3ª mezcla **Sustain** (vida/maná/Zen) de
  T1 y T2. Todo junto no entra en una grilla (22 celdas por set, 120 de grilla).
- **Alas:** página "Alas": T2/T3 → 2 (Aggressive = ignorar defensa, Sustain =
  vida). Página **"Alas (utilidad)"**: 3ª mezcla (maná máx. / reflejo total) de
  T2/T3. Capas de DL/RF también (tienen wing option). T1 de 1ª gen → 1.
- `BuildCategoryItems` **deduplica** variantes que resuelven al mismo ítem.
- Un ítem con wing option cuenta como 1 opción excelente para el **precio** (+25)
  y para la **clave de precio del cliente** (el serializador lo mete en el byte
  de excelentes).
- **Drops de creeps:** se elige primero el ítem y luego una de sus variantes
  al azar (un ítem con 3 mezclas no le gana a uno con 1).
- Tests: `VariantsCarryDifferentOptionsAtSamePrice` (variantes distintas y mismo
  precio), más los existentes de grilla/serialización. 803/804 (la falla es la
  preexistente `DescriptionMatchesWhatThePlugInRequires`).
- Quirk preexistente anotado: las capas de DL/RF usan el mismo ítem en T1 y T2,
  y `MobaItemPower` resuelve el tier por (grupo, número) → cuenta como T1.

#### Pendiente para la próxima conversación (actualizado 2026-09-29)

Hecho en esta tanda: pesos A/B 60/40 commiteados, helper de candidatos, variantes
de armas/sets/alas, curva de oro ×1,0/×1,5/×2,0 (provisional), páginas extra de
menú. Decisiones nuevas del usuario y lo que queda:

1. **Alas = full option, sin variantes.** Las alas dejan de ser "variantes": se
   venden **full option** (nivel máximo, luck, opción +4 y su wing option). Hay que
   quitar `WingsUtility` ("Alas (utilidad)") y las mezclas Aggressive/Sustain de
   `Wing(...)` en `MobaShopCatalog`, dejando 1 entrada por ala/tier; ajustar
   `VariantsCarryDifferentOptionsAtSamePrice` y `MobaShopTests`. Decidir cuál
   wing option lleva cada ala "full" (una sola por ala; sugerencia: la de
   Maximum Health si existe, si no ignorar defensa).
2. **Sin espacio vacío por gusto.** No se justifican páginas de menú extra si no
   hay más variantes reales que meter. Revisar `Sets (recursos)`: tiene contenido
   real (mezcla Sustain de T1/T2), pero verificar cuántas celdas ocupa cada página
   y **fusionar** páginas que quepan juntas en una grilla de 120 celdas; no
   dejar páginas casi vacías. Sets T3 sigue en 1 variante (lleva las 6 opciones).
3. **Medir la economía de oro y definir el 100 %.** Con `/mobabotfight` (o un
   simulador/test de la tasa de ingreso si no se puede jugar) medir oro
   acumulado por minuto para un jugador promedio y uno bien farmeado, por fase.
   **Sugerencia de objetivo a validar:** T1 completo (~9 piezas, ≈16.000-25.000
   Zen) hacia el minuto 12-15; T2 completo hacia el 25-30 para un jugador
   promedio; T3 completo (≈25.000-42.000) solo alcanzable para el que va bien
   (min 35-40) — no de cada partida. Si la medición queda por debajo, subir
   los factores de fase (×1,5/×2,0 son el punto de partida, no el definitivo);
   si el T3 completo llega antes del minuto 30 en promedio, bajarlos. Documentar
   los números medidos y los factores finales acá.
4. **Validar todo con `/mobabotfight`.** Requiere servidor + BD locales (Postgres
   17 corriendo; falta inicializar la BD del servidor y levantarlo) y un cliente
   MuMain con personaje GM. Es un paso interactivo: el usuario entra al juego,
   corre el comando y pasa el log/observaciones.
5. **Nota:** el cliente MuMain abre `Merchant` y `Merchant1` con la misma ventana
   (`ReceiveTalk` en `WSclient.cpp`, packet `0x30`, ambos en `default:`) — no hay
   segunda ventana de vendedor; el espacio extra se hace con opciones del menú.
6. Quirk preexistente: las capas de DL/RF usan el mismo ítem en T1 y T2 y
   `MobaItemPower` resuelve el tier por (grupo, número) → cuentan como T1.

#### Avance de la sesión 2026-09-29 (tareas 1-3 del pendiente)

1. **Alas full option, sin variantes — hecho.** `Wing(...)` deja 1 entrada por
   ala/tier; se vende siempre con nivel máximo del ítem, luck, opción +4 y su wing
   option (Maximum Health si existe, si no ignorar defensa). Se quitó
   `WingsUtility`, la variante `Utility` y sus preferencias. Test nuevo
   `WingsAreFullOptionWithoutVariants`; `VariantsCarryDifferentOptionsAtSamePrice`
   ahora solo mira armas. (T1 y T2 de las capas de DL/RF son el mismo ítem: la
   tienda ofrece uno solo, `BuildItems` deduplica.)
2. **Páginas del menú fusionadas — hecho (7 → 3).** Nuevo `MobaShopPage` +
   `MobaShopCatalog.Pages`; `MobaShop.BuildPageItems` empaqueta varias categorías en
   una grilla 8x15. Celdas ocupadas (máximo entre clases / mínimo):

   | Categoría | Celdas (min–máx por clase) |
   |---|---|
   | Armas | 14–65 |
   | Sets | 90–110 |
   | Sets (recursos) | 36–44 |
   | Alas | 12–42 |
   | Accesorios | 6 |
   | Consumibles | 14 |

   Antes: 7 páginas (Alas (utilidad) y Sets (recursos) casi vacías). Ahora:
   **Armas, Alas y Accesorios** (32–104) · **Sets** (90–110) · **Sets (recursos) y
   Buffs** (50–58). Test `EveryPageHasItemsAndFits` (sin overflow, ≥30 celdas).
   Fist Master tiene solo 32 celdas en la 1ª página porque su catálogo es corto
   (no hay más contenido que meter).
3. **Economía de oro — medida con simulador, propuesta pendiente de aprobación.**
   `MobaGoldEconomyReport.cs` (`[Explicit]`) replica las constantes reales de
   `MobaGold`/`MobaLevels` en ticks de 5 s (1 oleada de 6 creeps/48 s por equipo)
   con 3 perfiles (presencia en línea / % last-hit / kills·min / asistencias·min:
   promedio 60 %/20 %/0,25/0,30; bien farmeado 90 %/40 %/0,50/0,40; muy bien
   100 %/55 %/0,80/0,50 — **supuestos**, a calibrar con `/mobabotfight`).
   Costos reales del loadout completo (arma + 5 piezas + alas + anillo + colgante,
   promedio de las 7 familias): **T1 ≈ 22.500, T2 ≈ 30.300, T3 ≈ 40.900**
   (con alas full option). Mejora de tier = precio del nuevo − 70 % del anterior.
   Con los factores actuales ×1,0/×1,5/×2,0: promedio **T1 min 47,8; T2 y T3 no
   llegan en 60 min** (≈340-490 Zen/min); bien farmeado T1 min 35,5, T2 min 57 →
   **el oro actual está ~4× por debajo del objetivo**.
   **Hallazgo:** la fase de partida sale del *nivel* (T2 al nivel 11 ≈ **min 4**,
   T3 al 21 ≈ **min 12**), no del tiempo de compra; el ×2,0 rige desde el min 12 y
   los tiers de tienda no están alineados con esos umbrales.
   Búsqueda (K = escala global de todo el oro, más factores de fase) contra
   objetivos promedio T1 ~13,5 / T2 ~27,5 / sin T3 antes del 45; bien farmeado
   T3 ~37,5. Resultados en la conversación; ver "Pendiente" para la decisión.

4. **Servidor local para `/mobabotfight` — listo (2026-09-29).** La BD `openmu` ya
   estaba inicializada (mapa 200 "MOBA Arena", 20 cuentas de prueba): **no hace falta
   `-reinit`**. Se levanta con `dotnet build src\Startup\MUnique.OpenMU.Startup.csproj -c Debug`
   y `dotnet run --no-build --project src\Startup\MUnique.OpenMU.Startup.csproj -c Debug -- -autostart -resolveIp:loopback`
   (puertos 80 panel, 44406 cliente MuMain, 55901 game server). Cliente:
   `mu-main\outuild\windows-x64\src\Release\Main.exe` (compilado 2026-09-23 00:47,
   **antes** del último commit de mu-main `c492d5a3` de las 00:53 → si el menú
   server-driven de la tienda no aparece, recompilar con `cmake --build --preset windows-x64-release`).
   Pendiente: que el usuario entre con un personaje GM, corra `/mobabotfight` y pase el log.

#### Decisiones del usuario tras la sesión 2026-09-29 (a implementar en la próxima conversación)

Reemplazan / corrigen lo propuesto en "Avance de la sesión 2026-09-29":

- **Menú de tienda:** juntar **Consumibles + Accesorios + Alas en una sola página**,
  ordenados en la grilla: **arriba consumibles, en medio accesorios, abajo alas**
  (cada categoría empieza en fila propia). Las demás páginas (Armas, Sets, Sets
  (recursos)) se re-evalúan por celdas: fusionar solo si caben sin desorden.
- **El ritmo de niveles estaba mal:** subir a nivel 11 en ~4 min y a 21 en ~12 no es
  aceptable (un nivel debe tomar **~1 min o más**). Hay que **recalibrar la curva
  de EXP** (`MobaLevels.ExpToNext`, EXP pasiva/farmeo/kills) para que el **nivel
  máximo (30) se alcance hacia el min 35** de un jugador promedio (~1,1-1,2
  min/nivel). Las **fases** deben quedar: **T1 hasta ~min 8, T2 hasta ~min 24, T3
  desde ahí hasta el 40-50+ (lo que dure la partida)**; recalcular los umbrales de
  nivel de `MobaMatchPhase` (hoy 11/21) para que caigan en esos minutos con la
  curva nueva.
- **Objetivo de economía:** con **todas las fuentes de ingreso + revender el ítem
  anterior (70 %) para comprar el siguiente**, un jugador con compras **perfectas**
  debe lograr **T3 completo al min 40**. Con eso re-derivar K y los factores de fase
  (el simulador `MobaGoldEconomyReport` ya existe; ajustar su curva de EXP/fases a
  la nueva y volver a buscar).
- **Ítems dropeados por creeps se venden por 5 de oro** (precio de reventa fijo,
  no el 70 % de la fórmula).
- **Bug de cliente (resuelto 2026-09-29):** `Main.exe` crasheaba al iniciar (tras
  "Loading ok", antes del Login Scene). Causa: objetos compilados viejos tras cambiar
  el layout de `CNewUINPCDialogue` (commit `c492d5a3`, miembros `std::wstring`/`vector`
  nuevos) — el build incremental dejó TUs inconsistentes. Solución: `cmake --build
  --preset windows-x64-release --clean-first` (~10 min). Si vuelve a pasar tras
  tocar un header de NewUI, hacer build limpio.

#### Sesión 2026-09-29 (2ª tanda) — hecho + propuestas pendientes de aprobación

**Hecho:** cliente recompilado (crash resuelto); página de tienda nueva
(commit `5844ffc09`): `Consumibles, Accesorios y Alas` (arriba consumibles, en
medio accesorios, abajo alas, cada categoría en fila propia; 32–62 celdas) ·
`Armas y Sets (recursos)` (50–105 celdas) · `Sets` (90–110). Test
`EveryPageHasItemsAndFits` ahora también verifica que las categorías no se
mezclen en filas.

**Decisiones nuevas del usuario (aún sin implementar):**
- Brecha de precio de **150 % entre tiers**: T2 = 2,5 × T1, T3 = 6,25 × T1
  (ej. arma 100 / 250 / 625). El oro recibido hasta el nivel 8 debe alcanzar
  para el **T1 completo**.
- **DL:** vender **Dark Horse (13/4)** y **Dark Raven (13/5)** a nivel máximo
  (50); ajustar el daño de Earthshake (caballo) y del ataque del cuervo para que
  entre ambos sean **25 % del daño total del DL**.
- Ítems de **utilidad** (Fenrir, Demon, Imp, Guardian Angel, Spirit of Guardian,
  Panda, Unicorn, Skeleton, Rudolf…): definir % de aporte sobre el total.
- **Recall** con la tecla **B** (canalizado, como LoL, se ve como teletransporte).

**Propuesta A — curva de EXP / fases** (simulador; fuentes de EXP sin tocar):
`ExpToNext(L) = 470 + 7 × (L − 1)` (lineal, 470 → 666; total a nivel 30 ≈ 16.500).
Umbrales de fase `T2LevelThreshold = 9`, `T3LevelThreshold = 22`.

| Perfil | Nv 2 | Nv 5 | Nv 9 (T2) | Nv 15 | Nv 22 (T3) | Nv 30 |
|---|---|---|---|---|---|---|
| Promedio | 1,1 | 4,2 | 8,5 | 15,5 | 24,2 | 34,8 |
| Bien farmeado | 1,0 | 3,8 | 7,8 | 14,1 | 21,9 | 31,4 |
| Flojo | 1,1 | 4,4 | 9,1 | 16,5 | 25,8 | 37,4 |

(min de partida en que se alcanza cada nivel; ≈ 1,1–1,4 min/nivel.)

**Propuesta B — precios y oro:** precio de un ítem = precio T1 de su mismo slot
× 2,5^(tier−1) (mismo precio para todas las variantes del tier). Loadout
completo (promedio de familias) anclado a **T1 = 6.300 · T2 = 15.750 ·
T3 = 39.375**; mejora T2 = 15.750 − 70 % × 6.300 = 11.340; mejora T3 = 39.375 −
70 % × 15.750 = 28.350. Oro: escala global **K = 2,7** sobre todas las fuentes
(last-hit 54, proximidad 27, kill 405 + 22/nivel, asistencia 160, subir nivel
110, shutdown 95, pasivo 27 por tick) y factores de fase **T1 ×1,0 / T2 ×1,0 /
T3 ×2,6**. Simulación con compras perfectas (T1 completo, luego mejoras
revendiendo al 70 %):

| Perfil | T1 completo | T2 completo | T3 completo | Oro hasta nivel 8 |
|---|---|---|---|---|
| Promedio | min 8,5 | min 22,8 | **min 39,9** | 6.339 (≥ 6.300 ✔) |
| Bien farmeado | 6,4 | 16,9 | 32,5 | 7.878 |
| Muy bien | 5,2 | 13,7 | 28,5 | 8.991 |
| Flojo | 10,9 | 26,6 | 47,7 | 5.294 |

Los ítems dropeados por creeps se venden por 5 de oro fijo.

#### Implementado 2026-09-30 (propuestas A y B aprobadas + ítems nuevos)

**Aprobado por el usuario y ya en código** (commits en `moba/phase1`):
- **Curva de EXP lineal** `470 + 7·(L−1)`, fases T2/T3 en nivel **9 / 22** (`MobaLevels`, `MobaMatchPhase`).
- **Oro ×2,7** sobre todas las fuentes (last-hit 54, proximidad 27, kill 405 + 22/nivel, asistencia 160,
  subir nivel 110, shutdown 95, pasivo 27) y factores de fase **×1,0 / ×1,0 / ×2,6** (`MobaGold`).
- **Precios ×2,5 por tier** (`MobaShop.BuildTierPrices`): T1 del slot = fórmula MU escalada para que el
  loadout T1 promedio cueste **6.300**; T2 = ×2,5; T3 = ×6,25 (loadout T2 ≈ 15.100, T3 ≈ 39.400; las capas de
  DL/RF son el mismo ítem en T1 y T2 y cuestan el precio T1). Las mascotas usan la escalera del colgante.
  Simulador (`MobaGoldEconomyReport`): promedio T1 min 8,5 · T2 min 22,1 · **T3 min 39,7** con compras perfectas.
- **Ítems de creep se venden por 5 de oro fijo** (marca `Item.StorePrice = 5`).
- **Tienda — 4 páginas:** `Consumibles, Accesorios y Alas` · `Armas y Sets (recursos)` · `Sets` ·
  `Mascotas y Escudos` (21–37 celdas: Elf/RF no tienen escudo).
- **Recall (tecla B):** canal de 5 s (`MobaRecall`), se corta al moverse, atacar, castear, recibir daño o morir;
  al terminar teletransporta con la animación de Teleport junto a la tienda del equipo. Cliente: barra de
  canalización (paquete `C1 07 D5 09`), B sigue abriendo el ranking de Gens fuera del MOBA.
- **Pergamino de teletransporte** (Town Portal Scroll 14/10, 900 de oro): al usarlo se elige un **minion aliado
  con clic en el mundo** (`C1 06 D5 0B`), mismo canal de 5 s, se consume al llegar, **enfriamiento 5 min**
  para todos los pergaminos (`MobaTeleport`).
- **Escudos y libros por tier** (categoría `Offhand`): Wizard Legendary/Grand Soul/Guardian, Knight
  Serpent/Dragon/Crimson Glory, MG Legendary/Dragon/Salamander, DL Skull/Tower/Cross, Summoner Sahamutt/
  Neil/Lagle. Elf y RF no tienen. `MobaItemPower`: para Elf/RF solo cuenta el arma en la fracción ofensiva.
- **Escudo activo «Égida»** (`MobaCastEffects`): al lanzar Soul Barrier / Defense / Greater Defense se suma una
  barrera de **30 s** = 10 / 15 / 20 % de la vida máxima según el tier del escudo (5 % sin escudo),
  mostrada con la burbuja de *Spell of Protection* (efecto 0x22) para no parecerse a Soul Barrier ni al
  verde del Elf.
- **Mascotas** (`MobaPets`, `MobaShopCatalog`): valores nativos reescritos en memoria a los del diseño —
  Guardian Angel 5 % def + 3 % vida · Imp 5 % daño · Uniria velocidad · Skeleton 4 % daño + 5 % EXP · Panda
  8 % EXP + 3 % def · Unicorn 8 % oro + 3 % def · Dinorant velocidad + 6 % daño + 5 % def · Demon 8 % daño +
  5 vel. ataque · Spirit of Guardian 8 % def + 3 % vida · Fenrir negro 10 % daño / azul 10 % def / dorado
  vida-maná-daño. Rudolf no se vende (sin efecto hasta que exista visión).
- **DL — Dark Horse (T2) y Dark Raven (T3), nivel 50:** Earthshake sube a (195, 48) y el cuervo ataca solo
  con su dueño (pega como el ataque básico × 0,37 cada 1,5 s). Modelo `MobaPets.DarkLordPetShare`:
  entre ambos ≈ **25 %** del daño sostenido del DL (Earthshake ≈ 15 %, cuervo ≈ 10 %; test
  `PetsAreAboutAQuarterOfDarkLordDamage`). **Ojo:** el cuervo ocupa el slot de mano derecha (1) y el
  cetro pasa a la izquierda, así que un DL elige entre cuervo y escudo. El caballo y las mascotas de utilidad
  comparten el único slot de mascota.
- **Opciones MOBA de las piezas** (`MobaItemTraits`, con líneas de tooltip en el cliente, paquete `C2 D5 0A`):
  arma = **anti-curación** 10/20/30 % (3 s tras cada golpe; recorta lifesteal, Second Wind, pociones y Heal),
  armadura y pantalón = **resistencia a CC** 5/10/15 % c/u, guantes y botas = **reducción de enfriamiento**
  3/6/10 % c/u (subido el 2026-09-30 a 6/12/20 % c/u), anillos = **oro por asistencia** 5/10/15 % c/u, colgante = **oro pasivo** 5/10/15 %.

**Pendiente:**
- **Ward y barredor** (slots 10/11 del HUD): Vision v1 ya existe en el servidor (`/ward`, `/sweep`); falta el HUD.
- **Validar todo con `/mobabotfight`** (paso interactivo del usuario) y re-medir oro/EXP reales contra el
  simulador; el cuervo/caballo y las opciones nuevas no se probaron en combate real.
- Ítem-idea sin implementar: oro por asistencia/pasivo a más piezas, más escudos activos por clase.

#### Correcciones del usuario tras el 2026-09-30 (a implementar en la próxima conversación)

Reemplazan lo anotado arriba sobre la Égida y los drops:
- **Égida mal entendida.** NO debe dispararse al lanzar Soul Barrier / Defense / Greater Defense (quitar
  16, 18 y 27 de `MobaCastEffects.AegisSkills`). Debe dispararse al lanzar **el skill que trae el escudo**
  (skill del ítem). Sigue siendo: barrera de **30 s** de 10 / 15 / 20 % de la vida máxima según el tier del
  escudo, con la burbuja de *Spell of Protection* (efecto 0x22). Sin escudo equipado no hay Égida.
- **Todos los escudos/libros de la tienda deben traer skill** (hoy muchos no tienen: Legendary 6/14, Grand
  Soul 6/15, Guardian 6/20, Elemental, Frost Barrier… y los libros del Summoner traen las suyas de
  maldición). Hay que definir/asignar un skill de escudo a **cada** ítem de la categoría `Offhand` (pendiente
  de decidir: skill nuevo dedicado vs reutilizar uno; debe poder lanzarse desde el cliente, tener cooldown
  y aparecer en la barra de skills).
- **Opciones adicionales MOBA en los escudos** (las de `MobaItemTraits`: anti-curación aplica a armas, acá
  se usan **resistencia a CC, reducción de cooldown, oro por asistencia/pasivo**): **T1 = 1 opción, T2 = 2
  opciones, T3 = full opciones** (todas las excelentes + todas las adicionales). Si una raza tiene más de un
  escudo, el T3 es el «full». Falta decidir cuáles opciones exactas y su tamaño por tier, y agregar sus
  líneas de tooltip en el cliente.
- **Drops de los creeps:** ahora dropean **solo armas, sets y escudos** (quitar alas y accesorios de
  `MobaCreepDrops`). Los **escudos dropeados** llevan opciones adicionales al azar: **1 opción 70 %, 2
  opciones 30 %**.
- **La revisión/validación de todo (`/mobabotfight`, medir oro/EXP, probar recall, teletransporte, cuervo,
  Égida, opciones) queda para el final**, después de implementar lo de arriba.

#### Sesión 2026-09-30 (3ª tanda) — Égida, skill de escudos, drops (hecho)

- **Égida corregida:** ya no se dispara con Soul Barrier / Defense / Greater Defense. Se dispara al lanzar
  **el skill del ítem de la mano izquierda** (`MobaShieldSkills.IsEquippedOffhandSkill`): barrera de 30 s de
  10 / 15 / 20 % de la vida máxima según el tier del escudo, burbuja Spell of Protection (0x22). Sin escudo
  equipado no hay Égida (`AegisFractionOf(0) = 0`; además el skill del escudo solo existe mientras está equipado).
- **Skill en todos los escudos (`MobaShieldSkills`):** todos los escudos del catálogo (grupo 6) reciben, en
  memoria, el skill **Spell of Protection (210)** — skill S6 con icono propio en el cliente, `All` clases, que
  ningún campeón aprende en el MOBA (no está en los loadouts). Se hace autocasteable (`Buff`, `Self`) y se amplía
  a las clases que pueden usar cada escudo. Se vende con el skill desde T1 (`HasSkill`). **Cooldown por tier del
  escudo equipado: 60 / 50 / 40 s** (los skills de ítem no están en `LearnedSkills`, por eso `MobaCooldowns`
  tiene un caso propio). Los libros del Summoner siguen con sus maldiciones (223/224/225) y también disparan la
  Égida. `TargetedSkillDefaultPlugin`: el skill 210 cuenta como lanzado sin efecto mágico propio.
  **Desvío respecto a lo aprobado:** se habían elegido 3 skills nuevos (uno por tier), pero la configuración S6
  no trae los skills maestros (323/521/524 no existen) y crear skills nuevos exige sembrar datos + iconos; se
  usó un único skill existente y el tier decide barrera y cooldown. **El cliente no necesita paquete nuevo**: la
  barra de skills sale de la lista que envía el servidor (solo el tooltip del ítem usa `m_wSkillIndex`).
- **Drops de creeps:** solo armas, sets y escudos (sin alas ni accesorios). Los escudos dropeados llevan 1 opción
  (70 %) o 2 (30 %) al azar entre las 4 de abajo, con el tamaño de su tier.
- **Opciones de los escudos (`MobaItemTraits`, aprobadas con ajustes del usuario):** T1 resistencia a CC 10 % ·
  T2 CC 15 % + reducción de cooldown 15 % · T3 CC 20 % + cooldown 25 % + oro por asistencia 10 % + oro pasivo
  10 % (más las excelentes del tier). Drops de T1/T2 con otras opciones usan tamaños CD 5/15/25 %, oro 5/8/10 %.
  **Topes totales:** CC 50 % (armadura + pantalón + escudo), cooldown 65 % (guantes + botas + escudo). CC = control
  de masas (aturdir, dormir, ralentizar): la resistencia acorta su duración. Cliente: paquete nuevo `C2 D5 0C`
  (opciones por ítem identificado como un precio: tipo + nivel + opción + luck + nº de excelentes; si dos drops
  comparten esa clave adoptan las mismas opciones) y líneas de tooltip en `MobaShopPrices.cpp`/`ZzzInventory.cpp`.
  **Cooldown uniforme al tope:** guantes y botas suben a 6/12/20 % c/u, así T3 completo = 20 + 20 + 25 (escudo) = **65 %**.
  Cliente recompilado (mu-main).

#### Vision v1 (2026-09-30, implementado en servidor; sin HUD todavía)

Alcance aprobado: **solo bloqueo de targeteo** (sin ocultar modelos en el cliente; eso queda como v2, riesgo
estimado 40-50 % de bugs de viewport). `MobaVision.cs`:
- Un campeón enemigo solo se puede apuntar (ataque básico `HitAction` y skills dirigidos
  `TargetedSkillDefaultPlugin`) si lo ve el equipo: campeón/creep aliado a ≤ 9 tiles, torreta/nexo aliado a ≤ 11,
  o ward aliado a ≤ 8. Creeps y torretas ven lo de su propio rango, así que no necesitan regla.
- **Ward:** 75 de oro (Zen del clon), dura 150 s, máx. 3 por campeón (el más viejo se reemplaza). Comando
  `/ward` (pone el ward en tu posición).
- **Barredor:** `/sweep` destruye wards enemigos en radio 6, enfriamiento 90 s.
- **HUD cableado (cliente recompilado):** slot 10 = Ward (tecla `0` o clic), slot 11 = Barredor (tecla `-` o clic); paquetes `C1 04 D5 0D` / `D5 0E`, la respuesta llega como mensaje azul. Los comandos `/ward` y `/sweep` siguen.
- **Pendiente:** ítem
  comprable en tienda, marcador en el minimapa y prueba en partida real. Los skills de área no pasan por la regla.

#### Checklist de validación y pendientes al cierre del 2026-09-30

**A testear en el juego** (servidor + `Main.exe` nuevo; GM testgm/testgm; flujo: `/moba` → `/mobalevel 30` → `/mobabotfight` → `/mobaleave`):
1. Skill del escudo (Spell of Protection, 210): aparece en la barra al equipar un escudo, se puede lanzar sobre uno mismo, tiene cooldown 60/50/40 s y dispara la Égida (burbuja 0x22, 10/15/20 % de vida máx., 30 s). Sin escudo no hay skill ni Égida. Libros del Summoner: sus maldiciones disparan la Égida.
2. Opciones de escudos: tooltips (T1 CC 10 · T2 CC 15 + CDR 15 · T3 CC 20 + CDR 25 + oro asistencia 10 + oro pasivo 10), efectos reales, topes CC 50 % / CDR 65 %; guantes y botas ahora 6/12/20 % de CDR. Drops de escudo con 1-2 opciones al azar y su tooltip.
3. Drops de creeps: solo armas, sets y escudos; se venden por 5 de oro.
4. Visión v1: `/ward` o tecla `0` / clic slot 10 (75 oro, 150 s, máx. 3); `/sweep` o tecla `-` / slot 11; no se puede apuntar a un campeón enemigo no visto (ataque básico y skills dirigidos).
5. Oro y EXP reales contra el simulador (`tests/MUnique.OpenMU.Tests/MobaGoldEconomyReport.cs`): T1 completo ~min 8,5, T2 ~22, T3 ~40; nivel 30 ~min 35.
6. Recall (tecla B, 5 s), pergamino de teletransporte (clic en minion aliado, enfriamiento 5 min), Dark Raven + Dark Horse (~25 % del daño del DL), mascotas de utilidad.

**Comando de pruebas `/mobafight` (2026-09-30, `MobaFightChatCommandPlugIn.cs`, GM, hay que estar en la arena con `/moba`):**
`/mobafight 1` → tu campeón va al equipo azul contra 1 bot rojo de clase al azar; `/mobafight 2` → 2v2 **solo de bots** (2 bots azules al azar vs 2 bots rojos al azar; vos no jugás ni contás, quedás sin equipo como espectador — para probar TP/tienda con tu personaje usá `/mobafight 1`); ambos crean oleadas de creeps de los dos equipos cada **50 s** y aseguran
torretas/nexos/tienda. `/mobafight stop` elimina todos los bots y creeps y detiene las oleadas (las estructuras
quedan). Cada ejecución limpia antes lo anterior.

**Primer log real de `/mobafight 1` (2026-09-30) — hallazgos y arreglos:**
- **Auto-target sin Ctrl:** el cliente exigía Ctrl para apuntar a otro jugador (regla PvP vainilla). Nuevo `IsMobaEnemyChampion` en `ZzzInterface.cpp`: en MOBA, un campeón del equipo contrario es atacable/auto-apuntable sin Ctrl (usa `c->MobaTeam` vs `g_MyMobaTeam`).
- **Skills que "no dejan tirar después del CD":** causa probable = **maná**. El servidor cobra ×3 el costo S6 (`Player.TryConsumeForSkillAsync`) pero el cliente chequeaba el costo nativo, así que animaba el cast y el servidor lo descartaba en silencio. En el log el DL llegó a 32/450 de maná (t=99 s) y regenera ~1/s. Arreglo: el cliente chequea ×3 en MOBA (`MOBA_MANA_COST_MULTIPLIER`). **Pendiente de decidir: regeneración de maná en MOBA** (hoy muy baja para costos ×3).
- **Logs nuevos:** servidor `[MOBA-CAST] ... REJECTED: cooldown|resources (...)` (solo campeones humanos, no bots) y cliente `[MOBA-CAST-CLIENT] skill N rejected locally: cooldown|mana` en `ErrorReport.txt` (máx. 1 por skill cada 500 ms).

**Log de `/mobafight 2` (2026-09-30) y tanda siguiente:**
- **Maná (`MobaMana.cs`):** los costos siguen ×3 (`MobaMana.CostMultiplier`), la regeneración nativa S6 (~1/s) se apaga para campeones y el servidor regenera por su cuenta: `regen/s = clamp(costo de su skill más cara con rango ÷ 5 s, 4 % del pozo, 12 % del pozo)`. Así, sin pociones, nunca pasa más de ~5 s sin poder lanzar su skill más cara (pozo 450 → 3.400; `MobaManaReport` es el helper de dimensionado). Con pociones constantes no se queda sin maná.
- **Pociones:** packs de **100** (`MobaShopCatalog.PotionPackSize`; el tamaño S6 de stack es 3, el pack lo salta), nivel **+7 / +8 / +9** según tier (cada nivel suma % de recuperación y acorta el tiempo de recuperación, mecánica nativa). Precio de pack fijo: pequeñas 500, medianas 900, grandes 1.500 (`PotionPackPrices`) — **valores míos, a ajustar**. El brillo del modelo no está (el cliente solo pinta el nombre amarillo para nivel ≥7).
- **Bots que juegan la economía (`MobaBotEconomy.cs`):** compran en la tienda de su equipo subiendo de a un tier en todos los slots (arma, armadura, pantalón, casco, guantes, botas, escudo, alas, collar, anillos), revendiendo la pieza reemplazada; compran un pack de poción de vida y otro de maná; se toman las pociones (maná < 30 %, vida < 40 % en combate); recogen del suelo equipo que sea mejor que el puesto y se lo equipan; hacen recall a la tienda cuando les alcanza el oro para el siguiente upgrade; y usan el pergamino de teletransporte al volver de la base si el minion aliado más adelantado está a > 45 tiles. Logs: `[MOBA-BOT-ECON]`, `[MOBA-BOT-TP]`. No existe oro en el suelo (el oro se acredita directo), así que "recoger oro" = recibirlo por farmeo.
- **Asedio de bots:** antes el nexo era intocable (`ClampToLaneLimit`: "the nexus siege itself isn't a bot job yet") y las torretas solo se golpeaban con la oleada encima. Ahora, con la torreta caída y una oleada aliada a ≤ 26 tiles del nexo, los bots entran con ella y pegan al nexo desde 4 tiles. Hay que verificar en el log (`[MOBA-AI] engage: attacking ... Nexus`).
- **`/mobafight 2`** entrega además un pergamino de teletransporte a cada campeón (vos y los bots).
- **Pergamino de teletransporte (humano):** el clic sobre el minion no hace nada y no hay datos para saber por qué; se agregaron logs `[MOBA-TP]` (servidor) y `[MOBA-TP-CLIENT]` (cliente, `ErrorReport.txt`) para ver si el modo de apuntado se activa y si el clic llega con un minion seleccionado.

**Ajustes tras la revisión del usuario (2026-09-30):**
- Aprobado tal cual: regeneración de maná, packs de poción (+7/+8/+9, 500/900/1.500), asedio de bots, economía de bots, TP de los bots.
- **Oro del suelo:** los creeps sueltan Zen nativo (`DroppedMoney`, se recoge con espacio/clic) además del oro directo de `MobaGold`; los bots ahora también lo recogen (`MobaBotEconomy.FindWantedDrop`, gasta hasta 3 intentos por drop). Los drops del suelo duran **60 s** (`GameConfiguration.ItemDropDuration`), con prioridad de recogida de 10 s para quien hizo el kill/aliados cercanos (`DroppedItem.TimeUntilDropIsFree`). **Ojo:** este Zen de suelo no estaba en el simulador de economía.
- **Pergamino de TP:** el minion elegido queda clavado (`MobaCreepHold`) durante los 5 s del canal y se suelta al llegar; el destino es la casilla libre más cercana pegada al minion, nunca la suya. Cliente: `TrySendMobaTeleportClick` se llama desde la selección de objetos y desde las teclas (el clic podía consumirse antes), y mientras se espera el objetivo un clic que no da en ningún minion ya no hace caminar al héroe (ESC cancela).
- **Dark Raven:** está en el catálogo de la página «Mascotas y Escudos» del DL (slot 8, 2.190 de oro, nivel 50); `MobaShopPageReport` (Explicit) vuelca lo que ve cada clase en cada página.

**Log de `/mobafight 2` de bots (27 min, 2026-09-30):**
- **Funciona:** los bots compran (packs de poción, armaduras, alas, anillos...), revenden lo reemplazado, lootean equipo, hacen recall a la tienda con oro suficiente y usan el pergamino de TP (`[MOBA-TP]`: «teleported next to minion», a 1 tile del minion). Ningún error/excepción en el log.
- **Falla:** Azul ganó todo (KDA 196/8, nivel medio 24,5 vs 16) pero **nunca bajó el nexo rojo** (100 % todo el tiempo): los bots rojos reaparecen en su fuente a ~19 tiles del nexo, siempre hay un enemigo a la vista y el estado «Fight» le ganaba al asedio (`GroupPush` exige que no haya enemigos cerca). Además, parado a 4 tiles del nexo un bot melee (rango 2) nunca llegaba a pegarle («walking in» eterno).
- **Arreglo:** un equipo **dominante** (≥ 5 niveles de ventaja de campeón, `DominanceLevels`) con ≥ 50 % de vida pasa a `GroupPush` aunque haya enemigos, sin exigir oleada, y puede cuerpear torreta y asediar nexo; distancia de parada al nexo 1 tile. Falta ver en el próximo log que el nexo baje (`[MOBA-STRUCT] ... Red Nexus HP`).

**Estado al cierre (2026-10-01) y pendientes para la próxima conversación:**
- **Validado por el usuario jugando:** pergamino de TP (clic en minion, canal 5 s, aterriza al lado; log `[MOBA-TP] teleported next to minion`), recall (tecla B), compra/uso del Dark Horse, `/mobafight 1`. El log de `/mobafight 1` solo mostró rechazos de maná del DL humano (skill 78, costo 135, con 9-78 de maná) y ningún error.
- **Bug nuevo: Dark Raven.** Se compra pero «no deja usarlo, pide comando». Aún no se sabe qué mensaje es (¿requisito de Comando 185 + 15 × nivel del cliente `ZzzInfomation.cpp`, que en MOBA ya devuelve `bEquipable` antes de chequear, o el comando de la mascota Normal / Ataque aleatorio / Ataque con dueño?). El servidor activa el ataque solo (`MobaPets.TickAsync` + `ActivatedRavens`) pero no hay logs del cuervo. Pedirle al usuario el texto exacto o reproducir con logs.
- **Bug nuevo: visión de aliados.** El minimapa no muestra lo que ven los creeps aliados y los creeps lejanos no se ven: el servidor solo manda al cliente los objetos dentro de su rango de escena. Solución propuesta: paquete propio `D5 10` (servidor → cliente) con posición y tipo de los campeones, creeps y estructuras aliados + los enemigos que vea cualquier aliado (`MobaVision.IsVisibleTo`), y el cliente los dibuja en el minimapa de MOBA (`NewUIMiniMap`); de paso sirve para el marcador de wards. Alternativa más cara: ampliar el rango de escena para aliados (riesgo de rendimiento).
- **Verificar el asedio de bots dominantes:** en el próximo `/mobafight 2` el nexo enemigo debe bajar del 100 % (`[MOBA-STRUCT] ... Nexus HP`).
- **Maná del humano:** `[MOBA-CAST] REJECTED: resources` sigue apareciendo en peleas largas; revisar con telemetría si la regeneración propia (`MobaMana`) rinde lo esperado (mín. 4 % del pozo por segundo).
- Pendientes anteriores sin hacer: brillo del modelo de las pociones +7/+8/+9 (cliente), cooldown visual del barredor (slot 11), ítem de ward comprable, marcador de wards en el minimapa, skills de área que respeten la visión, Vision v2 (solo si el usuario lo pide), validar oro/EXP reales contra el simulador (el Zen que sueltan los creeps no está en el simulador) y los puntos del checklist (Égida, opciones de escudos, drops, ward/barredor, caballo y cuervo del DL ≈ 25 % del daño, mascotas de utilidad), fallo de test preexistente `DescriptionMatchesWhatThePlugInRequires`.
- **Reglas de trabajo vigentes:** responder en español, avisar antes de cualquier gasto real, commit + push de cada paso, Claude reinicia/levanta el servidor y recompila el cliente (`buildclient.bat` en el scratchpad de la sesión; hay que cerrar `Main.exe` para enlazar), proponer alternativas antes de tocar código cuando haya varias razonables, y actualizar este documento antes de entregar un prompt.

**Sesión 2026-10-01 (retoma del balance):**
- **Logs revisados.** `openmu-server.log` (23:44-00:19) es solo `/mobafight 1`; el último 2v2 (`openmu-server-mobafight2b.log`) es anterior al fix de asedio de las 20:09, así que **el asedio de bots dominantes sigue sin verificarse** (hace falta un `/mobafight 2` nuevo; Nexo rojo 100 % en todos los logs guardados).
- **Maná:** los `REJECTED: resources` del DL humano (skill 78, costo 135-138, tenía 9-78) llegan en **ráfagas de 4-5 en el mismo milisegundo** (la tecla repite el cast), con pozo 450-552 (el DL estaba en nivel 1-2). La regeneración de diseño (27/s para costo 135) no alcanza si se encadenan Fire Burst + Fire Scream: es el comportamiento esperado, no un fallo. Para decidir con datos se agregó telemetría **`[MOBA-MANA]`** (`MobaMana.cs`, cada 5 s mientras el humano gasta o regenera: maná actual/máx, regen/s, regenerado y gastado/bebido en la ventana).
- **Dark Raven:** revisado el código y **no hay bloqueo ni en servidor (`CompliesRequirements` ya salta los requisitos del clon) ni en cliente (`IsEquipable` ya devuelve antes del chequeo de Comando 185 + 15 × nivel)**; el único texto de «Comando» que ve el jugador es la línea roja del tooltip (`GIPetManager.cpp`, `CharismaRequirementD`). Sin reproducción real no se encontró la causa; se agregaron logs: servidor `[MOBA-EQUIP]` (equipar rechazado, con slot/ranura/cumple), `[MOBA-RAVEN]` (activación del ataque con el dueño y un ataque cada 10 s); cliente `[MOBA-RAVEN-CLIENT]` en `IsEquipable`. Falta que el usuario compre el cuervo y pase el log + el texto exacto del mensaje.

**Minimapa MOBA — opción A aprobada (2026-10-01):** paquete `C1 D5 10` (`MobaMinimapPeriodicPlugIn`, ~1 Hz): `count` + `[kind team x y hp%]`. kind 1 campeón, 2 torreta, 3 nexo, 4 ward; **no se envían creeps** (pedido del usuario). Se mandan campeones aliados (menos el propio), estructuras de ambos equipos, wards aliados y los campeones enemigos que ve el equipo (`MobaVision.IsVisibleTo`). Cliente: `ReceiveMobaMinimap` (`g_MobaMinimap`), cuadrados de color por equipo (azul/rojo, ward verde) en el minimapa grande y en el de la esquina (`RenderPointRotateSquare` / `RenderColor`). Con esto queda hecho el marcador de wards.
**Dark Raven:** el usuario ya pudo usarlo (2026-10-01); no se halló causa en el código, los logs `[MOBA-EQUIP]` / `[MOBA-RAVEN]` quedan por si reaparece.
**Nivel y puntos:** el nivel de campeón (1-30) se ve en la barra de EXP del HUD (`NewUIMainFrameWindow`, reemplaza el nivel nativo) y por mensajes azules al subir. Cada subida da 1 punto de habilidad y puntos de stats (clase normal 100.000 en 29 subidas ≈ 3.448/nivel, RF/Summoner 112.000 ≈ 3.862, DL 130.000 ≈ 4.482; tope 30.000 por stat) que se reparten con `/mobaadd <str|agi|ene|vit|cmd> <n>`; `/mobastats` muestra el reparto. Ahora el mensaje de subida de nivel avisa de los puntos y del comando.

**Nivel y puntos visibles (2026-10-01, a pedido del usuario):** corrección: el nivel de campeón NO se mostraba como número (la barra de EXP no lo imprime) y la ventana C seguía con el nivel 400 real y sin puntos. Ahora: (1) texto fijo «Nv N» sobre la esquina izquierda de la barra de EXP; (2) el paquete `D5 02` lleva al final los **puntos de stats sin gastar** (u32) y el cliente los vuelca en `CharacterAttribute->LevelUpPoint`, así la ventana C muestra «Punto: N» y los botones «+»; (3) en C, en MOBA: «Nv campeon N», EXP de campeón, «Puntos de habilidad: N (+ sobre la barra)» y la ayuda de los botones; (4) el «+» de C invierte **100 puntos por clic** (servidor, `MobaStatEconomy.ClickAmountFor`, tope 30.000 por stat); Shift repite el pedido 10 veces (+1.000) y Ctrl 50 (+5.000); (5) `/mobaadd` y cada subida refrescan el estado. Los **puntos de habilidad** no se gastan en el árbol de Master (que no existe en el clon) sino con los «+» que aparecen sobre los skills de la barra del HUD cuando `g_MobaSkillPoints > 0`.

**Árbol Master en la partida (camino B, decidido 2026-10-01):** el usuario pidió poder usar el árbol Master nativo, con **5 puntos por nivel de campeón** (~145 en 29 subidas, un sexto del árbol de ~850), **conviviendo** con los puntos de habilidad (rangos 1-5 y «+» sobre la barra). `MobaMasterTree.cs`: cada subida suma 5 puntos (`MasterLevelUpPoints`), fija `Stats.MasterLevel` = nivel de campeón y manda `SendMasterStatsAsync` (la ventana del árbol sale del botón «Master» de C). Solo se pueden gastar puntos en nodos **pasivos**: los de **daño** (alimentan el término «árbol» de la mezcla de daño), **Vida máxima** y **Maná máximo**; el resto (fortalecedores que *reemplazan* un skill —romperían las tablas por número de skill—, resistencias, durabilidad, recuperaciones…) el servidor lo rechaza con un mensaje azul. Regla de rango propia (`MeetsRank`): rangos 1-2 libres, y el rango r pide (r−2)×10 puntos en la misma rama; el cliente deja de exigir los requisitos nativos en MOBA (`NewUIMasterLevel.cpp`: `CheckParentSkill/CheckRankPoint/CheckBeforeSkill` devuelven true con `g_MobaLevel > 0`). Reporte de nodos: `MobaMasterTreeReport` (Explicit, `%TEMP%\moba-master-tree.txt`); capacidad de nodos de daño por clase: Wizard 100, Knight 160, Elf 100, MG 200, DL 140, Summoner 180, RF 80.
**Pesos del daño (fijados por el usuario):** `fracción = 0,45 × stats + 0,20 × árbol + 0,35 × ítems` (`MobaSkillDamage.BlendedFraction`; árbol = puntos en nodos de daño ÷ min(100, 80 % de la capacidad de la clase)). Mitigación y crítico siguen 60 % stats / 40 % ítems (el usuario fijó solo el peso del daño).
**Bots = humano (2026-10-01):** los bots reparten los puntos de stats 35 % primario / 35 % VIT / 30 % AGI (cada uno con tope 30.000, el sobrante va a los demás) y gastan los puntos Master en los nodos de daño hasta el tope de su clase, luego vida y maná. Antes volcaban todo al primario y dejaban ~70.000 puntos sin gastar, mientras el humano podía llenar VIT y AGI. **Otros huecos conocidos entre bot y humano (sin arreglar):** los bots no compran mascotas (caballo/cuervo del DL ≈ 25 % de su daño; mascotas de utilidad 5-10 %), no lanzan el skill del escudo (Égida) y no usan ward ni barredor.

**Decisiones del usuario al cierre del 2026-10-01 y plan para la próxima conversación:**
1. **Pesos 45/20/35 también en mitigación y crítico** (no solo en daño): `fracción = 0,45 × stats + 0,20 × árbol + 0,35 × ítems` en `MobaDefense.FinalMitigationOf` y `MobaCombatStats.FinalCritChanceOf` (hoy 60/40). El término «árbol» de cada uno sale de los nodos del tipo correspondiente (defensa → mitigación; crítico/doble daño → crítico). Mantener los techos (mitigación 70 %, crítico 60 %).
2. **El usuario quiere TODO el árbol Master útil, no solo daño/vida/maná.** La restricción actual (`MobaMasterTree.IsAllowed`) es provisional. Hay que agrupar los nodos por tipo e integrarlos uno por uno. Clasificación recomendada (datos reales en `MobaMasterTreeReport`, `%TEMP%\moba-master-tree.txt`, ~850 puntos por clase, nodos de hasta 20 niveles):
   - **A. Daño** (fortalecedores de arma, Mastery de daño, mínimo de ataque, Wizardry/Book/Staff, Dark Raven/Scepter pet): HECHO → término árbol del daño.
   - **B. Vida / Maná máximos:** HECHO (topes de la curva ×1,2).
   - **C. Fortalecedores de skill (ACTIVE «X Strengthener», reemplazan el skill N por otro número):** recomendado NO reemplazar el skill; el nodo conserva el skill base en la barra y suma **+% de daño a ese skill** (y los «Mastery» aportan un efecto secundario curado: aturdir/ralentizar con `MobaCc`, objetivo extra, etc.). Requiere mapear nodo → skill base (`ReplacedSkill`) y sumar a `MobaSkillDamage`; evitar que `AddLearnedSkill` meta el skill de reemplazo en la lista.
   - **D. Defensa** (Base Defense, Defense Rate PvM/PvP, Max SD, resistencias veneno/rayo/hielo, escudo/caballo defensivos): término árbol de la **mitigación**; las resistencias elementales como reducción extra contra daño de esos elementos si existe, si no sumarlas a la mitigación.
   - **E. Crítico / probabilidades** (Raven crit/exc chance, Double Damage chance de lanza/guante, Stun chance de maza, Attack Rate): término árbol del **crítico**; Attack Rate no tiene sentido sin tirada de acierto (sumar poco al crítico o dejar inerte).
   - **F. Recuperación** (HP/Mana/AG/SD Recovery, recuperar tras matar monstruo): conectar a la regeneración propia de MOBA (`MobaCombatRegen`, `MobaMana.RegenPerSecond` con multiplicador) y a «vida/maná al último golpe de creep».
   - **G. Utilidad** (Mana Usage Reduction → multiplicar el costo ×3 de `MobaMana`; Weapon Mastery Attack Speed → velocidad de ataque con su tope ≤70; Summoned Monster HP/Def, Pet Duration → solo donde el clon tiene esas mecánicas).
   - **H. Sin sentido en MOBA (Item Duration / «Durability Reduction» ×3, 60 puntos por clase):** reasignar a algo útil (p. ej. reducción de enfriamiento) o dejarlos inertes y avisar.
   Orden sugerido: C (skills) → D (defensa/mitigación) → E (crítico) → F (recuperación) → G (utilidad) → H. Cada tipo se valida con un reporte antes de habilitar sus nodos, y `IsAllowed` pasa a ser una tabla por tipo.
3. **Otros huecos de balance bot vs humano a resolver:** (a) los bots no compran mascotas (caballo/cuervo del DL ≈ 25 % de su daño, utilidad 5-10 %); (b) no lanzan el skill del escudo (Égida, skill 210, 60/50/40 s); (c) no usan ward ni barredor (`MobaVision.TryPlaceWard/TrySweep`). Revisar si aparece otro y avisar.
4. **Falla de test preexistente `DescriptionMatchesWhatThePlugInRequires` — causa ya diagnosticada:** 11 comandos de chat MOBA requieren GameMaster (`MinCharacterStatusRequirement`) pero su `[ChatCommandHelp(...)]` documenta el estado por defecto «Normal»: MobaBot, MobaBotClear, MobaBotFight, MobaClonePreview, MobaDummy, MobaFight, MobaLevel, MobaNexus, MobaTurrets, MobaWave, MobaWaves. Arreglo: usar el constructor `ChatCommandHelp(command, CharacterStatus.GameMaster)` / el que recibe el tipo de argumentos con `CharacterStatus.GameMaster` en cada uno.
5. **Lista completa de pendientes al 2026-10-01:** pesos 45/20/35 en mitigación y crítico; integrar todos los tipos del árbol (arriba); huecos de bots (mascotas, Égida, ward/barredor); arreglo del test; **verificar el asedio de bots** con un `/mobafight 2` nuevo (el nexo debe bajar del 100 %); **probar en juego lo implementado hoy** (minimapa D5 10 sin creeps, «Nv N» y puntos en la ventana C, «+» de C con 100/1.000/5.000 por clic, árbol Master con 5 puntos por nivel y mensajes de nodos rechazados, `[MOBA-MANA]`); cooldown visual del barredor (slot 11); ítem de ward comprable en la tienda; que los skills de área respeten la visión; brillo del modelo de las pociones +7/+8/+9 (cliente); validar oro y EXP reales contra el simulador (`MobaGoldEconomyReport`, ojo: el Zen del suelo no está en el simulador); validar Égida, opciones de escudos, drops, caballo y cuervo del DL (~25 %), mascotas de utilidad, pergamino de TP; Vision v2 solo si el usuario lo pide; telemetría `[MOBA-MANA]` para decidir si la regeneración de maná rinde; los logs `[MOBA-EQUIP]/[MOBA-RAVEN]` quedan por si el Dark Raven vuelve a fallar.

**Sesión 2026-10-01 (2ª parte) — hecho:**
1. **Test arreglado:** nuevo constructor `ChatCommandHelpAttribute(command, description, argumentsType, minimumCharacterStatus)`; los 11 comandos MOBA de GM declaran `CharacterStatus.GameMaster`. Suite 100 % verde (851/851).
2. **Pesos 45/20/35 en daño, mitigación y crítico** (`MobaMasterTree.Blend`, constantes `StatsWeight/TreeWeight/ItemsWeight`). El término árbol de la mitigación sale de los nodos **Defensa** y el del crítico de los nodos **Crítico**; si una clase no tiene nodos de ese tipo el término árbol se descarta y stats/ítems se renormalizan (no pierde el 20 %). Techos intactos (mitigación 70 %, crítico 60 %).
3. **Árbol Master por tipos** (`MobaTreeKind` + `MobaMasterTree.Classify/KindOf`; `IsAllowed` = tipo en `AllowedKinds`). **Habilitados hoy:** A Daño, B Vida, B Maná, **D Defensa** (Base Defense, Defense Rate PvM/PvP, resistencias, bonos de escudo/caballo; `Maximum Shield` queda fuera porque el escudo es la curva de nivel + Égida y sumaría durabilidad sin tope) y **E Crítico** (Critical Damage chance/bonus, Double Damage chance, Raven crit/exc, Attack Rate a medio peso). El clasificador corrige un error del filtro anterior (todo lo que contenía «Damage» era daño: ahora crit, «Receive/Decrement/Chance» van aparte). Tope de cada tipo = min(100, 80 % de la capacidad de la clase): daño 48-100, defensa 100 en todas, crítico 16-48 (pocos nodos). **Plan de relleno** (humano y bots, `FillStages`): daño 50 %, defensa 50 %, crítico 100 %, daño 100 %, defensa 100 %, vida, maná. Reporte: `MobaMasterTreeReport` (resumen por tipo + nodos por tipo) → `%TEMP%\moba-master-tree.txt`. RF queda con 60 puntos de daño (antes 80: «Equipped Weapon Mastery» pasó a crítico).
   **No habilitados todavía:** Recovery (F), Utility (G), Strengthener (C, 44+ nodos), Useless (H), Other (maestrías con aturdir/mover objetivo, Maximum Shield).
4. **Bots como humanos:** compran mascotas (`MobaBotEconomy.PetPurchase`: DL = Dark Horse en T2 + Dark Raven en T3; resto = Imp T1 → Horn of Dinorant T2 → Black Fenrir agresivo T3; un pet de tier t cuando todo el equipo está en tier t-1 y vendiendo el anterior), lanzan la Égida (skill 210) en pelea con < 80 % de vida si no está en cooldown, ponen ward en la línea (cada 45 s, fuera de pelea, sin ward aliado cerca, sin ahorro para compra) y usan el barredor si hay un ward enemigo a ≤ 6 tiles (`MobaBotPlayer.TickUtilityAsync`, logs `[MOBA-BOT-UTIL]`; compras de mascotas por `[MOBA-BOT-ECON]`). **Sin verificar en partida** (falta un `/mobafight 2`). Otros huecos: no revisé aún si los bots respetan la regla de visión de ataque (`MobaVision.CanTarget`); queda como punto a comprobar.

**Sesión 2026-10-01 (3ª parte) — decisiones del usuario y árbol completo:**
- **SD = 25 % de la durabilidad:** pozo de SD = vida/3 (`MobaProgression.ShieldAt`, con el multiplicador de rol de la vida). El reparto vida/SD sigue la regla nativa (90 % del golpe al SD mientras quede). **Opciones que atacan/defienden el SD** (`MobaItemTraits`, enganchadas en `GetHitInfo` para clones): perforación de SD del arma (−5/10/15 % al ratio), probabilidad de ignorar el SD de las alas (3/6/10 %), refuerzo de SD de armadura+pantalón (+2/4/6 % cada uno). **Pendiente de cliente:** los tooltips de esas tres opciones (nuevos tipos 6/7/8 de `MobaItemTrait`).
- **Égida:** los nodos «Maximum SD» (tipo Shield) suman hasta +25 % a la barrera de la Égida con el árbol completo (= 20 % del total). Sin nodos queda como antes.
- **H (Durability Reduction ×3):** pasan a reducción de daño: hasta 15 % extra, multiplicativo, después del techo de mitigación (`MobaMasterTree.DurabilityDamageReduction`).
- **C (fortalecedores):** el nodo NO reemplaza el skill (en `SkillList` el clon solo guarda los puntos); suma +1,5 % de daño por punto al skill base (máx. +45 % por skill) en `MobaSkillDamage.GetSkillBaseDamage`. Solo los de skills con tabla de daño; los de buffs/invocaciones quedan fuera. Los bots solo los llenan para skills que tienen.
- **F (recuperación):** HP/SD (regen nativa, solo fuera de combate) y maná (multiplica `MobaMana.RegenPerSecond` hasta ×1,5) habilitados. **Recuperación de AG:** inerte (sin AG). **«Recover after Monster kill» (HP/maná/SD): NO habilitado** — los valores nativos son divisores leídos como multiplicadores (curarían la barra entera por kill); falta darles valores MOBA (propuesta: 2 % → 10 % del máximo por último golpe).
- **G (utilidad):** Mana Usage Reduction (tope 40 %, `MobaItemCaps`) y velocidad de ataque de las Mastery (tope ≤ 70) habilitados; invocaciones/Swell Life inertes.
- **Plan de relleno** ampliado (`FillStages`): daño/defensa 50 %, crítico, fortalecedores 40 %, escudo, durabilidad, utilidad, recuperación 50 %, resto de daño/defensa/escudo/durabilidad, vida, maná.

**Decisiones del usuario al cierre de la 3ª parte (2026-10-01) y plan para la próxima conversación:**
1. **Recuperación al matar (HP/maná/SD): como recomendé.** Valores MOBA propios por último golpe de creep: 2 % del máximo con 1 punto → 10 % con 20 puntos (lineal), HP/maná/SD cada uno con su nodo («Monster Attack Life/Mana/SD Inc»; RF: «Recover HP/Mana/SD from Monster Kills»). Hay que neutralizar el efecto nativo del nodo (su fórmula es un divisor leído como multiplicador: curaría la barra entera): clasificar como `KillRecovery` habilitado pero con el valor nativo anulado/clamp en `Player.AfterKilledMonsterAsync` para clones, y calcular el valor MOBA desde el nivel del nodo (`MobaMasterTree`). Los ítems con «vida/maná al matar» (excelente) siguen por el camino nativo.
2. **Tooltips de las opciones de SD: sí, hacerlos** (cliente + servidor): perforación de SD del arma, ignorar SD de las alas, refuerzo de SD de armadura/pantalón (`MobaItemTraits.SdPierceOf/SdBypassOf/SdGuardOf`; hay que agregar los tipos 6/7/8 a `MobaItemTrait`, al paquete de traits y al tooltip del cliente). Hacerlo junto con cooldown visual del barredor y brillo de pociones en una sola recompilación del cliente.
3. **Fortalecedores de BUFF — el usuario quiere integrarlos; proponer cómo.** Hoy quedan en `Other` porque su skill base no está en la tabla de daño. Nodos: Elf: «Attack Increase Str/Mastery» (Greater Damage, skill 28), «Defense Increase Str/Mastery» (Greater Defense, 27), «Infinity Arrow Str» (77), «Heal Strengthener» (26), «Summoned Monster Str 1/2»; Knight: «Swell Life Strengt/Proficiency» (48); Wizard: «Soul Barrier Strength/Proficiency» (16), «Expansion of Wiz Streng/Mas» (233); Summoner: «Berserker Strengthener/Proficiency» (217), «Sleep Strengthener» (219), «Drain Life Strengthener» (214); DL: «Crit DMG Inc PowUp 1/2/3» (Increase Critical Damage, 64); RF: «Def SuccessRate IncPowUp / IncMastery», «Stamina Increase Strengthener». **Propuesta a presentarle:** el nodo suma +1,5 %/punto (máx. +45 %) a la *magnitud o duración* del buff base en MOBA: Greater Damage/Expansion/Infinity Arrow/Increase Critical Damage → +% al bono de daño que da el buff; Greater Defense/Soul Barrier/Swell Life/Increase Block → +% a la mitigación o al escudo/vida que da; Heal → +% a la curación; Berserker/Sleep/Drain Life → +% al efecto (daño recibido por el enemigo / duración del sueño / robo de vida); invocaciones (Summoned Monster) → solo si el clon tiene esas invocaciones, si no inertes. Requiere mirar cómo implementa cada buff el MOBA (`MobaCastEffects`, `MobaPassives`, loadouts) antes de decidir.
4. **Maestrías con efecto — el usuario las quiere incluir; lista completa y propuesta de integración:**
   | Nodo (clase) | Qué hace en S6 | Integración propuesta en MOBA |
   |---|---|---|
   | Earthshake Mastery (DL), Fire Burst Mastery (DL), Mace Mastery (Knight), Wind Tome Mastery (Summoner) — «Mastery Stun Chance» | probabilidad de aturdir con ese skill/arma | % de chance (hasta ~20 % a 20 pts) de aplicar aturdimiento corto (0,8-1,2 s) con `MobaCc` al impactar con ese skill; con ICD por objetivo (p. ej. 6 s) y respetando la resistencia a CC |
   | Twisting Slash Mastery (Knight), Lightning Tome Mastery (Summoner) — «Move Target Chance» | el golpe desplaza al objetivo | chance de empujar 2-3 tiles al objetivo (`MobaCc`/desplazamiento) con ICD; no empujar estructuras |
   | Triple Shot Mastery (Elf) — «Extra Projectiles» | +flechas por volea | +1 flecha (+1 a 20 pts) a ~50 % del daño de una flecha cada una, hasta 3 objetivos |
   | Sleep Strengthener (Summoner) — «Bonus Chance» | más chance/duración de Sleep | +% duración del sleep (tope 2 s) |
   | Drain Life Strengthener (Summoner) — «Bonus Healing» | más curación del Drain Life | +% a la curación del skill (hasta +45 %) |
   | Killing Blow Mastery (RF) — «Weakness Physical Damage Decrement» | el golpe debilita el daño físico del enemigo | −5 % daño del objetivo por 3 s (tope 15 %) |
   | Beast Uppercut Mastery (RF) — «Defense Decrement» | baja la defensa del objetivo | −% de mitigación del objetivo por 3 s (reutiliza la penetración de `MobaDefense.PenetrationBySkill`) |
   | Rageful Blow Mastery (Knight/RF) — «Durability Decrease Chance» | no baja durabilidad de ítems | sin sentido en MOBA: inerte (o reasignar a +% de daño de Rageful Blow) |
   | Pet Durability Str (DL) — «Pet Duration» | dura más el pet | inerte, o +% al daño del Raven |
   | «Maximum Shield» ya hecho; «Greater Damage/Defense Bonus» ver punto 3. | | |
   Presentar la lista al usuario para confirmar cada integración antes de codificar (cada una necesita un reporte/test propio).
5. **Pendientes que siguen:** verificar con un `/mobafight 2` (nexo < 100 %, mascotas/Égida/ward/barredor de bots, SD nuevo, reparto de puntos del árbol, logs `[MOBA-BOT-UTIL]` y `[MOBA-BOT-ECON]`); revisar con el usuario lo probado en juego (minimapa, Nv/puntos en C, «+», árbol Master, `[MOBA-MANA]`); cooldown visual del barredor (slot 11); ítem de ward comprable; skills de área con visión; brillo de pociones +7/+8/+9; validar oro/EXP contra `MobaGoldEconomyReport`; validar Égida, opciones de escudos, drops, caballo/cuervo del DL y mascotas de utilidad con logs; comprobar si los bots respetan `MobaVision.CanTarget`; Vision v2 solo si el usuario lo pide.

**Sesión 2026-10-01 (4ª parte) — primer `/mobafight 2` con el árbol/SD/bots nuevos (log `openmu-server-mobafight2c.log`, 14,5 min):**
- **Sin errores/excepciones.** Funciona: bots ponen wards y usan el barredor (`[MOBA-BOT-UTIL]`), compran Imp (T1), packs de poción y equipo. Égida de bots: ningún registro en el log (hay que ver si lanzan el skill 210; el log no lo anota).
- **El nexo siguió en 100 %** (torreta roja a 51 %): 113 avisos `IDLE`. **Bug 1 (reportado por el usuario):** bots a distancia (mago y elfas) congelados «walking in (dist 6 > range 6)»: con el objetivo a 6,3 tiles y rango 6, el destino de `StepShortOf` quedaba a < 1 tile y `WalkTowardAsync` lo ignora. Arreglo: el bot se detiene a `range-1` (`MobaBotPlayer.EngageAsync`).
- **Bug 2: vida máxima de las elfas BAJA al subir de nivel** (8.118 → 3.936; la azul llegó a −2.083). Causa probable: `MobaItemCaps.CapAttribute` restaba un delta plano, que un multiplicador de la stat amplifica y pasa del tope. Arreglo: se mide el efecto real del elemento y se reescala para caer exactamente en el tope. **Falta confirmar en un nuevo `/mobafight 2`** (la vida máx. de las elfas debe crecer con el nivel).
- Mascotas: solo se vio Imp (T1); las de T2/T3 requieren que todo el equipo llegue al tier anterior (no ocurrió en 14 min).

**Pendiente de hacer:**
- Cooldown visual del barredor en el slot 11; ítem de ward comprable en la tienda; marcador de wards en el minimapa.
- Vision v2 (ocultar enemigos en el cliente, riesgo 40-50 % de bugs de viewport) — decidido dejarlo para después de validar v1.
- Que los skills de área también respeten la visión (hoy solo ataque básico y skills dirigidos).
- Nombre del skill del escudo en el cliente es «Spell of Protection» (no se puede renombrar sin tocar la tabla de skills del cliente).
- Ajustes de balance según el log de `/mobabotfight`.
- Falla de test preexistente `DescriptionMatchesWhatThePlugInRequires` (no relacionada).

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

**Prioridad de adquisición** (lista completa de LoL, de mayor a menor; se evalúa
cuando el creep no tiene objetivo válido). "Está atacando a X" = ese enemigo hizo
una acción dañina sobre X en los últimos ~2 s. Todo dentro del rango de
adquisición.

1. **[INTERRUPCIÓN] Campeón enemigo en combate campeón-vs-campeón** con un
   **campeón aliado** del creep en los últimos **3 s**, en cualquier dirección
   (el enemigo pegó a nuestro campeón, o nuestro campeón le pegó al enemigo).
   *Interrumpe el ataque en curso* (salvo lock de estructura). Al expirar los 3 s
   revierte a esta lista. **El daño a creeps NUNCA dispara esto** — un jugador
   puede hacer *last hit* a la oleada libremente (como en LoL).
2. **Creep enemigo que está atacando a un campeón aliado.**
3. **Creep enemigo que está atacando a un creep aliado** (foco de fuego).
4. **Creep enemigo que me está atacando a mí** (a este creep).
5. **Creep enemigo más cercano.**
6. **Campeón enemigo más cercano** (solo si no hay ningún creep enemigo en rango).
7. **Estructura enemiga más cercana** (torreta → nexo).

*(No existe "campeón enemigo atacando a un aliado/a mí" como regla propia: en LoL
un campeón que golpea minions no entra a la tabla de aggro de minions salvo por
la regla #1 campeón-vs-campeón.)*

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

**Implementación:** un **registro de eventos de combate** (RAM, poda 5 s) anota
cada golpe `(atacante, víctima, tiempo)` entre participantes MOBA; la evaluación
de prioridad lo consulta (ventana ~2 s para #2–#4, 3 s para el aggro de campeón
#1).

**Diferido a después de v1** (impacto bajo): leash con invulnerabilidad + boost de
velocidad en el regreso, tipos de minion (melee / caster / cañón / super), y
requisito de visión (relevante recién con arbustos / jungla en Fase 3).
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
| 8 | Tienda de ítems de la Fase 1 | **Un Hanzo por base** → **menú de categorías por nombre** en el diálogo nativo de NPC (texto enviado por el servidor) → **ventana de vendedor nativa** filtrada por clase, T1–T3 mezclados. **Se edita MuMain** (menú server-driven + precios del servidor en el tooltip). Moneda = **Zen del clon**; **venta al 70 %**, sin tirar ni tradear; **sin requisitos de stats** en el match. Precio por **fórmula** (grado + nivel + opciones). Catálogo provisional. | 2026-09-23 |

### Notas de diseño relacionadas

- **Límites de la arena:** no dependemos de la forma nativa del mapa. Se recorta
  la zona jugable con (A) un **plugin de borde** en el servidor que rebota al
  jugador si sale del polígono de arena + mensaje, y opcionalmente (B) "hornear"
  el `TerrainData` del mapa para que oleadas/torretas/spawns respeten el mismo
  límite (Fase 2), y (C) editar el `.att` del cliente para el muro visual
  (pulido posterior). Los límites viven en config → ajustables sin recompilar.
  Consecuencia: se puede tallar un carril alargado dentro de *cualquier* mapa,
  así que la elección de mapa pesa más por ambiente/assets que por geometría.
