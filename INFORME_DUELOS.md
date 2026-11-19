# INFORME DE DUELOS 1v1

Generado por `tools/informe_duelos.py` a partir de `/mobaduel` (servidor, sin cliente, IA real de `MobaBotPlayer`, nivel 30, skills a rango 5).
Meta del balance: **4-6 victorias de 10** en cada cruce.

## Sin ítems (equipo inicial del clon)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_sin_items_20261113_163238.json, duelos_sin_items_20261113_223559.json, duelos_sin_items_20261113_225219.json, duelos_sin_items_20261113_231856.json, duelos_sin_items_20261113_234940.json, duelos_sin_items_20261115_164739.json, duelos_sin_items_20261118_210915.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 5.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 3-4-3e | 9-0 (9) ⚠ | 0-10 ⚠ | 10-0 ⚠ | 9-0 (9) ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **BK** | 9-1 ⚠ | 3-1-6e | 0-10 ⚠ | 9-0-1e ⚠ | 10-0 ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **FE** | 10-0 ⚠ | 10-0 ⚠ | 5-3-2e | 10-0 ⚠ | 9-0 (9) ⚠ | 1-9 ⚠ | 10-0 ⚠ |
| **MG** | 0-10 ⚠ | 0-9-1e ⚠ | 0-10 ⚠ | 1-5-4e ⚠ | 10-0 ⚠ | 0-10 ⚠ | 1-8-1e ⚠ |
| **DL** | 0-9 (9) ⚠ | 0-10 ⚠ | 0-9 (9) ⚠ | 0-10 ⚠ | 5-3-2e | 0-10 ⚠ | 3-7 ⚠ |
| **SUM** | 10-0 ⚠ | 10-0 ⚠ | 9-1 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 3-3-4e | 10-0 ⚠ |
| **RF** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 8-1-1e ⚠ | 7-3 ⚠ | 0-10 ⚠ | 3-5-2e |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 68 | 57 % | 11 | 37 % |
| Blade Knight (BK) | 69 | 56 % | 10 | 18 % |
| High Elf (FE) | 59 | 85 % | 13 | 48 % |
| Magic Gladiator (MG) | 60 | 20 % | 10 | 10 % |
| Dark Lord (DL) | 58 | 5 % | 10 | 6 % |
| Summoner (SUM) | 60 | 98 % | 8 | 46 % |
| Rage Fighter (RF) | 60 | 26 % | 9 | 7 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 3-4-3e, BK 3-1-6e, FE 5-3-2e, MG 1-5-4e, DL 5-3-2e, SUM 3-3-4e, RF 3-5-2e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 plain`)

- SUM 10 - 0 RF de 10  → `/mobaduel sum-rf 30 plain`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 plain`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 plain`
- MG 10 - 0 DL de 10  → `/mobaduel mg-dl 30 plain`
- FE 10 - 0 RF de 10  → `/mobaduel fe-rf 30 plain`
- FE 9 - 0 DL de 9  → `/mobaduel fe-dl 30 plain`
- FE 10 - 0 MG de 10  → `/mobaduel fe-mg 30 plain`
- BK 10 - 0 RF de 10  → `/mobaduel bk-rf 30 plain`
- BK 0 - 10 SUM de 10  → `/mobaduel bk-sum 30 plain`
- BK 10 - 0 DL de 10  → `/mobaduel bk-dl 30 plain`
- BK 0 - 10 FE de 10  → `/mobaduel bk-fe 30 plain`
- GM 10 - 0 RF de 10  → `/mobaduel gm-rf 30 plain`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 plain`
- GM 9 - 0 DL de 9  → `/mobaduel gm-dl 30 plain`
- GM 10 - 0 MG de 10  → `/mobaduel gm-mg 30 plain`
- GM 0 - 10 FE de 10  → `/mobaduel gm-fe 30 plain`
- GM 9 - 0 BK de 9  → `/mobaduel gm-bk 30 plain`
- BK 9 - 0 MG (1 empates) de 10  → `/mobaduel bk-mg 30 plain`
- FE 1 - 9 SUM de 10  → `/mobaduel fe-sum 30 plain`
- BK 9 - 1 GM de 10  → `/mobaduel bk-gm 30 plain`
- MG 1 - 8 RF (1 empates) de 10  → `/mobaduel mg-rf 30 plain`
- DL 3 - 7 RF de 10  → `/mobaduel dl-rf 30 plain`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 43 %, básico 32 %, #2 20 %, #3 5 %, #5 0 % · daño medio por duelo 97.050
- **BK**: básico 34 %, #43 17 %, #42 15 %, #232 12 %, #21 10 %, #19 6 % · daño medio por duelo 94.160
- **FE**: #51 42 %, básico 33 %, #235 19 %, #52 6 %, #24 0 % · daño medio por duelo 131.833
- **MG**: básico 45 %, #1 21 %, #2 13 %, #41 11 %, #236 7 %, #237 3 % · daño medio por duelo 88.343
- **DL**: #65 55 %, básico 21 %, #66 15 %, #61 9 % · daño medio por duelo 89.736
- **SUM**: básico 37 %, #214 32 %, #215 27 %, #230 3 % · daño medio por duelo 73.280
- **RF**: #263 46 %, #262 35 %, básico 15 %, #261 5 % · daño medio por duelo 93.397

