# F04 - PRESUPUESTO CON PESOS: resumen

Todos los numeros salen de corridas reales del simulador (`run.py --n 150000`, `robust.py`, desglose con `decompose()`). Archivos: `f04_presupuesto.py`, `resultados/f04_presupuesto_params.json`, `resultados/f04_presupuesto_informe.json`, `resultados/f04_presupuesto_perillas.json`.

## Puntaje

- **Final (150.000 trayectorias): 80.96 / 100.** Rapido (n=12.000, la misma medida que usa tune.py): 81,55. Antes de ajustar (diseno a mano, mismo n): 75,88. Referencias: f_real 45,7-47,9, f00_actual ~55.
- Recortes del simulador (`clips_harness_pct`): 0,0 %. Sin NaN. Elementos muertos (< 0,5 %): ninguno.

| Criterio | Peso | Valor (0-1) | Puntos |
|---|---|---|---|
| C1 cobertura | 20 | 0.951 | 19.0 |
| C2 duelos 40-60 % en cada nivel | 25 | 0.442 | 11.1 |
| C3 identidad tanque / dano | 10 | 1.0 | 10.0 |
| C4 ninguna celda manda | 5 | 0.831 | 4.2 |
| C5 duracion | 5 | 0.998 | 5.0 |
| C6 crecimiento por nivel | 5 | 0.891 | 4.5 |
| C7 sin topes | 5 | 1.0 | 5.0 |
| C8 transparencia | 5 | 1.0 | 5.0 |
| C9 paridad coste/efecto de items | 5 | 0.737 | 3.7 |
| C10 diferencia de nivel y oro | 5 | 1.0 | 5.0 |
| C11 3v3 con buffs | 10 | 0.86 | 8.6 |

### Detalle medido

- Cruces clase x arquetipo dentro de 40-60 % por nivel: nv1: 25.5 %, nv2: 27.6 %, nv3: 37.6 %, nv4: 39.5 %, nv5: 47.1 %, nv6: 69.3 %, nv8: 51.9 %, nv10: 58.1 %, nv12: 65.2 %, nv14: 64.8 %, nv16: 60.2 %, nv18: 52.1 %, nv20: 49.0 %, nv22: 45.2 %, nv25: 37.1 %, nv28: 33.8 %, nv30: 33.1 %.
- Duracion mediana (s): nv1: 4.4, nv2: 7.5, nv3: 9.8, nv4: 10.9, nv5: 11.4, nv6: 10.3, nv8: 10.2, nv10: 10.7, nv12: 11.0, nv14: 11.1, nv16: 11.1, nv18: 11.4, nv20: 12.1, nv22: 12.8, nv25: 13.9, nv28: 14.6, nv30: 14.9. Partidas sin muerto en 90 s: 0.03 %.
- Identidad: vida efectiva del tanque = 2.0 x la del de dano; dano del de dano = 1.89 x el del tanque; tanque contra dano gana 56.2 %.
- Extremos nivel 30: dps p99,9 = 2.187 x mediana, vida efectiva p99,9 = 2.374 x, mitigacion maxima 0.71.
- Nivel/oro: con 2-3 niveles menos gana 22.1 % (chance de remontar 5-50 % en el 92.0 % de los cruces); con 20 % menos oro gana 32.6 %.
- 3v3: composiciones en 40-60 %: 0.767; valor de los buffs de equipo: 1.0.
- Winrate clase contra clase nivel 16: todas entre 0,41 y 0,60 (la peor FE contra GM 0,41; la mejor GM contra SUM 0,60).

## La formula en texto

Cada personaje junta un **presupuesto** repartido en fondos. Cada elemento aporta `peso_propio x valor_saturado` (valor = `sat(x,k)=x(1+k)/(x+k)` de su entrada normalizada: stat/30.000, nodo/100, tier/3, poder de mascota, rango de buff/5) a uno o varios fondos:

