# Registro de skills del MOBA

Generado por `tools/skills_registro.py` a partir del código (no se edita a mano: se vuelve a generar). Orden: por clase y, dentro de cada una, por número de skill.

**Cómo leer las columnas**

- **Daño r1 / r5**: daño plano de la tabla `MobaSkillDamage` a rango 1 y rango 5 (ya con el multiplicador global ×1,5), antes de la escala por nivel, stats e ítems. **Escala stat** = fracción extra de daño con el stat principal al máximo (se multiplica por 2,5 en el código; 0,55 → hasta +137 %).
- **CD r1 / r5**: enfriamiento en segundos a rango 1 y 5 (sin entrada propia usa 6,5 → 4,5 s). **DPS r1 / r5** = daño ÷ CD (solo para comparar skills entre sí; la ventana de gracia de 3 s permite encadenar un lanzamiento extra).
- **Maná**: coste de MU × 3 (regla del MOBA). **Alcance** en casillas (valor del config de MU).
- **Elegible**: la skill entra en las 4-6 que el jugador elige; los **buffs** y el Teleport los tiene siempre; las **invocaciones** del Elfa son apariencias de una misma 5.ª skill.

## Dark Wizard / Soul Master / Grand Master

Picks por defecto (si el jugador no eligió): 9 Evil Spirit, 39 Ice Storm, 5 Flame, 8 Twister, 12 Aqua Beam, 14 Inferno.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Poison | DirectHit | Ataque elegible | 6 | 126 | 105 / 201 | 0.45 | 6.5 / 4.5 s (por defecto) | 16 / 45 | — | elemento Poison |
| 2 | Meteorite | DirectHit | Ataque elegible | 6 | 36 | 142 / 286 | 0.60 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Earth |
| 3 | Lightning | DirectHit | Ataque elegible | 6 | 45 | 128 / 254 | 0.55 | 7 / 5 s | 18 / 51 | — | elemento Lightning |
| 4 | Fire Ball | DirectHit | Ataque elegible | 6 | 9 | 120 / 240 | 0.55 | 6 / 4 s | 20 / 60 | — | elemento Fire |
| 5 | Flame | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 128 / 254 | 0.55 | 6.5 / 4.5 s (por defecto) | 20 / 56 | — | elemento Fire |
| 6 | Teleport | Other | Buff | 6 | 90 | — | — | 1 / 1 s | — | — |  |
| 7 | Ice | DirectHit | Ataque elegible | 6 | 114 | 128 / 248 | 0.50 | 9 / 6 s | 14 / 41 | — | elemento Ice |
| 8 | Twister | AreaSkillAutomaticHits | Ataque elegible | 6 | 180 | 90 / 174 | 0.40 | 6.5 / 4.5 s (por defecto) | 14 / 39 | — | elemento Wind |
| 9 | Evil Spirit | AreaSkillAutomaticHits | Ataque elegible | 7 | 270 | 142 / 286 | 0.55 | 8 / 6 s | 18 / 48 | — |  |
| 10 | Hellfire | AreaSkillAutomaticHits | Ataque elegible | 4 | 480 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Fire |
| 11 | Power Wave | DirectHit | Ataque elegible | 6 | 15 | 82 / 154 | 0.40 | 4 / 3 s | 21 / 52 | — |  |
| 12 | Aqua Beam | AreaSkillAutomaticHits | Ataque elegible | 6 | 420 | 135 / 267 | 0.50 | 6.5 / 4.5 s (por defecto) | 21 / 59 | — | elemento Water |
| 13 | Cometfall | AreaSkillAutomaticHits | Ataque elegible | 3 | 450 | 150 / 300 | 0.60 | 6.5 / 4.5 s (por defecto) | 23 / 67 | — |  |
| 14 | Inferno | AreaSkillAutomaticHits | Ataque elegible | 4 | 600 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Fire |
| 16 | Soul Barrier | Buff | Buff | 6 | 210 | — | — | 75 / 55 s | — | 60 s |  |
| 17 | Energy Ball | DirectHit | Ataque elegible | 6 | 3 | 68 / 128 | 0.35 | 2 / 1.5 s | 34 / 85 | — |  |
| 38 | Decay | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 98 / 188 | 0.40 | 6.5 / 4.5 s (por defecto) | 15 / 42 | — | elemento Poison |
| 39 | Ice Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 150 / 294 | 0.60 | 6.5 / 4.5 s (por defecto) | 23 / 65 | — | elemento Ice |
| 40 | Nova | DirectHit | Ataque elegible | 6 | 0 | 180 / 360 | 0.80 | 6.5 / 4.5 s (por defecto) | 28 / 80 | — | elemento Fire |
| 45 | Lance | DirectHit | Ataque elegible | 6 | 0 | 82 / 160 | 0.35 | 6.5 / 4.5 s (por defecto) | 13 / 36 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 233 | Expansion of Wizardry | Buff | Buff | 6 | 600 | — | — | 120 / 90 s | — | 90 s |  |

