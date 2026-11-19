# F10 — LEY DE CONSERVACIÓN DEL PODER DE DUELO (resumen, 2026-11-19)

Todo lo de aquí sale de corridas reales del simulador v2.2 (`run.py --n 100000`, `stress.py`, `cells.py`). **Nada de esto toca el juego todavía.**

## 1. Qué quieres y cómo lo traduje
«Todos pueden matar a su rival pero también morir en el intento» = (a) ningún cruce imposible (30-70 %), (b) sin paseos (el ganador no termina intacto), (c) el ganador termina con la vida bastante gastada, (d) en todos los niveles 1-30, (e) todos los elementos útiles, (f) sin topes duros. Eso es el nuevo criterio **C12** del simulador v2.2 (15 puntos), más C2 (cruces 40-60 %) medido con y sin buffs.

## 2. La idea (un cambio de enfoque, no una fórmula más)
En un duelo A mata a B antes que B a A  ⇔  `DPS_A × EHP_A > DPS_B × EHP_B`. Llamo **poder de duelo** a `P = DPS × EHP`. Si todos los personajes con la misma inversión (stats, árbol, oro, mascotas, buffs) tienen el mismo P, el duelo sale 50-50 **da igual cómo repartan entre daño y defensa**. Tanque y cristal no son «uno mejor que otro»: tienen el mismo P, uno con más EHP y menos DPS, el otro al revés (**la identidad es una inclinación, no una ventaja**). El balance deja de ser «ajustar 41 elementos para que todo salga parejo» y pasa a ser **una ley de conservación + correcciones pequeñas y medibles**.

```
log DPS = log DPS_nivel(L) + kO·O + ½·inclinación_clase + ½·ψ
log EHP = log EHP_nivel(L) + kD·D − ½·inclinación_clase + ½·ψ        → log P = … + kO·O + kD·D + ψ   (kO = kD ⇒ solo importa CUÁNTO se invierte)
O = fondos ofensivos (dano, crítico, enfriamiento, maná)   D = fondos defensivos (vida, mitigación/durabilidad, sostén)
```
- Cada elemento (5 stats, 6 nodos de árbol, 11 ranuras de ítem, 4 mascotas, 15 buffs) aporta `peso_propio × valor_saturado` a uno o varios fondos (perilla propia por elemento). Ítems: peso × precio relativo de la ranura × tier/3 (paridad de coste por diseño) con una parte genérica (toda pieza sirve) y otra por rol.
- **Sin topes duros:** saturación por elemento `x(1+k)/(x+k)`, mitigación `1−(1−m0)·e^(−x)`, buffs apilados con `Kb(1−e^(−suma/Kb))` (todos suman a la vez, el siguiente rinde menos). **Todos los aplicadores de vida (stats, nodo, piezas, mascota, SwellLife, IncreaseHealth, Berserker…) suman a la vez** en el mismo fondo VIDA.
- **Vida efectiva:** `hp = hp_nivel·e^(kD·VIDA…)`, mitigación física `1−(1−m0)e^(−x_m)`, mágica `1−(1−m0)e^(−μ·x_m)` con μ=0,72: **el rival mágico, el crítico (penetra) y IgnoreDefense se notan contra el tanque** (acotado: ≈ +13 % de poder contra un tanque muy blindado).
- **Identidad por atributo principal:** la inclinación base de cada clase (de los tilts del juego) + cuánto invierte el jugador en ofensa o defensa. Tanque: EHP ×2,2 y DPS ×0,45 del cristal (C3 = 1,0), y aun así tanque vs daño = 52 %: cuesta matarlo pero se puede.
- **Correcciones calibradas (en lazo cerrado, 2 minutos, `f10_calibra.py`):** `ψ_clase(L)` (6 números por clase, amplitud ±0,25) para que cada clase gane ≈ 50 % contra el campo en cada nivel, y `e1(L)`, `e2(L)` (compensan que repartir la inversión da un pequeño bono). Si cambias una perilla, **se vuelve a correr el calibrador y listo**.

## 3. Resultados (simulador v2.2, n=100.000, 17 niveles, 441 cruces por nivel)
| Fórmula | Puntaje | C12 matar y morir | C2 cruces 40-60 % | Cruces en banda (media de niveles) | Duración de duelos nv1 → nv30 |
|---|---|---|---|---|---|
| **juego real (`f_real`)** | **48,9** | 0,45 | 0,00 | 11 % | 23 s → 12,5 s |
| `f04` presupuesto (piloto) | 84,2 | 0,89 | 0,43 | 46 % | 4 s → 15 s |
| `f01` cubos (piloto) | 86,9 | 0,93 | 0,71 | 66 % | 28 s → **4 s** (no jugable) |
| **`f10` conservación** | **90,8** | **0,97** | **0,72** | **73 %** | 8,7 s → 13,4 s |