| Fondo | Que junta | Que produce |
|---|---|---|
| OFF | stat principal (afinidad 1) / secundario / otros, ofensa del arbol, items (generico + rol ATAQUE), arma, mascota ataque, buffs de dano | `frac` (mezcla de la rotacion) y `pw` (potencia) |
| CRI | AGI, nodo critico, rol ATAQUE, IncCrit, IgnoreDefense | probabilidad de critico y penetracion |
| VIDA | VIT, nodo vida, rol DEFENSA, mascota vital, SwellLife, IncreaseHealth, Berserker, escudo de SoulBarrier/Defense | vida |
| DUR | VIT, nodos defensa y durabilidad, rol DEFENSA, escudo, mascota guardia, Defense, GreaterDefense, IncreaseBlock, Reflection | mitigacion fisica / magica y menos dano recibido |
| SOS | rol RECURSOS, mascota vital, Heal, Recovery | regeneracion y vida al golpear |
| CDR / MANA | rol UTILIDAD, mascota veloz / nodo de mana | reduccion de enfriamiento y regeneracion de mana de la rotacion |

**Reparto suave:** `O = OFF+CRI`, `D = rho (VIDA+DUR+SOS)`, `s = 1/(1+(D/O)^gamma)`, `O' = s (O+D)`, `D' = (1-s)(O+D)/rho`; cada fondo se reescala en su lado (gamma > 1 especializa: el de dano pierde defensa y el tanque pierde ofensa; el total se conserva).

**Ofensa:** `frac = 1-exp(-OFF'/Ko)`; `pw = 1 + Ap (1-exp(-OFF'/Kp))`; `critico = 1 + cmax (1-exp(-CRI'/Kc)) (cm-1)`; la rotacion real de la clase sale de `kits.rotation(frac, cdr, mana)`; `dps = exp(gd (L-1)) L^-kd x dmg_clase x tilt x rotacion x sostenido x critico x pw` (el sostenido por mana ya va dentro del dps y la rafaga se corrige para que no se cuente dos veces). `pen = 0,98 m0 (1-exp(-(pen_crit prob.crit + pen_ign IgnoreDefense)))`, siempre menor que la mitigacion base.

**Defensa:** `hp = H1 exp(gh (L-1)) L^-kh x tilt x hp_clase x (1 + Ah (1-exp(-VIDA'/Kh)))`; `mit_fis = m0 + (1-m0)(1-exp(-DUR'/Km))`; `mit_mag` igual con `mu x DUR'` (mu < 1: contra el mago el tanque aguanta menos); `dano_recibido = exp(-DUR'/Kd)`; `regen = hp rr SOS'/(SOS'+Ks)`; `vida al golpear = ls SOS'/(SOS'+Ks)`; `escudo = sh0 hp`; `reflejo = refl x Reflection/5`.

Los limites salen de la forma (1-exp, x/(x+k), exponenciales de entradas acotadas): no hay `min`/`clip` en la formula.

## Reparto en % de un golpe (personaje de dano, desde `decompose()`)

| Factor | Que es | Nivel 5 | Nivel 15 | Nivel 30 |
|---|---|---|---|---|
| nivel | exp(gd (L-1)): escala base de dano por nivel | 12.96 % | 20.03 % | 29.22 % |
| kit_clase | dano base de las 6 skills reales de la clase a rango 1 (kits.py) | 46.65 % | 26.54 % | 23.68 % |
| rango_y_cadencia | subida por rango de skill + enfriamiento y mana (rotacion y sostenido) | 2.98 % | 8.57 % | 6.9 % |
| reparto_ofensivo | parte del dano que sale de la especializacion (softmax ofensa/defensa) | 2.39 % | 1.86 % | 1.23 % |
| ofensa_stats | stats que van al fondo OFF (principal, secundario, AGI, CMD) | 9.81 % | 6.89 % | 4.83 % |
| ofensa_arbol | nodos de ofensa del arbol | 2.64 % | 2.39 % | 2.34 % |
| ofensa_arma | arma (base + rol + genericos) | 2.66 % | 3.21 % | 2.71 % |
| ofensa_piezas | las otras 10 ranuras de items | 15.91 % | 24.77 % | 23.46 % |
| ofensa_aliados | mascotas y buffs de dano | 0.16 % | 1.24 % | 1.27 % |
| critico | 1 + prob. critica x (mult - 1) | 3.83 % | 4.51 % | 4.35 % |

