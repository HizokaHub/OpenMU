# F01 - Multiplicativa por cubos: resumen

Archivo: `tools/balance_sim/f01_cubos.py` (valores finales de las perillas ya escritos como iniciales de PARAMS; copia en `resultados/f01_cubos_params.json`).
Informe completo: `resultados/f01_cubos_informe.json`. Perillas: `resultados/f01_cubos_perillas.json`.

## Puntaje (todo medido, `run.py --n 150000`, semilla 1, 6 millones de duelos)
**Final adoptado: 85,68 / 100.** Recortes del simulador: 0,0 %. Sin NaN.

| Criterio | Peso | Valor (0-1) | Puntos |
|---|---|---|---|
| C1 cobertura de los 41 elementos | 20 | 0,941 | 18,8 |
| C2 duelos 40-60 % en cada nivel | 25 | 0,749 | 18,7 |
| C3 identidad tanque / dano | 10 | 1,000 | 10,0 |
| C4 ninguna celda manda | 5 | 0,959 | 4,8 |
| C5 duracion de peleas | 5 | 0,982 | 4,9 |
| C6 crecimiento por nivel | 5 | 0,470 | 2,4 |
| C7 sin topes | 5 | 1,000 | 5,0 |
| C8 transparencia | 5 | 1,000 | 5,0 |
| C9 paridad coste/efecto de items | 5 | 0,578 | 2,9 |
| C10 diferencia de nivel y de oro | 5 | 0,936 | 4,7 |
| C11 equipos 3v3 con buffs | 10 | 0,850 | 8,5 |

Trayectoria del puntaje (cada paso medido):
- Diseno a mano, antes de ajustar: 75,62 (evaluacion rapida de `tune.py`, n=12000, semilla 11; 75,2 con otra semilla).
- Ronda 1 de tune (40 it x 8): 81,93 rapido. **Descartada**: el ajuste inflo buffs y mascotas (IncreaseHealth +274 %, IgnoreDefense +100 % sobre un solo elemento) para aleatorizar los duelos y bajar los extremos; eso rompe C1 y no es un diseno honesto. Se acotaron los rangos de buffs y mascotas.
- Ronda 2 (rangos acotados): 83,26 rapido.
- Ronda 3 (con cotas minimas para que ningun elemento quede muerto, duelos mas largos): 84,26 rapido; con n=150000 y los valores de esa ronda: **87,49** (pero peleas de 1,9 s en el nivel 30).
- Ajuste manual final (hp_unit 4000 y hp_lvl_geo 0,8) para que las peleas duren >= 4 s en el nivel 30: **85,68**. Se pierde 1,8 puntos a cambio de peleas realistas (ver debilidades).
- Total: 3 rondas de tune.py (120 iteraciones).

Detalle medido: cruces en banda 40-60 % por nivel: 23 % (nivel 1), 18 % (2), 43 % (3), 63 % (4), 72 % (5), 73 % (8), 75 % (10), 83 % (16), 87 % (20), 79 % (30); cruces extremos (<25 o >75 %): 0-1 % desde el nivel 4. Tanque contra dano: 54,4 %. Vida efectiva tanque/dano 2,46; dano dano/tanque 2,10. Menos nivel (2-3) gana 18,2 % y puede remontar en el 92 % de los cruces; con 20 % menos de oro gana 34,8 %. 3v3: composiciones 75 % en banda; el equipo con buffs gana entre 55 y 72 % (puntaje 1,0). Extremos del nivel 30: dano p99,9 = 2,67 x mediana, vida efectiva 2,97 x, mitigacion maxima 35 %.

## Formula (en texto)
Notacion: `xl = (nivel-1)/29`; `S(x,k) = x(1+k)/(x+k)` (saturacion normalizada: S(0)=0, S(1)=1, sigue creciendo sin tope con rendimientos decrecientes); `SB(x,k) = x/(x+k)` (acotada en [0,1), para fracciones). Un cubo es `exp(A x S(puntos))`, o sea ganancia de A (en logaritmo) al llenarlo.

**Dano por segundo (un golpe = producto de cubos)**