Detalle de f10: cruces en banda por nivel 62-95 % (nv30: 61 %); cruces extremos (<25 % o >75 %) 0-7 % (nv30: 7 %); paseos 0 %; al ganador le queda 8-23 % de vida; cruces con ≥30 % y ≤70 %: 87-99 %; nivel con 2-3 menos gana 21 % (95 % con chances de remontar); 20 % menos oro gana 26 %; 3v3: composiciones 73 %, valor de los buffs 87 %; todos los elementos entre 1,2 % y 6,5 % (ninguno muerto, ninguno manda); 0 recortes del simulador.

## 4. Reparto del golpe (daño) en % — de más a menos relevante (nivel 30, cristal / tanque)
1. Piezas de ofensa 43 % / 40 %  2. Crítico del equipo (árbol, piezas, mascotas, buffs) 14 % / 7 %  3. Stats ofensivos 11 % / 13 %  4. Arma 8,5 % / 9 %  5. Base (nivel + clase) 5,7 % / 12,7 %  6. Árbol ofensivo 5,6 % / 2,8 %  7. Crítico por Agilidad 5,3 % / 6,6 %  8. Rotación (enfriamiento + maná) 3,2 % / 5,2 %  9. Mascotas 2,3 % / 1,8 %  10. Buffs 1,1 % / 1,9 %.  
En nivel 5 (cristal) la base pesa 32 % y las piezas 24,5 %: la importancia de cada fuente cambia con el nivel por sí sola. Datos completos en `f10_reparto.json` (ataque y defensa, niveles 5/15/30, tres arquetipos).
Vida efectiva (nivel 30, cristal): mitigación + menos daño por piezas 24,5 %, vida por piezas 21,8 %, base 18 %, árbol (mitigación) 5,7 %, stats (vida) 5,5 %…

## 5. Debilidades honestas
- **Nada está verificado contra el servidor real.** Falta traducir a C# y repetir `/mobaduel`. El simulador no modela peleas largas con control, maná/enfriamientos explícitos ni pasivos (fase 2).
- **Las curvas ψ y e1/e2 son calibración, no ley:** son pequeñas (ψ ±0,25) y automáticas, pero son 6 números por clase + 12. Si el juego cambia mucho hay que recalibrar.
- Niveles 28-30 siguen siendo los menos parejos (61 % en banda, 7 % extremos): quedan diferencias clase × arquetipo (p. ej. GM de daño fuerte y FE de daño débil) que una pendiente por clase no resolvió (se probó y no convergió; `--sl` apagado).
- **Diferencias individuales:** aun con clases y arquetipos parejos, un personaje con 15 % menos inversión pierde casi siempre (≈ 15 % de los personajes gana > 80 % y ≈ 15 % pierde < 20 % contra rivales al azar). Es propio de la población simulada (oro ±15 %, puntos gastados 85-100 %, ruido 8 %) y es igual en el juego real (`stress.py`): es la recompensa de invertir, no un error de la fórmula.
- El poder crece solo ≈ 2,6 % por nivel al final (los ítems T3 saturan): conviene seguir dando algo en 25-30.
- Los pesos de ítems salen del precio de la ranura (por diseño); stats y mascotas se ajustaron a mano para que cada elemento mueva 1,5-6 %.
- Escala absoluta de HP/daño distinta de la del juego (HP base 1.900 → ×1,3 por nivel, el resto crece con lo que equipas); **al traducir hay que reescalar** creeps, estructuras y tope por golpe.

## 6. Archivos
`f10_conservacion.py` (la fórmula: `PARAMS`, `attack`, `defense`, `decompose`, `omega`), `f10_calibra.py` (calibración), `resultados/f10_params.json` (valores finales), `f10_informe.json` (informe completo), `f10_reparto.json`, `v22_informe_*.json` (las otras fórmulas con las reglas nuevas), `stress.py`, `cells.py`.
Reproducir: `python -I -X utf8 run.py f10_conservacion --n 100000 --params resultados/f10_params.json`; recalibrar tras cambiar perillas: `python -I -X utf8 f10_calibra.py --start resultados/f10_params.json --out resultados/f10_params.json`.