## Dark Knight / Blade Knight / Blade Master

Picks por defecto (si el jugador no eligió): 19 Falling Slash, 22 Cyclone, 41 Twisting Slash, 42 Rageful Blow, 43 Death Stab, 232 Strike of Destruction.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 18 | Defense | Buff | Buff | ? | 90 | — | — | 70 / 50 s | — | 45 s |  |
| 19 | Falling Slash | DirectHit | Ataque elegible | 3 | 27 | 90 / 174 | 0.40 | 2.5 / 1.7 s | 36 / 102 | — | se lanza sobre el objetivo, empuja |
| 20 | Lunge | DirectHit | Ataque elegible | 2 | 27 | 105 / 201 | 0.40 | 5 / 3 s | 21 / 67 | — | se lanza sobre el objetivo, empuja |
| 21 | Uppercut | DirectHit | Ataque elegible | 2 | 24 | 150 / 300 | 0.55 | 8 / 6 s | 19 / 50 | — | se lanza sobre el objetivo, empuja |
| 22 | Cyclone | DirectHit | Ataque elegible | 2 | 27 | 135 / 267 | 0.50 | 7 / 5 s | 19 / 53 | — | se lanza sobre el objetivo, empuja |
| 23 | Slash | DirectHit | Ataque elegible | 2 | 30 | 98 / 188 | 0.40 | 3 / 2 s | 32 / 94 | — | se lanza sobre el objetivo, empuja |
| 41 | Twisting Slash | AreaSkillAutomaticHits | Ataque elegible | 2 | 30 | 128 / 248 | 0.45 | 6 / 4 s | 21 / 62 | — | elemento Wind |
| 42 | Rageful Blow | AreaSkillAutomaticHits | Ataque elegible | 3 | 0 | 172 / 340 | 0.75 | 6.5 / 4.5 s (por defecto) | 27 / 76 | — | elemento Earth |
| 43 | Death Stab | DirectHit | Ataque elegible | 2 | 0 | 188 / 368 | 0.80 | 6.5 / 4.5 s (por defecto) | 29 / 82 | — | elemento Wind |
| 44 | Crescent Moon Slash | DirectHit | Ataque elegible | 4 | 0 | 142 / 280 | 0.50 | 6.5 / 4.5 s (por defecto) | 22 / 62 | — | se lanza sobre el objetivo, empuja |
| 47 | Impale | DirectHit | Ataque elegible | 3 | 24 | 142 / 280 | 0.50 | 6.5 / 4.5 s (por defecto) | 22 / 62 | — |  |
| 48 | Swell Life | Buff | Buff | ? | 66 | — | — | 120 / 90 s | — | 90 s |  |
| 49 | Fire Breath | DirectHit | Ataque elegible | 3 | 27 | 142 / 280 | 0.50 | 6.5 / 4.5 s (por defecto) | 22 / 62 | — |  |
| 67 | Stun | AreaSkillAutomaticHits | Ataque elegible | 2 | 210 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 232 | Strike of Destruction | AreaSkillAutomaticHits | Ataque elegible | 5 | 0 | 202 / 394 | 0.85 | 6.5 / 4.5 s (por defecto) | 31 / 88 | — | elemento Ice |