A nivel 30 los 10 factores pesan 1-45 % (C8 = 1,0). A nivel 5 `kit_clase` pesa 46,7 % (sobre el 45 %) y `ofensa_aliados` 0,16 % (no hay buffs antes del nivel 6); el simulador solo mide el nivel maximo.

## Perillas (valor final y efecto de moverla +15 %)

Efecto = cambio de la mediana de dano / vida efectiva del nivel 30 en la clase mas afectada (robust.py). Peor caida de puntaje al mover una perilla +-15 %: **-4,92** (dmg_FE -15 %); caida media -0,83.

| Perilla | Valor | Cambio de puntaje (+15 % / -15 %) | Dano % | Vida efectiva % |
|---|---|---|---|---|
| kd | 0.9 | -1.93 / -2.30 | -36.82 | +0.00 |
| kh | 0.1 | -0.87 / -0.39 | +0.00 | -4.97 |
| w_STR | 0.2991 | -0.36 / +0.21 | +1.69 | -0.25 |
| w_AGI | 0.3167 | -0.40 / -0.43 | +2.58 | -0.53 |
| w_ENE | 0.3703 | -0.19 / -0.37 | +1.88 | -0.43 |
| w_VIT | 0.4531 | -0.56 / -0.59 | -0.56 | +2.56 |
| w_CMD | 0.5995 | -0.12 / -0.14 | +2.86 | -0.70 |
| w_arbol_ofensa | 0.3615 | -0.17 / -0.04 | +0.96 | -0.24 |
| w_arbol_defensa | 0.3585 | -0.30 / -0.25 | -0.16 | +1.07 |
| w_arbol_critico | 0.6631 | -0.05 / -0.26 | +0.60 | -0.20 |
| w_arbol_durabilidad | 0.36 | +0.08 / +0.02 | -0.21 | +1.21 |
| w_arbol_vida | 0.7118 | -0.04 / +0.05 | -0.39 | +0.93 |
| w_arbol_mana | 0.09933 | +0.01 / +0.01 | +0.58 | +0.00 |
| w_item_arma | 0.2453 | -0.26 / -0.26 | +1.52 | +0.50 |
| w_item_escudo | 0.2292 | +0.11 / +0.15 | +0.67 | +1.19 |
| w_item_casco | 0.3058 | -0.29 / -0.19 | +0.91 | +1.19 |
| w_item_armadura | 1.079 | -0.19 / -0.05 | +2.10 | +3.43 |
| w_item_pantalon | 0.5145 | -0.28 / +0.22 | +1.46 | +1.80 |
| w_item_guantes | 0.379 | -0.24 / +0.01 | +1.13 | +1.30 |
| w_item_botas | 0.3342 | -0.43 / -0.04 | +1.01 | +0.99 |
| w_item_alas | 0.524 | -0.43 / -0.44 | +1.00 | +1.79 |
| w_item_colgante | 0.2679 | -0.21 / -0.01 | +0.76 | +0.65 |
| w_item_anillo1 | 0.2898 | -0.14 / +0.09 | +0.90 | +1.09 |
| w_item_anillo2 | 0.3399 | -0.05 / -0.14 | +0.64 | +1.04 |
| w_mascota_ataque | 0.0741 | -0.02 / +0.16 | +0.32 | -0.09 |
| w_mascota_guardia | 0.04438 | +0.01 / -0.06 | -0.04 | +0.29 |
| w_mascota_vital | 0.08053 | +0.00 / -0.03 | -0.05 | +0.21 |
| w_mascota_veloz | 0.09469 | +0.01 / +0.01 | +0.35 | -0.03 |
| w_buff_SoulBarrier | 0.06748 | -0.03 / +0.02 | -0.05 | +0.32 |
| w_buff_Expansion | 0.05781 | +0.06 / -0.03 | +0.18 | -0.02 |
| w_buff_Defense | 0.06332 | -0.19 / +0.17 | -0.02 | +0.34 |
| w_buff_SwellLife | 0.07883 | -0.01 / -0.08 | -0.08 | +0.15 |
| w_buff_Heal | 0.09775 | +0.02 / -0.03 | -0.09 | +0.23 |
| w_buff_GreaterDefense | 0.06792 | +0.00 / -0.13 | -0.02 | +0.22 |
| w_buff_GreaterDamage | 0.04605 | -0.03 / -0.18 | +0.09 | -0.03 |
| w_buff_InfinityArrow | 0.06357 | -0.04 / -0.16 | +0.09 | -0.04 |
| w_buff_IncCrit | 0.1167 | -0.02 / -0.20 | +0.24 | -0.12 |
| w_buff_Reflection | 0.09011 | +0.01 / -0.03 | +0.01 | +0.32 |
| w_buff_Berserker | 0.1355 | +0.00 / -0.04 | +0.12 | +0.12 |
| w_buff_IgnoreDefense | 0.1002 | +0.02 / -0.06 | +0.35 | -0.08 |
| w_buff_IncreaseHealth | 0.0729 | +0.02 / -0.10 | -0.05 | +0.24 |
| w_buff_IncreaseBlock | 0.06672 | -0.01 / +0.02 | -0.05 | +0.26 |
| w_buff_Recovery | 0.0896 | +0.01 / -0.14 | -0.06 | +0.33 |
| sat_stat | 0.5501 | -0.02 / +0.16 | -0.29 | -0.12 |
| sat_tree | 1 | -0.19 / -0.11 | -0.29 | -0.64 |
| afin_sec | 0.2238 | -0.01 / +0.05 | +0.39 | -0.05 |
| afin_otro | 0.09105 | -0.02 / -0.01 | +0.59 | -0.08 |
| agi_crit | 0.8 | -0.48 / -0.47 | +1.07 | -0.32 |
| vit_vida | 0.55 | +0.02 / +0.08 | +0.00 | -1.04 |
| item_generico | 0.4 | +0.18 / -0.59 | +2.63 | +0.70 |
| arma_base | 0.5 | +0.09 / -0.42 | +0.89 | -0.16 |
| escudo_dur | 0.2884 | +0.04 / +0.12 | -0.08 | +0.56 |
| gamma | 1.562 | -0.94 / -0.40 | -2.19 | +1.77 |
| rho | 1 | -0.12 / -1.96 | -4.14 | +3.71 |
| Ko | 4 | -0.15 / -0.97 | -4.88 | +0.00 |
| Ap | 2 | -0.41 / -0.59 | +7.71 | +0.00 |
| Kp | 3.5 | +0.14 / -0.94 | -4.73 | +0.00 |
| Kc | 1.288 | -0.46 / -0.34 | -1.51 | +0.00 |
| cmax | 0.5 | -0.06 / -0.19 | +2.12 | +0.00 |
| cm | 1.75 | -0.17 / -0.97 | +4.66 | +0.00 |
| cdr_max | 0.45 | +0.18 / +0.07 | +2.52 | +0.00 |
| K_cdr | 0.7447 | +0.10 / +0.14 | +1.75 | +0.00 |
| mb_max | 0.9675 | +0.01 / +0.01 | +0.70 | +0.00 |
| K_mana | 0.6576 | +0.00 / +0.01 | -0.46 | +0.00 |
| gd | 0.04362 | -0.69 / -0.03 | +20.89 | +0.00 |
| gh | 0.0266 | -0.76 / +0.04 | +0.00 | +12.27 |
| H1 | 670 | +0.06 / -0.89 | +0.00 | +15.00 |
| Ah | 1.2 | -0.69 / -0.35 | +0.00 | +5.97 |
| Kh | 2.218 | -0.58 / -0.33 | +0.00 | -3.57 |
| Km | 2.8 | -0.19 / -1.53 | +0.00 | -6.05 |
| mu | 0.7685 | -0.85 / -0.75 | +0.00 | +3.41 |
| Kd | 6.408 | -0.15 / -0.88 | +0.00 | -3.17 |
| rr | 0.02242 | +0.03 / +0.01 | +0.00 | +1.86 |
| Ks | 0.7804 | +0.04 / +0.17 | +0.00 | -0.65 |
| ls | 0.12 | +0.04 / -0.29 | +0.00 | +0.00 |
| sh0 | 0.1087 | -0.15 / -0.41 | +0.00 | +1.45 |
| pen_crit | 0.6407 | +0.01 / +0.04 | +0.00 | +0.00 |
| pen_ign | 0.7156 | +0.04 / -0.05 | +0.00 | +0.00 |
| m0 | 0.08606 | -0.44 / -0.39 | +0.00 | +1.41 |
| refl | 0.06 | +0.01 / +0.00 | +0.00 | +0.00 |
| dmg_BK | 0.8727 | -2.97 / -3.66 | +15.00 | +0.00 |
| hp_BK | 0.8727 | -3.60 / -3.97 | +0.00 | +15.00 |
| dmg_RF | 0.9295 | -3.21 / -3.09 | +15.00 | +0.00 |
| hp_RF | 0.9295 | -2.63 / -1.98 | +0.00 | +15.00 |
| dmg_DL | 0.9587 | -3.23 / -3.58 | +15.00 | +0.00 |
| hp_DL | 0.9524 | -3.72 / -3.83 | +0.00 | +15.00 |
| dmg_MG | 1.135 | -3.09 / -4.02 | +15.00 | +0.00 |
| hp_MG | 1.135 | -2.95 / -3.88 | +0.00 | +15.00 |
| dmg_FE | 1.068 | -1.96 / -4.92 | +15.00 | +0.00 |
| hp_FE | 1.068 | -2.50 / -4.30 | +0.00 | +15.00 |
| dmg_GM | 1.097 | -3.28 / -2.77 | +15.00 | +0.00 |
| hp_GM | 1.097 | -2.11 / -2.19 | +0.00 | +15.00 |
| dmg_SUM | 0.9759 | -1.75 / -3.48 | +15.00 | +0.00 |
| hp_SUM | 0.9759 | -2.53 / -4.07 | +0.00 | +15.00 |

