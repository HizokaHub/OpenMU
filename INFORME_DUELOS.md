# INFORME DE DUELOS 1v1

Generado por `tools/informe_duelos.py` a partir de `/mobaduel` (servidor, sin cliente, IA real de `MobaBotPlayer`, nivel 30, skills a rango 5).
Meta del balance: **4-6 victorias de 10** en cada cruce.

## Sin ítems (equipo inicial del clon)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_sin_items_20261115_164739.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 2.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 5-5 | 0-9 (9) ⚠ | 0-10 ⚠ | 10-0 ⚠ | 9-0 (9) ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **BK** | 9-0 (9) ⚠ | 3-5-2e | 0-10 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **FE** | 10-0 ⚠ | 10-0 ⚠ | 8-2 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 1-9 ⚠ | 10-0 ⚠ |
| **MG** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 2-6-2e ⚠ | 10-0 ⚠ | 0-10 ⚠ | 7-2-1e ⚠ |
| **DL** | 0-9 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 4-1-5e ⚠ | 0-10 ⚠ | 7-2-1e ⚠ |
| **SUM** | 10-0 ⚠ | 10-0 ⚠ | 9-1 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 4-2-4e | 10-0 ⚠ |
| **RF** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 2-7-1e ⚠ | 2-7-1e ⚠ | 0-10 ⚠ | 6-2-2e ⚠ |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 58 | 50 % | 13 | 48 % |
| Blade Knight (BK) | 59 | 66 % | 10 | 34 % |
| High Elf (FE) | 60 | 85 % | 13 | 62 % |
| Magic Gladiator (MG) | 60 | 29 % | 10 | 13 % |
| Dark Lord (DL) | 59 | 13 % | 10 | 6 % |
| Summoner (SUM) | 60 | 98 % | 9 | 42 % |
| Rage Fighter (RF) | 60 | 8 % | 9 | 7 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 5-5, BK 3-5-2e, FE 8-2, MG 2-6-2e, DL 4-1-5e, SUM 4-2-4e, RF 6-2-2e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 plain`)

- SUM 10 - 0 RF de 10  → `/mobaduel sum-rf 30 plain`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 plain`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 plain`
- MG 10 - 0 DL de 10  → `/mobaduel mg-dl 30 plain`
- FE 10 - 0 RF de 10  → `/mobaduel fe-rf 30 plain`
- FE 10 - 0 DL de 10  → `/mobaduel fe-dl 30 plain`
- FE 10 - 0 MG de 10  → `/mobaduel fe-mg 30 plain`
- BK 10 - 0 RF de 10  → `/mobaduel bk-rf 30 plain`
- BK 0 - 10 SUM de 10  → `/mobaduel bk-sum 30 plain`
- BK 10 - 0 DL de 10  → `/mobaduel bk-dl 30 plain`
- BK 10 - 0 MG de 10  → `/mobaduel bk-mg 30 plain`
- BK 0 - 10 FE de 10  → `/mobaduel bk-fe 30 plain`
- GM 10 - 0 RF de 10  → `/mobaduel gm-rf 30 plain`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 plain`
- GM 9 - 0 DL de 9  → `/mobaduel gm-dl 30 plain`
- GM 10 - 0 MG de 10  → `/mobaduel gm-mg 30 plain`
- GM 0 - 10 FE de 10  → `/mobaduel gm-fe 30 plain`
- GM 0 - 9 BK de 9  → `/mobaduel gm-bk 30 plain`
- FE 1 - 9 SUM de 10  → `/mobaduel fe-sum 30 plain`
- DL 7 - 2 RF (1 empates) de 10  → `/mobaduel dl-rf 30 plain`
- MG 7 - 2 RF (1 empates) de 10  → `/mobaduel mg-rf 30 plain`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 37 %, básico 34 %, #2 23 %, #3 5 %, #5 0 % · daño medio por duelo 91.349
- **BK**: básico 28 %, #43 22 %, #232 18 %, #42 17 %, #21 8 %, #22 4 % · daño medio por duelo 98.373
- **FE**: #51 43 %, básico 30 %, #235 21 %, #52 6 %, #24 0 % · daño medio por duelo 128.680
- **MG**: básico 39 %, #2 20 %, #1 13 %, #41 12 %, #236 10 %, #237 6 % · daño medio por duelo 69.817
- **DL**: #65 65 %, básico 18 %, #61 10 %, #66 7 % · daño medio por duelo 73.965
- **SUM**: básico 40 %, #215 30 %, #214 23 %, #230 6 % · daño medio por duelo 72.258
- **RF**: #263 52 %, #262 33 %, básico 11 %, #261 4 % · daño medio por duelo 84.868