`dps = U x (1+Llin xl) x exp(Lgeo xl) x ROT x ENF x ID x C_stats x C_arbol x C_items x C_mascota x C_buffs x CRIT`

- `ROT`: rotacion real de las 6 skills por clase (`kits.rotation`, que ya trae el rango de skills), normalizada: `kit_propio^ks x media_de_clases^(1-ks)`.
- `ENF` = enfriamiento: `cdr = cdr_max x SB(piezas de UTILIDAD + mascota veloz + nodo de mana + AGI)`; el kit lo convierte en mas dano por segundo.
- `ID`: identidad de clase `id_dps_clase x tilt_dano^pow`.
- `C_stats = exp(A_stat x S(peso_stat_principal x stat/30000 + sec_w x peso_secundario x stat_sec/30000))`.
- `C_arbol = exp(tr_ofensa x S(nodo_ofensa/100))`.
- `C_items = exp(A_it x S(suma de las 11 ranuras: valor_ranura x coste x afinidad_de_poder x rol x tier/3))`. Una pieza fuera de su rol aporta `role_base` (30 %).
- `C_mascota = exp(pet_ataque x S(poder))`; el Mando amplifica mascotas y buffs `(1 + cmd_amp x S(CMD))`.
- `C_buffs = exp(A_buff x S(Expansion + GreaterDamage + Berserker))` x (1 + InfinityArrow x parte de basicos).
- `CRIT = 1 + p x (cm-1)`, `p = Pc_max x SB(AGI + nodo critico + piezas de critico + IncCrit)`, `cm = cm0 + cm_buff x IncCrit`.
- Dano verdadero: `dps x (IgnoreDefense + Reflection)` mas la parte de los criticos que ignora la mitigacion (`crit_true x p`, se desplaza, no se suma).
- Salidas del kit: `burst_mult`, `aoe_share`, `sustain` (con mana: `sustain + (1-sustain) x SB(ENE + nodo mana + piezas)`).

**Defensa**
- `vida = H x (1+Hlin xl) x exp(Hgeo xl) x id_hp x tilt_vida^pow x exp(A_hp x S(x_vida))` con UN solo cubo donde SUMAN todas las fuentes de vida: `x_vida = w_vit VIT + tr_vida nodo vida + piezas DEFENSA + pet_vital x mascota vital + SwellLife + IncreaseHealth + Berserker`. Asi apilan con rendimientos decrecientes naturales y ninguna queda en cero.
- `mitigacion = Mmax x SB(VIT + STR + nodo defensa + piezas + GreaterDefense)`; la magia ignora la fraccion `mag_bypass` de esa mitigacion (el tanque nota al rival magico).
- `dano_recibido = exp(-A_dt x S(nodo durabilidad + piezas + mascota guardia + Defense + SoulBarrier + IncreaseBlock))`.
- `escudo = vida x (sh0 + A_sh x S(piezas + SoulBarrier + Defense))`; `regen = vida x R_max x SB(piezas de recursos + Heal + Recovery + mascota vital + VIT)`; `vampirismo = ls_max x SB(piezas)`.

## Reparto en % de un golpe (desde `decompose`, personajes de dano, 20.000 por nivel, medido)
| Factor | Significado | Nivel 5 | Nivel 15 | Nivel 30 |
|---|---|---|---|---|
| nivel_base | base de nivel lineal x geometrica | 20,0 | 26,7 | 34,2 |
| rango_de_skills | rango de las skills (1-5) | 5,7 | 8,9 | 5,9 |
| kit_de_skills | rotacion real de la clase | 26,1 | 16,0 | 15,3 |
| identidad_de_clase | peso de clase (id x tilt) | 8,9 | 4,4 | 3,3 |
| enfriamiento | cdr de piezas, mascota, arbol, AGI | 2,2 | 2,8 | 2,2 |
| cubo_stats | stat principal (y secundario) | 10,4 | 9,2 | 7,9 |
| cubo_arbol | nodo de ofensa | 6,3 | 6,5 | 7,5 |
| cubo_items | 11 ranuras, poder de skill | 15,7 | 20,4 | 19,0 |
| cubo_mascota+buffs+verdadero | mascota ataque, Expansion, GreaterDamage, Berserker, IgnoreDefense, Reflection | 0,04 | 1,1 | 1,2 |
| critico | probabilidad x multiplicador | 4,6 | 4,2 | 3,6 |