Perillas que no mueven dano ni vida efectiva (< 0,1 %): w_buff_GreaterDamage, w_buff_InfinityArrow, ls, pen_crit, pen_ign, refl. `ls`, `pen_crit`, `pen_ign`, `refl` actuan solo dentro del duelo (no entran en dano ni vida efectiva); `w_buff_GreaterDamage` y `w_buff_InfinityArrow` no mueven la mediana porque la mediana de personajes no tiene el buff puesto (en C1 y en el 3v3 si cuentan).

## Como se llego (honesto)

1. Diseno a mano con `q.py`-style de pruebas a n pequeno: pase de 56 a 75 puntos arreglando recortes (la penetracion se limita por la mitigacion base `m0`), el factor `pw`, los rangos de saturacion y la calibracion de pesos.
2. **Calibracion de pesos por elemento** (script propio fuera de la carpeta del proyecto): cada peso se escala hasta que subir UNA unidad estandar de ese elemento en el personaje de referencia mueva ~2-3 % (items proporcional al coste de la ranura, buffs ~2 %). Hay una condicion de existencia (`x e^-x = t S / G`): si el objetivo por elemento es mayor (~4-7 %) los fondos se saturan y los pesos divergen.
3. **tune.py:** una ronda (`--iters 30 --pop 8 --n 9000`) se corto tras 11 iteraciones (~7,5 min) sin ninguna mejora: con ~92 perillas, el 30 % cambia a la vez con pasos grandes y casi todos los candidatos empeoran. Despues use una busqueda propia (cambia 1-3 perillas por candidato, mismo n=9000 y misma semilla que tune.py): 160 + 220 + 220 candidatos; el puntaje rapido paso de 76,3 a 82,8 (semilla 11), con una recalibracion de constantes por clase (iterativa, converge a ~50 % por clase) al final. Las constantes por clase (`dmg_*`, `hp_*`) quedaron entre 0,87 y 1,13.
4. Un cambio de forma importante: la escala de nivel `exp(gd(L-1)) L^-kd` (dano algo menor al principio): sin el, el poder crecia 12-20 % por nivel en niveles bajos y quien tenia 2-3 niveles menos ganaba solo 8 % (C10 = 0,5); con el, 22 % (C10 = 1,0).