## Con set completo T3 (set, arma, escudo y accesorios)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_t3_20261115_170216.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 3.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 4-5-1e | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 0-9 (9) ⚠ | 0-10 ⚠ | 7-2 (9) ⚠ |
| **BK** | 10-0 ⚠ | 5-4-1e | 1-8 (9) ⚠ | 10-0 ⚠ | 10-0 ⚠ | 9-1 ⚠ | 10-0 ⚠ |
| **FE** | 10-0 ⚠ | 8-1 (9) ⚠ | 5-5 | 10-0 ⚠ | 10-0 ⚠ | 4-5-1e | 10-0 ⚠ |
| **MG** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 3-7 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **DL** | 9-0 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 6-3-1e ⚠ | 9-0-1e ⚠ | 10-0 ⚠ |
| **SUM** | 10-0 ⚠ | 1-9 ⚠ | 5-4-1e | 10-0 ⚠ | 0-9-1e ⚠ | 1-3-6e | 10-0 ⚠ |
| **RF** | 2-7 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 4-4-2e |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 58 | 29 % | 12 | 22 % |
| Blade Knight (BK) | 59 | 85 % | 9 | 28 % |
| High Elf (FE) | 59 | 89 % | 10 | 38 % |
| Magic Gladiator (MG) | 60 | 0 % | 8 | 4 % |
| Dark Lord (DL) | 59 | 65 % | 10 | 43 % |
| Summoner (SUM) | 60 | 62 % | 6 | 38 % |
| Rage Fighter (RF) | 59 | 20 % | 6 | 12 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 4-5-1e, BK 5-4-1e, FE 5-5, MG 3-7, DL 6-3-1e, SUM 1-3-6e, RF 4-4-2e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 t3`)

- SUM 10 - 0 RF de 10  → `/mobaduel sum-rf 30 t3`
- DL 10 - 0 RF de 10  → `/mobaduel dl-rf 30 t3`
- MG 0 - 10 RF de 10  → `/mobaduel mg-rf 30 t3`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 t3`
- MG 0 - 10 DL de 10  → `/mobaduel mg-dl 30 t3`
- FE 10 - 0 RF de 10  → `/mobaduel fe-rf 30 t3`
- FE 10 - 0 DL de 10  → `/mobaduel fe-dl 30 t3`
- FE 10 - 0 MG de 10  → `/mobaduel fe-mg 30 t3`
- BK 10 - 0 RF de 10  → `/mobaduel bk-rf 30 t3`
- BK 10 - 0 DL de 10  → `/mobaduel bk-dl 30 t3`
- BK 10 - 0 MG de 10  → `/mobaduel bk-mg 30 t3`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 t3`
- GM 0 - 9 DL de 9  → `/mobaduel gm-dl 30 t3`
- GM 10 - 0 MG de 10  → `/mobaduel gm-mg 30 t3`
- GM 0 - 10 FE de 10  → `/mobaduel gm-fe 30 t3`
- GM 0 - 10 BK de 10  → `/mobaduel gm-bk 30 t3`
- DL 9 - 0 SUM (1 empates) de 10  → `/mobaduel dl-sum 30 t3`
- BK 9 - 1 SUM de 10  → `/mobaduel bk-sum 30 t3`
- BK 1 - 8 FE de 9  → `/mobaduel bk-fe 30 t3`
- GM 7 - 2 RF de 9  → `/mobaduel gm-rf 30 t3`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 46 %, #2 28 %, básico 27 % · daño medio por duelo 69.620
- **BK**: #232 27 %, básico 25 %, #42 20 %, #43 14 %, #21 9 %, #19 2 % · daño medio por duelo 83.879
- **FE**: #51 52 %, básico 26 %, #235 23 %, #52 0 % · daño medio por duelo 91.948
- **MG**: básico 41 %, #2 25 %, #1 17 %, #41 10 %, #237 4 %, #236 3 % · daño medio por duelo 43.342
- **DL**: #65 83 %, básico 10 %, #66 7 %, #61 0 %, #76 0 % · daño medio por duelo 77.963
- **SUM**: básico 36 %, #215 33 %, #214 30 %, #230 0 % · daño medio por duelo 44.067
- **RF**: #263 61 %, #262 26 %, básico 13 %, #261 0 % · daño medio por duelo 68.986