## Fairy Elf / Muse Elf / High Elf

Picks por defecto (si el jugador no eligió): 24 Triple Shot, 51 Ice Arrow, 52 Penetration, 235 Multi-Shot, 67 Stun, 76 Plasma Storm.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 24 | Triple Shot | AreaSkillAutomaticHits | Ataque elegible | 6 | 15 | 57 / 111 | 0.35 | 2 / 1.6 s | 28 / 69 | — |  |
| 26 | Heal | Regeneration | Buff | 6 | 60 | — | — | 10 / 6 s | — | instantáneo |  |
| 27 | Greater Defense | Buff | Buff | 6 | 90 | — | — | 75 / 55 s | — | 60 s |  |
| 28 | Greater Damage | Buff | Buff | 6 | 120 | — | — | 75 / 55 s | — | 60 s |  |
| 30 | Summon Goblin | SummonMonster | Invocación | ? | 120 | — | — | 45 / 25 s | — | — |  |
| 31 | Summon Stone Golem | SummonMonster | Invocación | ? | 210 | — | — | 45 / 25 s | — | — |  |
| 32 | Summon Assassin | SummonMonster | Invocación | ? | 330 | — | — | 45 / 25 s | — | — |  |
| 33 | Summon Elite Yeti | SummonMonster | Invocación | ? | 480 | — | — | 45 / 25 s | — | — |  |
| 34 | Summon Dark Knight | SummonMonster | Invocación | ? | 600 | — | — | 45 / 25 s | — | — |  |
| 35 | Summon Bali | SummonMonster | Invocación | ? | 750 | — | — | 45 / 25 s | — | — |  |
| 36 | Summon Soldier | SummonMonster | Invocación | ? | 1050 | — | — | 45 / 25 s | — | — |  |
| 51 | Ice Arrow | DirectHit | Ataque elegible | 8 | 0 | 162 / 318 | 0.70 | 6.5 / 4.5 s (por defecto) | 25 / 71 | — | elemento Ice |
| 52 | Penetration | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 128 / 254 | 0.50 | 6 / 4 s | 21 / 63 | — | elemento Wind |
| 67 | Stun | AreaSkillAutomaticHits | Ataque elegible | 2 | 210 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 77 | Infinity Arrow | Buff | Buff | 6 | 150 | — | — | 60 / 40 s | — | 130 s |  |
| 234 | Recovery | Regeneration | Buff | 6 | 120 | — | — | 14 / 9 s | — | instantáneo |  |
| 235 | Multi-Shot | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 138 / 270 | 0.50 | 6.5 / 4.5 s (por defecto) | 21 / 60 | — |  |

## Magic Gladiator / Duel Master

