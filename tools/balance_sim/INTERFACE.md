# Simulador de formulas de balance (analisis; NO toca el juego)

Carpeta: `tools/balance_sim/`. Python 3.14 + numpy. Todo vectorizado: 150.000 trayectorias x 17 niveles + 6 millones de duelos tardan ~45 s.

## Contrato de una formula (`fNN_nombre.py`)
```python
import numpy as np, sim
FORMULA_TEXT = "texto de la formula general en una o varias lineas"
PARAMS = {"nombre": (inicial, minimo, maximo), ...}      # TODO valor numerico de la formula va aqui (se puede subir/bajar)
def attack(b, p)  -> dict(dps_phys, dps_mag, pen_phys, pen_mag, lifesteal)   # arrays (N,)
def defense(b, p) -> dict(hp, shield, mit_phys, mit_mag, dmg_taken_mult, regen)  # arrays (N,)
def decompose(b, p) -> {"mode": "mult"|"add", "factors": {nombre: array (N,)}}   # factores de UN golpe (para el % sobre el total)
```
- `b` es `sim.Builds` (N personajes del MISMO nivel `b.L`): `b.cls` (0 BK,1 RF,2 DL,3 MG,4 FE,5 GM,6 SUM), `b.stats[:,0..4]` = STR, AGI, ENE, VIT, CMD
  (puntos invertidos, tope 30.000 por stat, presupuesto `b.stat_budget`), `b.tree[:,0..5]` = ofensa, defensa, critico, durabilidad, vida, mana (puntos; 5 por nivel,
  `b.tree_budget`), `b.tier[:,0..10]` (0 vacio,1..3) y `b.role[:,0..10]` (0 ATAQUE, 1 DEFENSA, 2 RECURSOS, 3 UTILIDAD) de las ranuras
  arma, escudo, casco, armadura, pantalon, guantes, botas, alas, colgante, anillo1, anillo2; `b.pet[:,0..3]` (poder 0..1: ataque, guardia, vital, veloz);
  `b.buff[:,0..14]` rango 0..5 de SoulBarrier, Expansion, Defense, SwellLife, Heal, GreaterDefense, GreaterDamage, InfinityArrow, IncCrit, Reflection, Berserker,
  IgnoreDefense, IncreaseHealth, IncreaseBlock, Recovery; `b.rank` rango medio de skills (1..5). Ayudas: `b.primary`, `b.secondary`, `b.hp_tilt`, `b.dmg_tilt`, `b.mag`,
  `b.tier_frac(slots, roles)`, `b.buff_on(i)`. Referencias del juego actual: `sim.ref_health(L)`, `ref_mana`, `ref_damage_scale`, `ref_flat_defense` (puedes ignorarlas).
- Unidades libres: `dps_*` = dano por segundo SIN mitigar; `hp`, `shield` en puntos de vida; `mit_*` en [0,1) (fraccion que se evita); `dmg_taken_mult` factor (1 = nada);
  `regen` vida/s; `lifesteal` fraccion del dano hecho que se cura; `pen_*` fraccion de mitigacion ignorada.
- **Sin topes duros**: nada de `np.minimum(x, tope)` ni `np.clip` para arreglar la formula. Los limites deben salir de la forma matematica (x/(x+k), 1-exp(-x/k),
  productos de factores acotados, raices, log). El simulador cuenta cuantas veces tiene que recortar (`clips_harness_pct`) y lo penaliza. Los topes naturales de las
  entradas (30.000 por stat, 5 rangos, 3 tiers) SI valen.
- Debe funcionar de nivel 1 a 30 (a nivel 1 no hay items ni buffs ni mascotas; a nivel 30 hay T3 y todo).

## Salidas opcionales del contrato (el simulador las usa si existen; por defecto 0 o 1)
attack: `burst_mult` (multiplicador del dano los primeros 6 s: rafaga de skills), `sustain` (fraccion del dano que se mantiene despues: mana y enfriamientos),
`cc_uptime` (fraccion del tiempo que dejas sin actuar al rival), `antiheal` (cuanto reduces su curacion), `pen_phys`/`pen_mag`, `lifesteal`.
defense: `shield` (SD), `aegis` y `aegis_cd` (escudo periodico y cada cuantos s), `tenacity` (reduce el control que recibes), `reflect` (fraccion del dano que devuelves), `regen`.
Todo debe ser un array (N,) o un numero. Sin topes duros (ver abajo).

## Trayectorias (como se construye cada personaje, nivel por nivel)
- **Stats:** cada nivel da `puntos_clase/29` (BK, MG, FE, GM 100.000 en total; RF y SUM 112.000; DL 130.000), repartidos segun la intencion del personaje (dano / tanque / mixto) y
  una politica de orden (equilibrado, tanque primero, dano primero o critico primero en los niveles 2-12) con ruido por nivel; tope 30.000 por stat.
