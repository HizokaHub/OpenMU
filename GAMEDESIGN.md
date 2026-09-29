# GAMEDESIGN — Modo de juego custom sobre OpenMU (S6E3)

> Documento de diseño. Contexto trasladado desde una conversación previa para
> centralizar el trabajo en este repositorio. A partir de ahora el diseño y el
> desarrollo avanzan únicamente aquí.

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
- **Bug de cliente:** `Main.exe` (MuMain, compilado 2026-09-23) **crashea al iniciar**
  (APPCRASH 0xc0000374 = corrupción de heap en ntdll, 2026-09-29 17:43) justo tras
  "First Load Files OK" / `File not found Data\Interface
ewui_item_back01.tga`, antes
  de conectar (el servidor no registró ninguna conexión). Sospechas: build viejo
  respecto del último commit del cliente (`c492d5a3`) o textura faltante. Primero
  recompilar el cliente y reprobar; si persiste, depurar con el .mdmp de WER.

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