Picks por defecto (si el jugador no eligió): 22 Cyclone, 41 Twisting Slash, 55 Fire Slash, 56 Power Slash, 9 Evil Spirit, 14 Inferno.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Poison | DirectHit | Ataque elegible | 6 | 126 | 105 / 201 | 0.45 | 6.5 / 4.5 s (por defecto) | 16 / 45 | — | elemento Poison |
| 2 | Meteorite | DirectHit | Ataque elegible | 6 | 36 | 142 / 286 | 0.60 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Earth |
| 3 | Lightning | DirectHit | Ataque elegible | 6 | 45 | 128 / 254 | 0.55 | 7 / 5 s | 18 / 51 | — | elemento Lightning |
| 4 | Fire Ball | DirectHit | Ataque elegible | 6 | 9 | 120 / 240 | 0.55 | 6 / 4 s | 20 / 60 | — | elemento Fire |
| 5 | Flame | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 128 / 254 | 0.55 | 6.5 / 4.5 s (por defecto) | 20 / 56 | — | elemento Fire |
| 7 | Ice | DirectHit | Ataque elegible | 6 | 114 | 128 / 248 | 0.50 | 9 / 6 s | 14 / 41 | — | elemento Ice |
| 9 | Evil Spirit | AreaSkillAutomaticHits | Ataque elegible | 7 | 270 | 142 / 286 | 0.55 | 8 / 6 s | 18 / 48 | — |  |
| 11 | Power Wave | DirectHit | Ataque elegible | 6 | 15 | 82 / 154 | 0.40 | 4 / 3 s | 21 / 52 | — |  |
| 14 | Inferno | AreaSkillAutomaticHits | Ataque elegible | 4 | 600 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Fire |
| 17 | Energy Ball | DirectHit | Ataque elegible | 6 | 3 | 68 / 128 | 0.35 | 2 / 1.5 s | 34 / 85 | — |  |
| 18 | Defense | Buff | Buff | ? | 90 | — | — | 70 / 50 s | — | 45 s |  |
| 19 | Falling Slash | DirectHit | Ataque elegible | 3 | 27 | 90 / 174 | 0.40 | 2.5 / 1.7 s | 36 / 102 | — | se lanza sobre el objetivo, empuja |
| 22 | Cyclone | DirectHit | Ataque elegible | 2 | 27 | 135 / 267 | 0.50 | 7 / 5 s | 19 / 53 | — | se lanza sobre el objetivo, empuja |
| 41 | Twisting Slash | AreaSkillAutomaticHits | Ataque elegible | 2 | 30 | 128 / 248 | 0.45 | 6 / 4 s | 21 / 62 | — | elemento Wind |
| 55 | Fire Slash | AreaSkillAutomaticHits | Ataque elegible | 2 | 0 | 135 / 267 | 0.50 | 6.5 / 4.5 s (por defecto) | 21 / 59 | — | elemento Fire |
| 56 | Power Slash | AreaSkillAutomaticHits | Ataque elegible | 5 | 45 | 128 / 248 | 0.45 | 6.5 / 4.5 s (por defecto) | 20 / 55 | — |  |
| 57 | Spiral Slash | DirectHit | Ataque elegible | 5 | 0 | 142 / 280 | 0.50 | 6.5 / 4.5 s (por defecto) | 22 / 62 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 236 | Flame Strike | AreaSkillAutomaticHits | Ataque elegible | 3 | 0 | 158 / 314 | 0.60 | 6.5 / 4.5 s (por defecto) | 24 / 70 | — | elemento Fire |
| 237 | Gigantic Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 165 / 333 | 0.65 | 6.5 / 4.5 s (por defecto) | 25 / 74 | — | elemento Wind |

## Dark Lord / Lord Emperor