- **Arbol:** 5 puntos por nivel; mitad de los personajes los reparte "profundo" (llena nodos de 100 en orden de prioridad) y mitad "repartido" (proporcional).
- **Items:** el oro crece como `62.000 x ((nivel-1)/29)^1,5` (x factor de farmeo ~ +-15 %); cada compra es una mejora (ranura, tier) con coste `700 x peso_ranura x (precio_tier - 0,7 x precio_tier_anterior)`
  y se compran por orden de prioridad (los de dano empiezan por el arma, los tanque por armadura y pantalon); a nivel 20 ya hay T2 casi completo y T3 llega en 25-30.
- **Mascotas:** se desbloquean en los niveles 4, 8, 12 y 16 y suben de poder hasta el 30. **Buffs:** de nivel 6 en adelante cada pelea tiene un conjunto distinto activo (10-70 % de cada uno).
- **Rango de skills:** sube de 1 a 5 hacia el nivel 17.

## Salidas nuevas (v2.1)
attack: `pct_dps` (dano por segundo como fraccion de la vida MAXIMA del rival: veneno, sangrado, Death Stab), `true_dps` (dano verdadero absoluto por segundo), `aoe_share` (parte del dano que
salpica al 65 % a los demas rivales en 3v3). defense: `dot_resist` (reduce el dano en % de vida y el verdadero). El dano en % y el verdadero ignoran la mitigacion y la reduccion de dano.
**Buffs en equipo:** en el 3v3 cada miembro RECIBE los buffs que lanzan sus companeros segun su clase: BK Swell Life (equipo) y Defense (propio); RF Increase Health e Increase Block (equipo) e Ignore Defense (propio);
DL Increase Critical Damage (equipo) y Defense; MG Defense; FE Heal, Greater Defense, Greater Damage, Infinity Arrow y Recovery (equipo); GM Soul Barrier y Expansion (propios); SUM Recovery (equipo), Reflection y Berserker (propios).
El rango del buff es el rango medio de skills del lanzador. La formula los ve en `b.buff` como siempre (Heal y Recovery = curacion recibida: traducelos a `regen`).

## Duelo (por pasos de 0,5 s, hasta 90 s)
Cada paso: dano de A a B (= dps x (1 - mitigacion + penetracion) x dmg_taken_mult x alcance x control), primero contra el escudo y luego contra la vida; reflejo; curacion
(regen + vida al golpear, recortada por el anticurar del rival); Egida periodica. Variabilidad de habilidad: cada lado multiplica su dano por un factor lognormal (sigma 8 %; los bots del servidor real miden ~4 %).
Alcance: un cuerpo a cuerpo contra un rival de mas alcance pierde 5 % de tiempo de pelea por casilla de diferencia (hasta 20 %).
Gana quien mata primero; si nadie muere en 90 s gana quien conserva mas vida relativa (y cuenta como "tabla" en C5).

## Puntuacion (0-100), la calcula `sim.evaluate`
- **C2 (25) duelos parejos en CADA nivel:** se simulan todas las parejas de las 21 celdas (7 clases x {dano, tanque, mixto}) en 17 niveles (1 a 30); cada pareja debe quedar entre 40 y 60 %
  (5-5, 6-4, 4-6). Puntua el porcentaje de parejas en banda menos la mitad del porcentaje de parejas extremas (< 25 % o > 75 %).
- **C1 (20) cobertura:** cada uno de los 41 elementos (5 stats, 6 nodos del arbol, 11 ranuras de items, 4 mascotas, 15 buffs), al subir UNA unidad estandar en un personaje de referencia
  (niveles 8, 16, 24, 30), mueve el dano o la vida efectiva entre 1,5 % y 12 % ("todo sirve, se nota y ninguno manda"). Se mide por clase y vale la clase a la que mas le sirve.
- **C3 (10) identidad:** vida efectiva del tanque 1,8-3,5 x la del de dano; dano del de dano 1,8-3,5 x el del tanque; tanque contra dano 40-60 %.
- **C4 (5)** ninguna celda gana mas del 58 % ni menos del 42 % contra el campo. **C5 (5)** duelos de 5-25 s (3-30 s antes del nivel 5) y casi sin tablas. **C6 (5)** el poder crece 3-15 % por nivel sin saltos.
- **C7 (5) sin topes:** extremos (p99,9) a menos de 3 x la mediana, mitigacion < 85 %, sin recortes del simulador, sin NaN. **C8 (5)** transparencia: hay >= 10 factores en el golpe y cada uno pesa 1-45 %.
- **C9 (5)** paridad: una ranura mas cara rinde proporcionalmente mas.
- **C10 (5) diferencia de nivel y de oro:** quien tiene 2-3 niveles menos debe ganar entre 22 y 42 % (casi siempre con alguna chance de remontar: 5-50 % en el 85 % de los cruces); quien tiene 20 % menos oro, entre 30 y 45 %.
- **C11 (10) equipos 3v3 con buffs compartidos:** 5 composiciones (2 tanques + 1 dano, 3 dano, equilibrada, con apoyo FE/SUM, aleatoria) en los niveles 8, 16, 24 y 30; cada cruce 40-60 %,
  y el equipo CON los buffs que lanzan sus clases debe ganar 55-72 % al mismo tipo de equipo SIN buffs (que los buffs sirvan, se noten y no decidan).

