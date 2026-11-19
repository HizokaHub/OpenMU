# BALANCE_FACTORES — todo lo que influye en el balance de un duelo (estado real del código, 2026-11-18)

Generado leyendo el código del servidor (`src/GameLogic/PlugIns/Moba`, commit `f45fa6b77`). Cifras en %, con su tope. **«Hoy» = lo que está en el código**; lo que el usuario decidió el 2026-11-13 pero aún no está implementado va marcado **[decidido, sin implementar]**.

## 0. La fórmula de balance (la tuya)
**`fracción = 0,45 × stats + 0,20 × árbol + 0,35 × ítems`** (0 a 1), la misma mezcla para tres cosas:
- **Daño** (`MobaSkillDamage.BlendedFraction`): stats = puntos invertidos en el stat primario de la clase ÷ 30.000; árbol = puntos en nodos de daño ÷ min(100, 80 % de la capacidad de la clase); ítems = fracción de ítems de ataque (arma y escudo/libro, ver §5).
- **Mitigación** (`MobaDefense.FinalMitigationOf`): stats = 12 % + 58 % × VIT/(VIT + 12.000); árbol = nodos de defensa × 70 %; ítems = fracción de ítems de defensa × 70 %.
- **Crítico** (`MobaCombatStats.FinalCritChanceOf`): stats = 60 % × AGI/(AGI + 14.000); árbol = nodos de crítico × 60 %; ítems = Luck nativo; **+ el crítico de las piezas** (sumado después de la mezcla).
- Si el personaje no tiene nodos de ese tipo en el árbol, la mezcla es `(0,45 stats + 0,35 ítems) ÷ 0,80`.

**Daño de un golpe de skill** = `(Base + PorRango × (rango−1)) × 1,5 × (1 + fortalecedor) × (1 + Expansion) × (1 + poder de skill de las piezas) × (1 + ratio × 2,5 × fracción) × EscalaNivel × tilt de clase`, con ±10 % de variación.
- `ratio` = el «MaxStatBonus» de la skill (0,35 a 0,85) × (1 + 0,6 × (rango−1)/4): a rango 5 la skill escala ×1,6 más con la fracción.
- Ataque básico: `(45 + 4 × (nivel−1)) × 1,5 × (1 + 0,6 × 2,5 × fracción) × EscalaNivel × tilt`.
- **Después** del daño: defensa plana (§2), crítico ×1,75, mitigación, durabilidad, tope por golpe y freno de ráfaga (§2).

## 1. Base por nivel y por clase (`MobaProgression`, nivel 1 → 30, lineal)
| Elemento | Nivel 1 | Nivel 30 | Nota |
|---|---|---|---|
| Vida | 1.900 | 50.000 × tilt | tilt de vida por clase (abajo) |
| Maná | 450 | 3.400 | igual para todas |
| SD (escudo) | vida ÷ 3 | 16.667 × tilt | 25 % de la durabilidad total |
| Defensa plana (P1) | 15 | 400 | resta fija contra campeones, venga de quien venga |
| Escala de daño | ×1 | ×15 | multiplica TODO el daño de skills y básicos |

| Clase | Tilt de vida | Tilt de daño | Vida nv30 | Stat primario | Alcance básico |
|---|---|---|---|---|---|
| BK | ×1,30 | ×1,02 | 65.000 | Fuerza | 2 casillas |
| RF | ×1,30 | ×1,02 | 65.000 | Fuerza | 2 |
| DL | ×1,15 | ×0,95 | 57.500 | Mando | 4 |
| MG | ×1,00 | ×1,00 | 50.000 | Energía | 3 |
| Elfa | ×0,90 | ×1,08 | 45.000 | Agilidad | 6 |
| Mago/GM | ×0,90 | ×1,10 | 45.000 | Energía | 6 |
| Summoner | ×0,90 | ×1,10 | 45.000 | Energía | 6 |

