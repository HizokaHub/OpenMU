# Rune Wizard (S21) en OpenMU / MuMain — documento de traspaso

> **Toda conversación nueva sobre el Rune Wizard (RW) debe leer este archivo entero antes de hacer nada**, y después `GAMEDESIGN.md` (secciones «Rune Wizard») y `docs/CODING_RULES.md` del cliente antes de tocar C++. Actualizarlo al terminar cada fase. Última actualización: 2026-10-28.

## 1. Qué se quiere

Traer al juego el **Rune Wizard** del cliente IGCN **S21** (el «monito» élfico, ranura de clase 8 de S21) con **su propio contenido** (skills, efectos, items, sets), tanto para el **MU normal** como para el **MOBA «Eclipse Arena»**. Lo común va en las capas compartidas (datos de configuración de OpenMU, protocolo, código general de MuMain); lo específico del MOBA va encima (no dentro de `PlugIns/Moba` ni detrás de `WD_200_MOBA_ARENA` lo que sirva a ambos modos).

- El usuario pidió explícitamente: *«no la configures como SM, agrégale sus cosas»* → el RW **no** debe usar skills/items prestados del Dark Wizard (las skills ya son propias; los items siguen prestados, ver §5).
- **Último pedido (2026-10-28, sin empezar):** *integrarla en la creación de personajes* (con su imagen y animaciones de S21 si hacen falta) para que el usuario cree un RW y pruebe sus skills. El usuario dijo «espera» a mitad de la investigación: **no se ha cambiado nada** para esto. Ver §6.

## 2. Reglas del usuario (obligatorias)

- Responder **siempre en español**.
- **Avisar antes de cualquier gasto real** (APIs de pago, builds de pago, descargas grandes).
- Al terminar cada paso: **commit y push** (mensaje descriptivo; terminar con `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`). Mantener `GAMEDESIGN.md` al día.
- **Nunca matar procesos por nombre.** El servidor se identifica por su puerto (44406); usar `tools/iniciar-todo.ps1` (`-Parar`, `-SinPortal`, `-Estado`).
- **Avisar antes de reiniciar el servidor o recompilar/enlazar el cliente**: el usuario tiene que cerrar su cliente (el `Main.exe` en uso bloquea el enlazado y la copia de assets).
- **Cualquier migración/escritura en la base de datos exige: plan → `pg_dump` → confirmación explícita del usuario** (el punto de confirmación es al presentar el plan, no en cada comando posterior que lo ejecute). Nada de tocar la BD «de pasada».
- No usar directamente las ramas `moba/phase1` (servidor) ni `moba-camera` (cliente): otra conversación trabaja ahí. Se trabaja en ramas/worktrees propios.
- Los assets de S21 son de terceros; el lanzamiento público depende de una decisión legal pendiente.
- **Fidelidad de efectos = nivel B**: modelo + textura de S21 (`Data\Effect`, convertidos con `tools/bmd_convert.py`) + comportamiento simple programado en MuMain con luz y sonido, animación de ataque por skill, numeración propia de skills (por `MAX_SKILLS=650`). No se hace nivel C. En el MOBA cada efecto debe dejar claro el alcance y el momento del golpe.
- Strings C++ del cliente con acentos: el cliente compila en ANSI → re-escapar con `tools/escapar_unicode_cpp.py` (la herramienta Write convierte `\u` en UTF-8).
- Avisar proactivamente si hay una forma más rápida/barata/mejor de hacer algo.

## 3. Repos, ramas y estado