## Con set completo T3 (set, arma, escudo y accesorios)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_t3_20261113_224246.json, duelos_t3_20261113_232956.json, duelos_t3_20261114_003548.json, duelos_t3_20261115_170216.json, duelos_t3_20261118_212203.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 8.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 4-5-1e | 6-2-1e (9) ⚠ | 2-8 ⚠ | 9-0 (9) ⚠ | 9-0 (9) ⚠ | 0-10 ⚠ | 9-0 (9) ⚠ |
| **BK** | 4-0 (4) ⚠ | 3-5-2e | 1-8 (9) ⚠ | 10-0 ⚠ | 10-0 ⚠ | 9-1 ⚠ | 10-0 ⚠ |
| **FE** | 8-2 ⚠ | 8-1 (9) ⚠ | 4-4-2e | 10-0 ⚠ | 10-0 ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **MG** | 0-9 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 4-3-3e | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **DL** | 0-9 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 6-3-1e ⚠ | 0-10 ⚠ | 10-0 ⚠ |
| **SUM** | 10-0 ⚠ | 1-9 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 2-2-6e | 10-0 ⚠ |
| **RF** | 0-9 (9) ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 3-5-2e |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 60 | 59 % | 11 | 28 % |
| Blade Knight (BK) | 62 | 75 % | 8 | 28 % |
| High Elf (FE) | 59 | 78 % | 9 | 31 % |
| Magic Gladiator (MG) | 59 | 17 % | 8 | 24 % |
| Dark Lord (DL) | 59 | 34 % | 8 | 22 % |
| Summoner (SUM) | 60 | 85 % | 5 | 27 % |
| Rage Fighter (RF) | 59 | 0 % | 6 | 4 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 4-5-1e, BK 3-5-2e, FE 4-4-2e, MG 4-3-3e, DL 6-3-1e, SUM 2-2-6e, RF 3-5-2e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 t3`)

- SUM 10 - 0 RF de 10  → `/mobaduel sum-rf 30 t3`
- DL 10 - 0 RF de 10  → `/mobaduel dl-rf 30 t3`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 t3`
- MG 10 - 0 RF de 10  → `/mobaduel mg-rf 30 t3`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 t3`
- MG 0 - 10 DL de 10  → `/mobaduel mg-dl 30 t3`
- FE 10 - 0 RF de 10  → `/mobaduel fe-rf 30 t3`
- FE 0 - 10 SUM de 10  → `/mobaduel fe-sum 30 t3`
- FE 10 - 0 DL de 10  → `/mobaduel fe-dl 30 t3`
- FE 10 - 0 MG de 10  → `/mobaduel fe-mg 30 t3`
- BK 10 - 0 RF de 10  → `/mobaduel bk-rf 30 t3`
- BK 10 - 0 DL de 10  → `/mobaduel bk-dl 30 t3`
- BK 10 - 0 MG de 10  → `/mobaduel bk-mg 30 t3`
- BK 4 - 0 GM de 4  → `/mobaduel bk-gm 30 t3`
- GM 9 - 0 RF de 9  → `/mobaduel gm-rf 30 t3`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 t3`
- GM 9 - 0 DL de 9  → `/mobaduel gm-dl 30 t3`
- GM 9 - 0 MG de 9  → `/mobaduel gm-mg 30 t3`
- BK 9 - 1 SUM de 10  → `/mobaduel bk-sum 30 t3`
- BK 1 - 8 FE de 9  → `/mobaduel bk-fe 30 t3`
- GM 2 - 8 FE de 10  → `/mobaduel gm-fe 30 t3`
- GM 6 - 2 BK (1 empates) de 9  → `/mobaduel gm-bk 30 t3`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 46 %, básico 31 %, #2 23 % · daño medio por duelo 92.008
- **BK**: básico 28 %, #232 20 %, #42 18 %, #43 14 %, #21 11 %, #19 7 % · daño medio por duelo 82.354
- **FE**: #51 53 %, básico 27 %, #235 20 %, #52 0 % · daño medio por duelo 100.681
- **MG**: básico 41 %, #1 23 %, #2 19 %, #41 11 %, #237 3 %, #236 3 % · daño medio por duelo 72.176
- **DL**: #65 75 %, #66 13 %, básico 11 %, #61 0 % · daño medio por duelo 73.768
- **SUM**: #214 39 %, básico 35 %, #215 25 % · daño medio por duelo 52.702
- **RF**: #263 62 %, #262 26 %, básico 12 %, #261 0 % · daño medio por duelo 63.423