## 2. Cómo se resta el daño (en este orden, `MobaDefense.Apply`)
1. Defensa plana de nivel (400 a nv30) restada al daño base. [P1]
2. **Penetración por skill** (reduce la mitigación): Penetration 55 %, Death Stab 40 %, Strike of Destruction 35 %, Dark Side 35 %, Electric Spike 30 %, Phoenix Shot 30 %, Rageful Blow 20 %.
3. **Mitigación final** = mezcla 0,45/0,20/0,35 (§0). **Tope 70 %.**
4. **Durabilidad** = 1 − (1 − árbol) × (1 − piezas): árbol hasta −15 % de daño; piezas del set de recuperación (BK/RF: +10 %; DL +6 %; MG +6 %; Elfa/Mago/Summoner 0 %); accesorio Resistencia −3 % cada uno (T3).
5. **Crítico:** ×1,75 (+0,15 → +0,35 con Increase Critical Damage).
6. **Tope por golpe: 30 % de la vida máxima** del objetivo.
7. **Freno de ráfaga** (ventana de 2 s): hasta el 30 % de la vida cuenta entero; del 30 al 50 % cuenta 50 %; más del 50 % cuenta 20 %.
8. Daño especial que ignora parte de la defensa: Death Stab 6 % de vida máx.; Strike of Destruction 5 %; Dragon Roar 9 % de la vida actual; Dragon Slasher 7 % actual; Killing Blow 5 % actual; Evil Spirit 4 % máx.
9. Vampirismo (cura al golpear): BK/RF 14 % del daño básico; Elfa 12 %; MG/DL 10 %; casters 8 %; en skills ×0,35.

## 3. Stats (`MobaStatEconomy`)
- **Tope 30.000 por stat** (Fuerza, Agilidad, Energía, Vitalidad, Mando). Nivel máximo 30 (29 subidas).
- Puntos totales a repartir: **100.000** (normal), **112.000** (RF y Summoner), **130.000** (DL); ≈ 3.448 / 3.862 / 4.483 por nivel.
- Nadie llega a 120.000 (4 stats) ni 150.000 (DL con 5): hay que elegir.
- Qué hace cada stat en el MOBA: **stat primario → daño** (÷30.000, entra con 45 % a la fórmula); **Agilidad → crítico** (hasta 60 %, la mitad en 14.000 puntos); **Vitalidad → mitigación** (12 % a 70 %, la mitad del camino en 12.000 puntos). La vida y la SD que dan los stats se recortan al tope de §6.
- Bots: 35 % primario / 35 % VIT / 30 % AGI.

## 4. Árbol Master (`MobaMasterTree`)
- **5 puntos por nivel** (~145 en total); rangos 1-2 libres, el rango r pide (r−2)×10 puntos en la rama.
- Daño: **100 puntos = fracción completa** (o el 80 % de la capacidad de la clase si es menor). Capacidad de nodos de daño por clase: Mago 100, Elfa 100, DL 140, BK 160, Summoner 180, MG 200, RF 80.
- Fortalecedor de skill: **+1,5 % de daño por punto, máx. +45 %** en esa skill.
- Durabilidad: hasta **−15 %** de daño recibido. Égida: **+25 %** de tamaño. Vida/maná máximos: nodos propios (hoy recortados por el tope de §6).
- Recuperación al matar un creep: **2 % → 10 %** de vida/maná/SD (lineal, hasta 20 puntos).