## Debilidades honestas

- C2 (duelos 40-60 %) solo 0,44: a niveles 25-30 solo el 33-37 % de las 420 parejas queda en 40-60 % (el de dano pierde frente al tanque/mixto, ~25 % de victorias en nivel 30) y en niveles 1-2 solo el 26-28 % (todos los personajes son casi iguales, solo cambia la clase).
- La calibracion que mantiene C1 (cada elemento ~2-3 %) neutraliza los cambios de 'rango' de los canales: si se sube la ofensa maxima, los pesos se reajustan y la deriva tanque/dano a nivel alto vuelve. Es una tension entre C1 y C2, no la resolvi.
- Siete + siete constantes por clase (`dmg_*`, `hp_*`) son un ajuste por clase, no una ley general; sin ellas la clase mas fuerte (BK) ganaba 71 % y la mas debil (MG) 29 %. El balance entre clases depende del ajuste, no de la forma.
- Algunos elementos de bajo coste quedan bajo el 1,5 % en C1 (anillos 0,8-0,95 %, colgante 1,1 %, guantes 1,0 %): C1 = 0,951, C9 (paridad) = 0,737.
- El reparto ofensa/defensa por razon con gamma genera efectos cruzados: subir un elemento ofensivo baja un 0,3-1,1 % la vida efectiva (y al reves). Es el 'intercambio' buscado, pero C1 lo mide con valor absoluto.
- No uso `pct_dps` ni `true_dps`: el veneno / sangrado no estan modelados; 'contra magia el tanque se siente' solo viene de `mu` (mitigacion magica menor) y de la penetracion por critico. No hay control (`cc_uptime`) ni `tenacity` modelados.
- C6 = 0,891: crecimiento por nivel fuera de 3-15 % en algun tramo (no investigue en cual; con el diseno anterior el crecimiento temprano era 12-20 % por nivel).
- Transparencia de C8 depende del nivel: a nivel 5 `kit_clase` pesa 46,7 % y `ofensa_aliados` 0,16 %. La escala `L^-kd` es un factor < 1 y no aparece en el desglose.
- Ruido de medicion: una misma formula da 80,6-81,6 segun semilla y n (rapido vs completo); las diferencias < 1,5 puntos no son significativas.
- Posible error del simulador (no tocado): en `fight` se cuenta como recorte cualquier mitigacion efectiva `mit - pen < 0`, asi que cualquier penetracion mayor que la mitigacion del rival penaliza C7; obliga a que `pen < mitigacion minima` (aqui `m0`). Ademas `bump_score` en C1 usa el valor absoluto, de modo que un elemento ofensivo puede 'cumplir' bajando la defensa.

