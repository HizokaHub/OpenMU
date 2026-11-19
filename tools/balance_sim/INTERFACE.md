# Simulador de formulas de balance (analisis; NO toca el juego)

Carpeta: `tools/balance_sim/`. Python 3.14 + numpy. Todo vectorizado: un nivel con 100.000 personajes tarda ~1 s.

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

## Puntuacion (0-100), la calcula `sim.evaluate`
C1 (25) cobertura: cada uno de los 41 elementos (5 stats, 6 nodos del arbol, 11 ranuras de items, 4 mascotas, 15 buffs) al subir UNA unidad estandar debe mover
el poder ofensivo o la vida efectiva entre 1,5 % y 12 % ("todo sirve y se nota", y ninguno manda). C2 (25) arquetipos: tanque/dano/mixto/utilidad entre 43-57 % de
victorias contra el campo, el tanque con 1,8-3,5 x la vida efectiva del de dano y el de dano 1,8-3,5 x el dano del tanque, tanque contra dano 40-62 %. C3 (10) sin
personajes invencibles. C4 (10) duelos de 5-25 s a partir de nivel 5. C5 (10) crecimiento suave entre niveles (3-15 % por nivel). C6 (10) sin topes: valores extremos
(p99,9) a menos de 3 x la mediana, mitigacion < 85 %, sin recortes. C7 (5) transparencia: cada factor del golpe pesa entre 1 y 45 % y hay al menos 10 factores.
C8 (5) paridad: una ranura mas cara debe rendir proporcionalmente mas.

## Comandos (siempre `python -I -X utf8`, desde `tools/balance_sim`)
- `run.py fNN_nombre --n 100000 [--params '{"k":v}'|archivo.json] [--out informe.json] [--quick]` -> informe JSON con puntuacion y detalle.
- `tune.py fNN_nombre --iters 120 --pop 10 --n 25000 --out mejores.json [--start params.json]` -> ajusta PARAMS (estrategia evolutiva, misma semilla).
- Referencia del juego actual: `f00_actual.py` (aprox. de la mezcla 45/20/35 con topes) ~66 puntos.
Evalua final con `--n 300000` (2,7 millones de personajes + millones de duelos) cuando los parametros esten ajustados.

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