## 5. Ítems (`MobaItemPower`, `MobaArchetype`, `MobaItemTraits`)
Cada tier vale un tercio del valor completo: **T1 = 33 %, T2 = 67 %, T3 = 100 %** del valor T3 de la tabla.
- **Fracción ofensiva** = suma de tiers de arma + escudo/libro ÷ 6 (Elfa y RF: solo el arma ÷ 3).
- **Fracción defensiva** = suma de tiers de casco, armadura, pantalón, guantes, botas, alas, colgante, 2 anillos ÷ (9 × 3). MG no cuenta casco y RF no cuenta guantes (P3): llegan al 100 % con lo que pueden llevar.
- **Rasgos de pieza a T3** (set de **Recuperación** del BK/RF; otras clases cambian el rasgo del casco): casco vida +12 %; armadura y pantalón durabilidad +5 % cada uno; guantes y botas regeneración de vida 0,7 %/s cada uno; arma vida +5 % y regeneración 0,4 %/s.
  - Mago/Summoner Recuperación: casco maná +20 %; armadura y pantalón regeneración de maná +12 % cada uno; guantes regeneración 0,5 %/s; botas vida +5 %; arma maná +10 % y regeneración de maná +8 %.
  - Elfa: casco y armadura poder de curación +10 % cada uno; pantalón vida +8 %; guantes regeneración 0,5 %/s; botas regeneración de maná +10 %; arma curación +6 % y regeneración de maná +6 %.
  - DL: casco vida +8 %; armadura durabilidad +6 %; pantalón regeneración de maná +12 %; guantes y botas regeneración 0,6 %/s; arma vida +5 % y regeneración de maná +6 %.
  - MG (sin casco): armadura maná +10 %; pantalón durabilidad +6 %; guantes regeneración 0,6 %/s; botas regeneración de maná +10 %; arma vida +5 % y regeneración de maná +6 %.
- **Set de Ataque** (igual para todas): armadura y pantalón poder de skill +5 % cada uno; guantes y botas crítico +3 % cada uno; arma poder de skill +6 % y crítico +3 %; casco +4 % de vida (BK/RF/Elfa), +8 % de maná (casters), +8 % regeneración de maná (DL), sin casco el MG (+5 % maná). Total del set de ataque: **poder de skill +16 %, crítico +12 %**.
- **Accesorios** (2 anillos y 1 colgante, valor a T3): Ofensivo +4 % poder de skill / +2 % crítico; Resistencia −3 % daño / +3 % vida; Recursos +10 % regeneración de maná / +0,3 % regeneración de vida; Utilidad −6 % de enfriamiento.
- **Opciones MOBA de piezas** (T1 / T2 / T3): arma anticurar 10 / 20 / 30 % (3 s) y perforación de SD 5 / 10 / 15 %; armadura y pantalón resistencia a control 5 / 10 / 15 % cada uno (tope total 50 %) y guardia de SD 2 / 4 / 6 % cada uno; guantes y botas enfriamiento 6 / 12 / 20 % cada uno (tope total 65 %); alas ignoran el escudo 3 / 6 / 10 % de probabilidad; escudo: resistencia a control 10 / 15 / 20 %, enfriamiento 5 / 15 / 25 %, oro.
- **Escudo y Égida:** el escudo T3 levanta una Égida del 20 % de la vida cada 40 s (T1 60 s, T2 50 s, T3 40 s).
- Bots: no compran mascotas; el DL con caballo y cuervo mete ≈ 25 % más de daño (cuervo ×0,37).

## 6. Vida, maná, SD y regeneración — TOPES (`MobaItemCaps`)
| Elemento | Tope hoy | Qué entra en el tope |
|---|---|---|
| Vida máxima | **curva × tilt × 1,20** | stats (VIT) + piezas (`HealthPool`) + nodos de vida + **Swell Life** + cualquier otro buff de vida |
| SD máxima | curva × tilt × 1,20 + barrera temporal | stats + Égida y barreras de skill quedan fuera del tope |
| Maná máximo | curva × 1,20 | stats + piezas + nodos |
| Velocidad de ataque | 70 | ítems, Frenesí del RF (+8 por golpe, 5 cargas = +40) |
| Reducción de maná | 40 % | árbol |
| Crítico final | 60 % | mezcla + piezas |
| Golpe excelente | 50 % | ítems |
| Mitigación final | 70 % | mezcla |
| Enfriamiento | 65 % | guantes, botas, escudo, utilidad |
| Resistencia a control | 50 % | armadura, pantalón, escudo, Increase Block |

