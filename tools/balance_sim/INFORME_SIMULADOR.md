# INFORME DEL SIMULADOR DE BALANCE (reporte 1, 2026-11-18)

Herramienta de análisis. **No modifica el juego ni el servidor.** Código en `tools/balance_sim/` (`sim.py`, `run.py`, `tune.py`, `f00_actual.py`, `INTERFACE.md`).

## Qué hace, paso a paso
1. **Fabrica personajes nivel por nivel** (7 clases). Cada nivel entrega los puntos de stats de su clase (100.000 / 112.000 / 130.000 en total), 5 puntos de árbol, oro para comprar ítems, mascotas que se desbloquean y rango de skills. Cada personaje tiene una *intención* (daño, tanque o mixto) y un orden de reparto distinto (equilibrado, tanque primero, daño primero, crítico primero; árbol «profundo» o «repartido»). Así el nivel 16 tiene personajes a medio camino, como en una partida real, y no personajes inventados ya terminados.
2. **Una fórmula (módulo `fNN_*.py`) convierte cada personaje en números de combate:** daño por segundo físico y mágico, vida, escudo, mitigación, reducción de daño, regeneración, vida al golpear, penetración, control, anticurar, reflejo, Égida, ráfaga inicial y sostenido.
3. **Duelo por pasos de 0,5 s (hasta 90 s)** con todo lo anterior, alcance (el cuerpo a cuerpo pierde tiempo contra el de largo alcance) y variabilidad de habilidad (σ 12 %).
4. **Matriz de cruces en 17 niveles (1, 2, 3, 4, 5, 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 30):** todas las parejas de 21 celdas (7 clases × {daño, tanque, mixto}). Meta: cada cruce entre 40 y 60 % (5-5, 6-4, 4-6) en **cada** nivel.
5. **Mide los 41 elementos uno por uno** (5 stats, 6 nodos del árbol, 11 ranuras de ítems, 4 mascotas, 15 buffs): sube una unidad estándar en un personaje de referencia y mira cuánto cambia el daño o la vida efectiva. Cada uno debe mover entre 1,5 % y 12 %: que todo sirva, se note y ninguno mande.
6. **Puntúa de 0 a 100** (30 duelos parejos por nivel, 20 cobertura de elementos, 15 identidad tanque/daño, 10 sin topes, 5 + 5 + 5 + 5 + 5 el resto; detalle en `INTERFACE.md`).

## Escala (medida)
150.000 trayectorias × 17 niveles = 2,5 millones de personajes-nivel y ≈ 3 millones de duelos por evaluación completa: **42 segundos**. El ajuste de parámetros usa una versión reducida (≈ 1,5 s por evaluación).

## Primera lectura: el juego actual dentro del simulador
`f00_actual.py` es **mi aproximación** de la mezcla 45/20/35 con topes (no el código real): **57/100**. Solo el 31 % de los cruces cae en 40-60 %; Magic Gladiator es la clase más débil (sale entre 28 y 35 % contra casi todas) y el Elfo la más fuerte; el tanque contra el de daño gana solo 28 %; el tanque tiene 1,3 veces la vida efectiva del de daño (meta 1,8-3,5). 14 de los 41 elementos casi no mueven nada (árbol de vida y maná, mascotas vital y veloz, 9 buffs). Es coherente con lo que ya medimos en el servidor real (MG al fondo, Elfo arriba), lo que da confianza en que el simulador reproduce el patrón.

## Lo que el simulador NO hace (para que no haya sorpresas)
- Solo duelos 1 contra 1. No modela peleas de equipo, minions, torres, jungla, visión ni posición.
- Las skills no se simulan una por una: una fórmula decide cuánto daño por segundo, ráfaga y sostenido tiene cada personaje. Las diferencias de clase que conoce son: stat principal, tilt de vida y daño, parte de daño mágico, alcance. Los pasivos (Frenesí, Combustión…) los modela la fórmula con los números del contrato.
- Los efectos de ítems por rol, mascotas y buffs son supuestos míos (descritos en `INTERFACE.md`); cuando elijamos fórmulas hay que contrastarlos con el código real.
- La meta 40-60 % es muy exigente (21 × 21 cruces en 17 niveles): puede que ninguna fórmula la cumpla al 100 %. Se reportará cuánto se acerca cada una.
- Lo que salga se valida **antes de tocar el juego**: se traduce a C#, se despliega aparte y se repiten los duelos reales de `/mobaduel` para comprobar que la matriz real se parece a la simulada.

## Plan por etapas (una por ventana de 5 h)
1. **Etapa 1:** 9 agentes diseñadores, uno por familia de fórmula (multiplicativa por cubos, producto de potencias, rating→%, presupuesto con pesos, plana+ratio, paridad por coste, saturación, intercambio ofensa↔defensa, capas por categoría). Cada uno escribe su módulo, lo ajusta con `tune.py` y entrega puntuación, fórmula en texto y reparto en % del golpe.
2. **Etapa 2:** la fórmula 10 (híbrida con lo mejor de las 9) y 2 críticos por fórmula (uno busca personajes que la rompan, otro audita niveles 1-30 y cobertura).
3. **Etapa 3:** síntesis, comparación y las 10 fórmulas finales con significado de cada término y su % sobre el golpe. Tras cada etapa recibes un reporte.

---
# REPORTE 2 (2026-11-18): las 5 mejoras pedidas

1. **Fórmula real portada y calibrada** (`f_real.py`, `calibrar.py`). Reproduce tus matrices de duelos reales: correlación **0,97** sin ítems y **0,85** con T3, acierta el ganador en el 100 % y el 88 % de los cruces. Descubrimiento: los duelos reales de los bots casi no tienen variación (σ ≈ 4 %), por eso salen 10-0 y 0-10. El simulador puntúa con σ = 8 % (más exigente que un humano, menos que un bot). Límite: la calibración es solo en nivel 30 (es lo único medido) y el factor de kit por clase resume las skills (SUM sale ×2,2, BK ×0,8, RF ×0,75, DL ×0,74…).
2. **Daño en % de la vida máxima, daño verdadero y reducción de ambos** (`pct_dps`, `true_dps`, `dot_resist`): veneno, sangrado, Death Stab y similares ya cuentan en el duelo.
3. **Prueba de perillas** (`robust.py`): sube y baja cada parámetro un 15 %. En `f_real`, la peor caída de puntaje es 1,1 puntos, es decir, sus perillas mueven el balance poco; sirve de referencia.
4. **Diferencia de nivel y de oro** (C10): quien tiene 2-3 niveles menos debe ganar 22-42 % con chances de remontar; quien tiene 20 % menos de oro, 30-45 %. Hoy el juego real: nivel 23 %, remontada posible en el 48 % de los cruces, oro 43 %.
5. **Equipos 3v3 con buffs compartidos y daño en área** (C11): cada clase lanza sus buffs reales a su equipo; se mide si 5 composiciones se llevan 40-60 % y si el equipo con buffs gana 55-72 % al equipo sin buffs. Hoy el juego real: composiciones 57 %, valor de los buffs 78 %.

**Medición del estado actual con los 11 criterios:** `f_real` (juego real) **47,9/100**; `f00_actual` (mi aproximación anterior) 55,5. Evaluación completa (150.000 trayectorias × 17 niveles, 6 millones de duelos individuales + peleas de equipo): 56-103 s.
Solo el 31 % de los cruces clase × arquetipo cae en 40-60 % en el juego real (10 % en el nivel 1, 26 % en el 10, 31 % en el 30).