Picks por defecto (si el jugador no eligió): 60 Force, 61 Fire Burst, 66 Force Wave, 65 Electric Spike, 78 Fire Scream, 22 Cyclone.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 18 | Defense | Buff | Buff | ? | 90 | — | — | 70 / 50 s | — | 45 s |  |
| 19 | Falling Slash | DirectHit | Ataque elegible | 3 | 27 | 90 / 174 | 0.40 | 2.5 / 1.7 s | 36 / 102 | — | se lanza sobre el objetivo, empuja |
| 20 | Lunge | DirectHit | Ataque elegible | 2 | 27 | 105 / 201 | 0.40 | 5 / 3 s | 21 / 67 | — | se lanza sobre el objetivo, empuja |
| 21 | Uppercut | DirectHit | Ataque elegible | 2 | 24 | 150 / 300 | 0.55 | 8 / 6 s | 19 / 50 | — | se lanza sobre el objetivo, empuja |
| 22 | Cyclone | DirectHit | Ataque elegible | 2 | 27 | 135 / 267 | 0.50 | 7 / 5 s | 19 / 53 | — | se lanza sobre el objetivo, empuja |
| 23 | Slash | DirectHit | Ataque elegible | 2 | 30 | 98 / 188 | 0.40 | 3 / 2 s | 32 / 94 | — | se lanza sobre el objetivo, empuja |
| 60 | Force | DirectHit | Ataque elegible | 4 | 30 | 75 / 147 | 0.40 | 2.5 / 2 s | 30 / 74 | — |  |
| 61 | Fire Burst | DirectHit | Ataque elegible | 6 | 75 | 150 / 306 | 0.55 | 6.5 / 4.5 s (por defecto) | 23 / 68 | — |  |
| 63 | Summon | Other | Invocación | ? | 210 | — | — | 6.5 / 4.5 s (por defecto) | — | — |  |
| 64 | Increase Critical Damage | Buff | Buff | ? | 150 | — | — | 90 / 65 s | — | 60 s |  |
| 65 | Electric Spike | AreaSkillAutomaticHits | Ataque elegible | 10 | 0 | 165 / 333 | 0.65 | 6.5 / 4.5 s (por defecto) | 25 / 74 | — |  |
| 66 | Force Wave | DirectHit | Ataque elegible | 4 | 30 | 90 / 174 | 0.40 | 6.5 / 4.5 s (por defecto) | 14 / 39 | — |  |
| 74 | Fire Blast | DirectHit | Ataque elegible | 6 | 0 | 142 / 280 | 0.50 | 8 / 5 s | 18 / 56 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 78 | Fire Scream | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 150 / 300 | 0.55 | 6.5 / 4.5 s (por defecto) | 23 / 67 | — |  |

## Summoner / Bloody Summoner / Dimension Master

Picks por defecto (si el jugador no eligió): 214 Drain Life, 215 Chain Lightning, 223 Explosion, 224 Requiem, 225 Pollution, 230 Lightning Shock.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | Meteorite | DirectHit | Ataque elegible | 6 | 36 | 142 / 286 | 0.60 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Earth |
| 4 | Fire Ball | DirectHit | Ataque elegible | 6 | 9 | 120 / 240 | 0.55 | 6 / 4 s | 20 / 60 | — | elemento Fire |
| 7 | Ice | DirectHit | Ataque elegible | 6 | 114 | 128 / 248 | 0.50 | 9 / 6 s | 14 / 41 | — | elemento Ice |
| 11 | Power Wave | DirectHit | Ataque elegible | 6 | 15 | 82 / 154 | 0.40 | 4 / 3 s | 21 / 52 | — |  |
| 45 | Lance | DirectHit | Ataque elegible | 6 | 0 | 82 / 160 | 0.35 | 6.5 / 4.5 s (por defecto) | 13 / 36 | — |  |
| 76 | Plasma Storm | AreaSkillAutomaticHits | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 214 | Drain Life | AreaSkillExplicitTarget | Ataque elegible | 6 | 150 | 120 / 240 | 0.45 | 8 / 6 s | 15 / 40 | — |  |
| 215 | Chain Lightning | AreaSkillExplicitTarget | Ataque elegible | 6 | 255 | 128 / 248 | 0.45 | 6.5 / 4.5 s (por defecto) | 20 / 55 | — |  |
| 217 | Damage Reflection | Buff | Buff | 5 | 120 | — | — | 75 / 55 s | — | 45 s |  |
| 218 | Berserker | Buff | Buff | 5 | 300 | — | — | 90 / 65 s | — | 60 s |  |
| 219 | Sleep | Buff | Ataque elegible | 6 | 60 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 221 | Weakness | Buff | Ataque elegible | 6 | 150 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 222 | Innovation | Buff | Ataque elegible | 6 | 210 | 105 / 213 (defecto) | 0.50 | 6.5 / 4.5 s (por defecto) | 16 / 47 | — |  |
| 223 | Explosion | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Fire |
| 224 | Requiem | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Wind |
| 225 | Pollution | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 128 / 248 | 0.45 | 6.5 / 4.5 s (por defecto) | 20 / 55 | — | elemento Lightning |
| 230 | Lightning Shock | AreaSkillAutomaticHits | Ataque elegible | 6 | 0 | 142 / 286 | 0.55 | 6.5 / 4.5 s (por defecto) | 22 / 64 | — | elemento Lightning |
| 234 | Recovery | Regeneration | Buff | 6 | 120 | — | — | 14 / 9 s | — | instantáneo |  |