- Regeneración de vida: suma de rasgos de pieza (0,3 a 0,7 %/s de la vida máxima por pieza, ≈ 1,8 %/s de la vida máxima con el set de Recuperación completo del BK).
- Maná: regeneración base 4 % → 12 % del pozo; el coste de las skills es ×3 el de S6.
- **Hallazgo:** a nivel 30 los stats ya llegan solos al tope +20 % de vida, así que **las piezas de vida y el Swell Life hoy dan 0**.

## 7. Buffs de vida (los que mueven la vida o la cura)
| Buff | Quién | Objetivo | Hoy | **[Decidido, sin implementar]** (rango 1 → 5) |
|---|---|---|---|---|
| Swell Life (48) | BK | equipo ≤ 10 | +12 % vida máx., 90 s, enfriamiento 120 → 90 s; recortado por el tope | +12 → +24 % |
| Increase Health (267) | RF | equipo ≤ 10 | +133 de vida (0,2 %) | +10 → +15 % |
| Berserker (218) | Summoner | uno mismo | −32 % vida y +0 de daño (mal) | +15 → +20 % vida, +5 → +10 % daño |
| Heal (26) | Elfa | aliado | +105 de vida y 5.400 de escudo | cura 5 → 10 % de la vida máx. |
| Recovery (234) | Elfa, Summoner | aliado | +525 de escudo | cura 20 % de la vida máx. (fijo) |
| Égida | cualquiera con escudo | uno mismo | 20 % de la vida cada 40 s | — |

## 8. Pasivos de clase
| Clase | Pasivo | Efecto |
|---|---|---|
| BK | Segundo aliento | al bajar del 40 % de vida: −20 % daño recibido 4 s y 15 % de vampirismo, enfriamiento 14 s |
| RF | Frenesí | +8 de velocidad de ataque por golpe (máx. 5 cargas); a las 5, aturde 0,6 s; ventana de 3 s |
| Mago/GM | Combustión | cada hechizo deja quemadura (12 por tick, hasta 3 cargas, 4 s) |
| Elfa | Marca del cazador | su básico marca 4 s: todo el equipo hace +8 % de daño a ese objetivo |
| MG | Impulso híbrido | tras un hechizo, el siguiente básico en 3 s hace +40 %; hechizos cuestan 70 % de maná |
| Summoner | Eco espiritual | cada 4.º hechizo lanza un rayo extra (60 %) |
| BK/RF | Tenacidad | control duro dura ×0,70; DL ×0,85 |

## 9. Control y debuffs ya vigentes
- Control duro (aturdir, congelar, dormir) tope **1,4 s**; congelar tope **0,65 s**; rendimientos decrecientes por control seguido: **100 % → 60 → 36 → 22 → 15 %** (se reinicia a los 7 s).
- Debuffs actuales: ver GAMEDESIGN «Lista de debuffs HOY». **[Decidido, sin implementar]:** veneno 3 %/s de vida máx. (5 → 10 s, 60 %), sangrado 4 %/s (6 → 9 s, 60 %), lentos −25 → −45 % (1 → 3 s), Weakness −12 % de daño, Innovation −13 % de defensa, Fire Slash −25 %, Beast Uppercut −15 %.
- Área: **[decidido]** 100 % al principal y 65 % al resto; multigolpe ×1,25.

## 10. Lo demás que pesa en una partida (fuera del duelo 1v1)
Curva de oro por fase (×1 / ×1 / ×2,6), precios por tier (T2 = 2,5 × T1, T3 = 6,25 × T1), tiempo de reaparición (7 s → 25,8 s), creeps con vida y daño según nivel, daño de campeones a estructuras normalizado por la escala de nivel, Vision v1.