## Ideas para una formula hibrida

- Fondos con peso propio por elemento y reparto suave ofensa/defensa (razon con exponente gamma): da identidad de tanque/dano (vida x2,0 y dano x1,9) casi sin tocar nada mas.
- Calibrar los pesos automaticamente con la regla 'una unidad estandar de cada elemento mueve ~2-3 %' (script de calibracion): deja C1 en ~1,0 desde el primer intento y hace que todos los buffs, piezas de vida, SwellLife, IncreaseHealth, Berserker, Heal y Recovery sirvan a la vez con rendimientos decrecientes naturales.
- Items con parte generica (cada pieza sirve sea cual sea su rol) + parte por rol: la mediana de personajes nota cada ranura.
- Usar `kits.rotation` con `frac` del fondo, enfriamiento y mana de los fondos CDR/MANA, y plegar el sostenido dentro del dps.
- Penetracion acotada por la mitigacion base (`pen < m0`): cero recortes.
- Escala de nivel `exp(g(L-1)) L^-k` para que el poder crezca ~5-7 % por nivel y los cruces de 2-3 niveles sean 22 %.
- Desglose de golpe agrupando las contribuciones de los fondos por grupo (stats, arbol, arma, piezas, aliados) repartiendo el log del factor del fondo segun lo que aporta cada grupo.