(Los 10 factores estan entre 1 y 45 % en el nivel 30, que es donde puntua C8. En el nivel 5 no hay buffs ni mascotas, por eso ese factor casi no pesa.) El factor `nivel_base` esta en unidades relativas: el dps real tiene ademas una constante global oculta (`U`, la rotacion se divide entre 100).

## Perillas (valores finales; efecto = lo que hacen al subirlas)
Hay 113 perillas en `PARAMS`; las principales:
- `dps_unit` 0,95 / `hp_unit` 4000: escala global de dano / vida (duracion de las peleas). `dps_lvl_lin` 3,2 / `dps_lvl_geo` 0,85 / `hp_lvl_geo` 0,8: crecimiento por nivel de dano y vida.
- `kit_strength` 1,43: cuanto pesan las diferencias reales de kit entre clases. `id_dps_*` y `id_hp_*` (7+7): peso de cada clase en dano y vida (valores entre 0,79 y 1,58).
- Stats: `stat_dmg_STR/AGI/ENE/VIT/CMD` = 0,84 / 0,81 / 0,50 / 0,64 / 1,14 (peso de cada stat en el dano), `A_stat_dmg` 0,56, `w_vit_hp` 0,41, `w_vit_mit` 0,97.
- Arbol: `tr_ofensa` 0,72, `tr_defensa` 0,94, `tr_critico` 2,74, `tr_durabilidad` 0,8, `tr_vida` 0,2, `tr_mana` 1,19 (uno por nodo).
- Items: `it_arma` 2,75, `it_escudo` 1,69, `it_casco` 2,07, `it_armadura` 3,04, `it_pantalon` 1,33, `it_guantes` 1,86, `it_botas` 4,25, `it_alas` 2,71, `it_colgante` 2,45, `it_anillo1` 2,38, `it_anillo2` 3,94 (valor de cada ranura), `A_it_pow` 0,93.
- Mascotas: `pet_ataque` 0,012, `pet_guardia` 0,73, `pet_vital` 0,096, `pet_veloz` 1,32.
- Buffs (una por buff, 15): `bf_SoulBarrier` 0,05, `bf_Expansion` 0,042, `bf_Defense` 0,028, `bf_SwellLife` 0,037, `bf_Heal` 0,23, `bf_GreaterDefense` 0,40, `bf_GreaterDamage` 0,13, `bf_InfinityArrow` 0,52, `bf_IncCrit` 0,17, `bf_Reflection` 0,05, `bf_Berserker` 0,05, `bf_IgnoreDefense` 0,02, `bf_IncreaseHealth` 0,03, `bf_IncreaseBlock` 0,11, `bf_Recovery` 0,13.
- Vida y defensa: `A_hp` 1,28 (ganancia log maxima del cubo de vida), `Mmax` 0,43 (mitigacion maxima), `A_dt` 0,50, `mag_bypass` 0,11, `cdr_max` 0,34, `Pc_max` 0,54, `crit_true` 0,25.

Prueba de perillas (`robust.py`, +-15 %): **peor caida de puntaje 2,63 puntos, caida media 0,31** (menos de 5 en todas). Las que mas mueven el dano o la vida sin romper nada: `dps_unit`, `hp_unit`, `A_hp`, `A_it_pow`, `A_stat_dmg`, `A_dt`, `id_*`. Las mas delicadas para el puntaje: `id_dps_FE` (-2,63), `id_dps_BK` (-2,05), `id_dps_RF` (-2,34), `A_hp` (+15 % cae 1,48).
No mueven el dano ni la vida efectiva (<0,1 %) al nivel 30: `w_ene_mana`, `w_it_mana`, `bf_SoulBarrier`, `bf_Recovery`, `crit_true`, `ls_max`, `k_ls`. Actuan en el duelo (sostenido, escudo, curacion, dano verdadero), pero el medidor de perillas solo mira dano sin mitigar y vida efectiva, asi que no se ve.