| | Servidor (OpenMU fork) | Cliente (MuMain) |
|---|---|---|
| Checkout normal | `C:\Users\aruiz\Proyectos\openmu-aram` | `C:\Users\aruiz\Proyectos\mu-main` |
| **Worktree del RW** | `C:\Users\aruiz\Proyectos\openmu-aram-rw` | `C:\Users\aruiz\Proyectos\mu-main-rw` |
| Base | `moba/phase1` | `moba-camera` |
| Rama actual | `rw/phase4` (cadena `rw/phase0 → phase1 → phase3 → phase4`) | `rw/phase4` (sobre `rw/phase0`) |
| Último commit | `dbd76af13` (GAMEDESIGN fase 4a); skills `75bf10635` | `159a5ad4` (skills en el cliente) |
| Remoto | `origin` = HizokaHub/OpenMU, todo subido | todo subido |

Fuente de datos S21 (solo lectura): `C:\Users\aruiz\OneDrive\Desktop\RadicaL\Nexus MU Online`.

**Notas, informes, TSV y herramientas** (fuera de los repos): carpeta de la sesión, ahora en
`C:\Users\aruiz\AppData\Local\Claude-Data\claude_pzs8sxrjxfjjc\side-session-notes\local_1061241a-6fce-4202-bf54-0af21d1b1f92\` (antes en `...\AppData\Roaming\Claude\side-session-notes\...`; si una no existe, probar la otra). Contiene `informe-viabilidad-rune-wizard.md`, `verificacion-datos-cifrados-s21.md`, `tercera-tanda-datos-s21.md`, `datos-rw/` (rw_skills, rw_items, rw_sets, rw_master_tree), `datos-s21/*.tsv`, `herramientas/*.py` (`s21_data.py`, `volcar.py`, `analizar.py`, exportadores, renderizadores), imágenes y `referencias-efectos-web/`.

**Estado de procesos al escribir esto:** el servidor **normal** (`openmu-aram`, PID 14480) está corriendo; el servidor de vista previa del worktree y el cliente RW están **cerrados**. Comprobar con `tools\iniciar-todo.ps1 -Estado` antes de asumir nada.

## 4. Lo que ya está hecho

- **Fase 0 (servidor+cliente):** clase **28** (el campo de apariencia son 5 bits: `Number << 3 & 0xF8`; 30/31 reservados para generaciones siguientes; flag = bit 28). `RuneWizardClassFactory.AddTo` es la **única definición** de la clase (hereda fórmulas de mago; `CreationAllowedFlag=16`, `LevelRequirementByCreation=150`). Cliente: `CLASS_RUNEWIZARD`, `GetBaseClass(RW)=CLASS_WIZARD` (porque `MAX_CLASS=7` fija el formato de `item.bmd`/`skill.bmd`), skins 21–23 (`MODEL_BODY_NUM=24`), modelos/texturas de cuerpo de S21 (`Character/RuneWizardModels.*`, `Data/Player/*Class09/309/409.bmd`, `HQskinClass109/HQSkinClass309/HQSkinClass409.OZJ`). Escala/pose/bastón revisados en pantalla: «está bien».
- **Fase 1:** vista previa **solo en memoria** (`RuneWizardPreview`, `Startup/RuneWizardPreviewHook`, variable de entorno `OPENMU_RUNE_WIZARD_PREVIEW=1`): la clase 28 existe en RAM con `CanGetCreated=false`, creada con un contexto EF real **que nunca se guarda**. `/mobabot ... rw` (alias `rw`/`rune`/`runewizard`/28) y `/mobadummy rw`. Visto en pantalla.
- **Fase 3 (MOBA):** familia `MobaFamily.RuneWizard` (clases 28/30/31), pasiva «Marca rúnica» (3.er golpe de skill sobre el mismo objetivo en 5 s detona, +55 %), arquetipo, tienda (clon de la del Mago), retrato prestado del DW (`MobaMinimapKind`), bots.
- **Fase 4a (esta sesión):** skills propias **630–636** en el servidor (`RuneWizardSkillsInitializer`, `RuneWizardClassFactory.AddSkillsTo`): Magic Arrow 630, Plasma Ball 631, Lightning Storm 632, Burst 633 (buff propio +20 % daño mágico, efecto 168), Haste 634 (buff de grupo +20 vel. ataque, efecto 169), Runic Spear 635, Rune Phrase 636. El RW ya no califica para las skills del DW. Tablas MOBA (`MobaSkillDamage`, `MobaCooldowns`, `MobaBuffs`, `MobaBuffPower.RuneBurst`, `MobaLoadouts`, bots). Cliente: filas 630–636 en `skill_eng.bmd` (`tools/rw_skill_bmd.py`, idempotente, checksum clave 0x5A18, Bux `FC CF AB` por registro, 108 B/registro), `Character/RuneWizardSkills.*` (`IsRuneSkill`, `IsAreaSkill`, `VisualSkillOf`), ruta de cast por `MobaCastSkill` también fuera del MOBA. **Por ahora cada skill toma prestados la pose y el efecto de una skill de S6** (630→Energy Ball, 631→Twister, 632→Ice Storm, 633→Expansion, 634→Greater Damage, 635→Cometfall, 636→Evil Spirit) y el icono. Tests: 304/304 en los filtros `RuneWizard`+`Moba` (suite completa 1091 antes de la 4a). **Compilado y arrancado, pero NO visto en pantalla** (el usuario aún no lo probó).

## 5. Pendiente (en este orden sugerido)

1. **Creación de personajes (pedido actual)** — ver §6. Necesaria para que el usuario cree un RW y pruebe skills sin comandos.
2. **Efectos de nivel B** que sustituyan a los prestados: Runic Sphere (trío), DarkPlasmaBall, `rune_skillball`, forma de tornado, etc. (modelos en `Nexus MU Online\Data\Effect`, referencias en la carpeta de notas), con luz y sonido; Haste como buff real de velocidad de ataque visible; icono de buff de Burst/Haste (efectos 168/169 sin icono en el cliente); iconos de skill propios.
3. **Items propios:** Rune Mace(s) (grupo 2), escudos (grupo 6), **22 sets de S21** (grupos 7–11) con los números reales de S21 (`datos-rw/rw_items.tsv`, `rw_sets.tsv`), definiciones en servidor + modelos/`Item.bmd`/tooltips en cliente (`item_eng.bmd` 8192×84 B). Hoy el RW sigue vistiendo los items del DW (`RuneWizardPreview` los autoriza).
4. **Retrato propio** (8.ª celda del atlas de retratos del cliente; hoy usa el del DW), piel/animaciones propias si hace falta.
5. **MU normal persistente** (Fase 2): árbol maestro, quests de cambio de clase, mixes, etc., y los datos de las skills/efectos/items en la BD.
6. Condición de lanzamiento: decisión legal sobre assets de S21.

## 6. Creación de personajes — lo que hay que decidir/hacer

**Problema clave:** el servidor guarda la clase en la BD PostgreSQL (`openmu`). Un `Character` apunta a su `CharacterClass` por clave foránea, así que **crear un RW real exige que la clase 28 (y sus skills 630–636) existan en la BD**. La vista previa en memoria **no sirve** para esto (la entidad no se guarda). Por tanto crear personajes RW = **migración de BD** → regla de oro: **plan → `pg_dump` → confirmación explícita**.

**Plan propuesto (aún NO presentado ni aprobado):**
- Servidor: un plugin de actualización `AddRuneWizardClassUpdatePlugIn` en `src/Persistence/Initialization/Updates` (modelo: `AddKalimaPlugIn`, `AddElfSoldierBuffPlugIn`…) que llame a `RuneWizardClassFactory.AddTo(context, config, canGetCreated: true)` y `AddSkillsTo`; los plugins de actualización de OpenMU corren al arrancar sobre la configuración guardada. Añadir comando `/makechar rw` (ya existe `MakeCharChatCommandPlugIn`, hoy resuelve por alias) y el flag de creación `CreationAllowedFlag=16` en la cuenta (revisar cómo se desbloquean las clases por cuenta).
- **Opción más segura recomendada:** aplicar la actualización primero sobre **una copia de la BD** (`pg_dump` de `openmu` → `createdb openmu_rw` → restaurar → apuntar el servidor del worktree a esa copia), de modo que la BD real quede intacta hasta que el usuario decida migrarla. Pedir confirmación del plan antes de crear la copia o ejecutar nada.
- Cliente: la pantalla de creación (`Character/CharMakeWin.*`, `Character/CharSelMainWin.cpp`, `Scenes/CharacterScene.*`) tiene un selector de clases con modelos/animaciones/imágenes por clase. Hay que añadir la opción RW (clase 28): su imagen/retrato de selección, el modelo en pose de selección y los textos. Buscar en S21 (`Nexus MU Online\Data\Interface` y `Data\Player`, p. ej. imágenes `newui_*` de selección de clase, animaciones de la pantalla de personajes) lo que haga falta; las skins ya existen (Fase 0). Hay que revisar cómo el cliente decide el número de clases mostradas y el botón de «crear» (también mirar `Dotnet/PacketFunctions_ClientToServer` para el paquete de creación y qué byte de clase envía: el RW es 28 → debe caber en el campo del paquete).
- Probar: crear el personaje RW, entrar, usar las 7 skills (aprenderlas con `/learnskills` o por la lista de skills del servidor), ver daño y efectos.

**Investigación hecha y su resultado:** solo se listaron los archivos del cliente que tocan la creación (`CharMakeWin`, `CharSelMainWin`, `CharacterScene`, `UIMng`, `PacketFunctions_ClientToServer`, …) y los plugins `Updates/Add*PlugIn.cs` del servidor. **No se leyó ninguno en detalle ni se cambió nada.**

## 7. Cómo compilar, probar y arrancar

- **Servidor:** `dotnet build src/Startup/MUnique.OpenMU.Startup.csproj` en el worktree (en un worktree nuevo hay que compilar antes `src/Persistence/SourceGenerator`). Tests: `dotnet test tests/MUnique.OpenMU.Tests --filter "FullyQualifiedName~RuneWizard|FullyQualifiedName~Moba"`. La compilación modifica `src/Web/Map/wwwroot/js/MUnique.OpenMU.Web.Map.js`: restaurar con `git checkout -- <archivo>` antes de commitear.
- **Arrancar servidor de vista previa:** `$env:OPENMU_RUNE_WIZARD_PREVIEW='1'; .\tools\iniciar-todo.ps1 -SinPortal` desde el worktree (la ejecución puede tardar más de 5 min en consola: lanzarla en segundo plano). Parar: `.\tools\iniciar-todo.ps1 -Parar` (se niega si hay jugadores; no usar `-Forzar` sin avisar). **Parar antes el servidor normal** si está levantado (si no, el cliente RW crearía un BM por defecto: la clase 28 no existe ahí).
- **Cliente:** `C:\Users\aruiz\AppData\Local\Temp\buildclient-rw.bat` (VsDevCmd + `cmake --preset windows-x64` + `cmake --build --preset windows-x64-release`; si falta, recrearlo así). Salida: `mu-main-rw\out\build\windows-x64\src\Release\Main.exe`. Los assets de `src/bin` se copian al compilar (por eso el cliente debe estar cerrado). `config.ini` del usuario ya está copiado en esa carpeta. Lanzar con `Start-Process Main.exe -WorkingDirectory <esa carpeta>`.
- `src/bin/Data` está en `.gitignore` del cliente: los binarios nuevos se añaden con `git add -f` (los ya rastreados, como `skill_eng.bmd`, se actualizan normal).
- Script auxiliar `edit.py` (editar preservando BOM/CRLF) en el scratchpad de la sesión; si no está, recrearlo.
- **Cuidado:** nunca hacer `cat > archivo` sin entrada (truncó una vez `buildclient-rw.bat` y se colgó); usar la herramienta Write.

## 8. Conocimiento técnico clave

- **Skills:** server `Skill.Number` = índice de skill del cliente. `MAX_SKILLS=650`; el cliente lee `Data\Local\Eng\skill_eng.bmd` (650×108 B + 4 de checksum; slots libres ≥ 637 ahora; 618–629 también). Las skills del RW se crean con `RuneWizardSkillsInitializer` (hereda `SkillsInitializerBase`, `SkillNumber`/`MagicEffectNumber` internos de `Persistence.Initialization`).
- **Clases en memoria:** las colecciones respaldadas por EF rechazan entidades «InMemory»: crear siempre con `IPersistenceContextProvider.CreateNewContext()` y **no guardar** ese contexto (riesgo: guardar un contexto desde donde sea alcanzable la entidad la persistiría).
- **MOBA:** todo se indexa por número de skill (`MobaSkillDamage.Table`, `MobaCooldowns`, `MobaBuffs.DurationSeconds`, `MobaLoadouts` familias/Skills/Buffs/DefaultPicks, `MobaBotPlayer.CorePriority`). Los picks son exactamente 4 (`MobaLoadoutStore.Min/MaxSkills`). `MobaBuffs.StripGaugeCosts` quita el coste de AG a las skills «solo de mago/RW».
- **Cliente, despacho de skills:** `MobaCastSkill` (genérico, paquetes 0x19 objetivo / 0x1E área, `MobaIsAreaSkill`), recepción en `WSclient.cpp` (`ReceiveMagic`, `ReceiveMagicContinue`), efectos al soltar el cast en `MoveCharacter` (`ZzzCharacter.cpp`, tres `switch` sobre `c->Skill` ya envueltos en `VisualSkillOf`). Para efectos propios: añadir `case AT_SKILL_RUNE_*` con efecto propio (sistema data-driven `Render/Effects/EffectRegistry` + `Behaviors/`, precedente `MobaAegisEffect`) y quitar el alias en `VisualSkillOf`.
- **Datos S21:** cifrado Bux (XOR `FC CF AB`, continuo o reiniciado por registro); archivos `[u32 n][n registros][u32 checksum]`; BMD v15 se convierten con `tools/bmd_convert.py`; OZJ de S21 = cabecera de 24 bytes + JPEG. Clase RW en S21 = ranura 8; sus piezas son `…Class09/309/409`.

## 9. Prompt para empezar una conversación nueva

> Continuamos la integración del Rune Wizard (S21) en OpenMU/MuMain (MU normal + MOBA Eclipse Arena). Lee primero, enteros y en este orden: `C:\Users\aruiz\Proyectos\openmu-aram-rw\docs\RUNE_WIZARD_TRASPASO.md`, las secciones «Rune Wizard» de `C:\Users\aruiz\Proyectos\openmu-aram-rw\GAMEDESIGN.md` y `C:\Users\aruiz\Proyectos\mu-main-rw\docs\CODING_RULES.md` (antes de tocar C++). Trabaja en los worktrees `openmu-aram-rw` (rama `rw/phase4`) y `mu-main-rw` (rama `rw/phase4`); no uses `moba/phase1` ni `moba-camera`. Responde en español. Tarea actual: integrar el RW en la creación de personajes (cliente: pantalla de creación con su imagen/animación de S21; servidor: que la clase 28 y sus skills 630–636 existan en la BD) para que yo cree un RW y pruebe sus skills. Como eso exige migrar la BD, primero **preséntame el plan** (idealmente aplicado a una copia de la BD) y espera mi confirmación explícita antes de ejecutar nada ni hacer `pg_dump`. Avisa antes de reiniciar el servidor o recompilar el cliente (tengo que cerrar el mío). Haz commit y push al terminar cada paso y actualiza el traspaso y `GAMEDESIGN.md`.