## Kits reales por clase (`kits.py`, `kits.json`)
`kits.json` (generado por `make_kits.py` desde SKILLS_REGISTRO.md) tiene las 6 skills por defecto de cada clase: dano r1/r5, escala por stat, enfriamiento r1/r5, mana, alcance y si es de area.
`kits.rotation(b, frac, cdr=0, mana_regen_bonus=0)` calcula para cada personaje la rotacion continua: `skill_dps` (dano de las skills con su escala por stat `x (1 + 2,5 x frac x escala)`), `basic_dps` (basicos en los huecos
mientras no lanza), `steady` (suma), `burst_mult` (los primeros 6 s lanzando todo), `sustain` (limite por mana con la regeneracion real del servidor), `aoe_share`. `frac` es la fraccion de mezcla de la formula y `cdr` la
reduccion de enfriamiento que decida la formula. Supuestos MIOS: 1,25 basicos por segundo y 0,5 s de animacion por skill. Una formula nueva PUEDE usarlo (recomendado) o ignorarlo.
Uso tipico: `dps = rot['steady'] x escala_nivel x tilt x (1+poder_piezas) x crit`, y devolver `burst_mult`, `sustain` y `aoe_share` en `attack`.

## Calibracion y perillas
- `calibrar.py [--fit]`: compara `f_real.py` (port de las formulas reales del servidor con un factor de kit por clase) con las matrices de `INFORME_DUELOS.md` (nivel 30, sin items y con T3).
  Resultado actual (con los kits reales): correlacion 0,98 sin items y 0,89 con T3; acierta el ganador en el 100 % y el 95 % de los cruces; duraciones medias 4-10 s contra 5-13 s reales (las de FE/GM/SUM salen mas cortas). Los factores de kit calibrados estan dentro de `f_real.py` (KIT_CAL).
- `robust.py fNN --out perillas.json`: sube y baja cada parametro un 15 % y reporta el cambio de puntaje y de dano / vida efectiva (clase mas afectada). Una formula buena tiene perillas que
  ajustan el dano o la vida pero no rompen el puntaje (peor caida < 5 puntos).

## Comandos (siempre `python -I -X utf8`, desde `tools/balance_sim`)
- `run.py fNN_nombre --n 150000 [--params '{"k":v}'|archivo.json] [--out informe.json] [--quick]` -> informe JSON con puntuacion y detalle.
- `tune.py fNN_nombre --iters 40 --pop 8 --n 12000 --out mejores.json [--start params.json]` -> ajusta PARAMS (estrategia evolutiva, misma semilla).
- Referencia del juego actual: `f00_actual.py` (aprox. de la mezcla 45/20/35 con topes) ~55 puntos; `f_real.py` (el juego real, calibrado) ~48.
Evalua final con `--n 150000` (2,5 millones de personajes-nivel + 6 millones de duelos) cuando los parametros esten ajustados.

## Semantica de los elementos (para que la formula los use con sentido)
- Pesos de rol de una pieza (`b.role`): ATAQUE = poder de skill y critico; DEFENSA = vida, durabilidad (menos dano recibido), mitigacion; RECURSOS = sostenimiento
  (regeneracion de vida y de mana, vida al golpear); UTILIDAD = reduccion de enfriamiento (mas dano por segundo), resistencia a control, oro. El arma tambien aporta dano base segun su tier.
  Escudo (ranura 1): defensa/resistencia o enfriamiento; alas: velocidad/vida/penetracion de escudo; colgante y anillos: segun rol.
- Mascotas: ataque = dano extra (cuervo/caballo del DL); guardia = menos dano recibido; vital = vida y regeneracion; veloz = velocidad de ataque/enfriamiento.
- Buffs (rango 1-5, de aliados o propios): SoulBarrier (escudo + menos dano), Expansion (+dano de skills), Defense (menos dano + escudo), SwellLife (+% vida), Heal (cura instantanea
  = regeneracion equivalente), GreaterDefense (menos dano), GreaterDamage (+dano), InfinityArrow (+dano de basicos), IncCrit (+prob. y multiplicador de critico), Reflection (devuelve dano),
  Berserker (+vida y +dano), IgnoreDefense (parte del dano ignora mitigacion = penetracion), IncreaseHealth (+% vida), IncreaseBlock (resistencia a control = vida efectiva), Recovery (cura).
  **TODOS los buffs deben servir a la vez y notarse; no vale que solo cuente el mejor.** Si apilarlos desborda, se corrige con su valor o con la forma de la formula, no con un tope.
- Sin simulacion de mana ni de enfriamientos: traducelos a dano por segundo sostenido (p. ej. enfriamiento -x % = dano por segundo +x/(1-x)).