## Debilidades honestas
1. **C6 (0,47) es el punto mas flojo**: el poder no crece parejo (la vida y el dano suben a ritmos distintos; las peleas bajan de 27 s en el nivel 1 a 4 s en el 30). Con el ajuste puro de la ronda 3 las peleas del nivel 30 duran 1,9 s (inaceptable para un juego) y el puntaje era 87,49; alargarlas costo 1,8 puntos y no se resolvio del todo: la formula no tiene un mecanismo que mantenga el cociente dano/vida constante por nivel.
2. **Niveles 1-3**: en 1 y 2 solo hay diferencias de clase (sin stats ni items) y 66 % / 61 % de los cruces son extremos; C2 solo puntua desde el nivel 3, pero el juego real tiene esos niveles.
3. **C9 paridad de items 0,58**: las ranuras baratas (guantes, botas, anillos, colgante) y las caras no rinden en proporcion a su coste; el ajuste lo sacrifico por C2. Hay items cerca de los limites: guantes (1,2 / 1,0 %), mascota de ataque 1,1 %, nodo de critico 1,0 %, nodo de mana 1,4 % (bajo el 1,5 % de C1); mascota de guardia 20 % (sobre el 12 %).
4. Los **buffs en aislado son pequenos** (entre 1,4 y 7,5 %) para que el equipo con muchos buffs no gane >72 %; Reflection e IgnoreDefense se tradujeron a dano verdadero porque el simulador solo ve `dps`, vida, mitigacion y regeneracion en C1 (no es el comportamiento exacto del juego). Berserker usa la version "decidida" (vida y dano suben), no la actual del servidor.
5. La formula depende de **la tabla de kits** (`kits.rotation`) y de dos supuestos del simulador (1,25 basicos/s, 0,5 s por skill); el medio de normalizar la clase (`kit_strength` > 1) amplifica sus diferencias, lo que indica que el kit real de cada clase no esta bien normalizado todavia.
6. Muchas perillas (113): el ajuste automatico usa una semilla fija y se puede sobreajustar; con otra semilla (n=3000, rapido) el modelo de la ronda 3 daba ~82,7 frente a 84,3 de la semilla de ajuste. No se verifico con el servidor real.
7. El valor "VIT" pesa poco en la vida (`w_vit_hp` 0,41; el tanque saca la vida sobre todo de piezas y arbol): la identidad por atributo principal sale por reparto de puntos, no por un atributo concreto. Los pesos por clase (`id_*`) hacen parte del trabajo de equilibrar clases a mano, no solo de la forma matematica.
8. Posible falla del simulador: `run.py --params archivo.json` no abre la clave `params` de los archivos que escribe `tune.py` (solo funciona con diccionarios planos). No se modifico; se uso un archivo plano.

## Ideas para una formula hibrida
- **Un solo cubo de vida con suma de fuentes antes de saturar** (VIT + arbol + piezas + mascota + SwellLife + IncreaseHealth + Berserker): resuelve el pedido de vida apilable sin tope y hace que todos sirvan.
- Cubos `exp(A x S(x))` por fuente: acotados, sin topes duros, y permiten medir el aporte de cada uno en el reparto del golpe (C8 = 1,0 con 10 factores).
- Matriz de afinidad pieza x canal (poder, critico, vida, mitigacion, durabilidad, regeneracion, enfriamiento, escudo) combinada con el rol: toda pieza aporta algo (30 % fuera de rol) y el rol decide cuanto; asi las 11 ranuras se notan.
- Normalizar el kit por la media de clases con una perilla (`kit_strength`) y dejar el resto de la identidad en pesos por clase.
- Traducir el dano verdadero y los criticos como "parte del dano que ignora la mitigacion" (se desplaza, no se suma) y que la magia ignore parte de la mitigacion: el tanque nota al rival magico/critico sin necesitar penetracion que obligue al simulador a recortar.
- Mantener el crecimiento por nivel pequeno y dejar que las inversiones (stats, items, arbol) aporten el resto; con ello C10 (diferencia de nivel y oro) sale bien (0,94).
- Para una hibrida conviene sumar un termino que ate vida y dano por nivel (cociente constante) y asi arreglar C6 y las peleas del nivel 30, que esta formula no resuelve.