## Rage Fighter / Fist Master

Picks por defecto (si el jugador no eligió): 260 Killing Blow, 261 Beast Uppercut, 262 Chain Drive, 263 Dark Side, 264 Dragon Roar, 265 Dragon Slasher.

| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 19 | Falling Slash | DirectHit | Ataque elegible | 3 | 27 | 90 / 174 | 0.40 | 2.5 / 1.7 s | 36 / 102 | — | se lanza sobre el objetivo, empuja |
| 260 | Killing Blow | DirectHit | Ataque elegible | 2 | 27 | 180 / 360 | 0.75 | 6.5 / 4.5 s (por defecto) | 28 / 80 | — | elemento Earth |
| 261 | Beast Uppercut | DirectHit | Ataque elegible | 2 | 27 | 165 / 333 | 0.60 | 6.5 / 4.5 s (por defecto) | 25 / 74 | — | elemento Fire |
| 262 | Chain Drive | DirectHit | Ataque elegible | 4 | 45 | 150 / 300 | 0.55 | 6.5 / 4.5 s (por defecto) | 23 / 67 | — | elemento Ice |
| 263 | Dark Side | DirectHit | Ataque elegible | 4 | 210 | 202 / 400 | 0.85 | 6.5 / 4.5 s (por defecto) | 31 / 89 | — | elemento Wind |
| 264 | Dragon Roar | AreaSkillExplicitTarget | Ataque elegible | 3 | 150 | 150 / 294 | 0.55 | 6.5 / 4.5 s (por defecto) | 23 / 65 | — | elemento Earth |
| 265 | Dragon Slasher | DirectHit | Ataque elegible | 4 | 300 | 150 / 294 | 0.55 | 6.5 / 4.5 s (por defecto) | 23 / 65 | — | elemento Wind |
| 266 | Ignore Defense | Buff | Buff | 3 | 150 | — | — | 70 / 50 s | — | 45 s |  |
| 267 | Increase Health | Buff | Buff | 7 | 150 | — | — | 120 / 90 s | — | 90 s |  |
| 268 | Increase Block | Buff | Buff | 7 | 150 | — | — | 90 / 65 s | — | 60 s |  |
| 269 | Charge | DirectHit | Ataque elegible | 4 | 0 | 135 / 267 | 0.50 | 6.5 / 4.5 s (por defecto) | 21 / 59 | — |  |
| 270 | Phoenix Shot | AreaSkillExplicitTarget | Ataque elegible | 4 | 90 | 165 / 333 | 0.60 | 6.5 / 4.5 s (por defecto) | 25 / 74 | — | elemento Earth |

## Lectura rápida de equilibrio (solo con la tabla, sin probar en juego)

DPS de rango 1 mediano entre las skills con daño: **22**. Skills muy por encima (×1,6 o más) o muy por debajo (×0,5 o menos):

| Skill | DPS r1 | Respecto a la mediana | CD por defecto |
|---|---|---|---|
| 19 Falling Slash | 36 | ×1.64 | no |

Skills ofensivas del kit **sin entrada en la tabla de daño** (usan 70 + 18 por rango por defecto): 67 Stun, 76 Plasma Storm, 219 Sleep, 221 Weakness, 222 Innovation.
