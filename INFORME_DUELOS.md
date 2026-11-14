# INFORME DE DUELOS 1v1

Generado por `tools/informe_duelos.py` a partir de `/mobaduel` (servidor, sin cliente, IA real de `MobaBotPlayer`, nivel 30, skills a rango 5).
Meta del balance: **4-6 victorias de 10** en cada cruce.

## Sin ítems (equipo inicial del clon)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_sin_items_20261113_163238.json, duelos_sin_items_20261113_223559.json, duelos_sin_items_20261113_225219.json, duelos_sin_items_20261113_231856.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 0.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 3-7 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ |
| **BK** | 9-1 ⚠ | 5-4-1e | 0-10 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ |
| **FE** | 10-0 ⚠ | 10-0 ⚠ | 5-5 | 10-0 ⚠ | 10-0 ⚠ | 7-3 ⚠ | 10-0 ⚠ |
| **MG** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 3-5-2e | 10-0 ⚠ | 0-10 ⚠ | 8-1-1e ⚠ |
| **DL** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 4-3-3e | 0-10 ⚠ | 10-0 ⚠ |
| **SUM** | 0-10 ⚠ | 0-10 ⚠ | 3-7 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 7-3 ⚠ | 9-0-1e ⚠ |
| **RF** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 1-8-1e ⚠ | 0-10 ⚠ | 0-9-1e ⚠ | 7-3 ⚠ |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 70 | 59 % | 13 | 43 % |
| Blade Knight (BK) | 70 | 84 % | 11 | 28 % |
| High Elf (FE) | 60 | 95 % | 13 | 58 % |
| Magic Gladiator (MG) | 60 | 31 % | 10 | 11 % |
| Dark Lord (DL) | 60 | 17 % | 10 | 7 % |
| Summoner (SUM) | 60 | 54 % | 8 | 24 % |
| Rage Fighter (RF) | 60 | 3 % | 9 | 8 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 3-7, BK 5-4-1e, FE 5-5, MG 3-5-2e, DL 4-3-3e, SUM 7-3, RF 7-3

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 plain`)

- DL 10 - 0 RF de 10  → `/mobaduel dl-rf 30 plain`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 plain`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 plain`
- MG 10 - 0 DL de 10  → `/mobaduel mg-dl 30 plain`
- FE 10 - 0 RF de 10  → `/mobaduel fe-rf 30 plain`
- FE 10 - 0 DL de 10  → `/mobaduel fe-dl 30 plain`
- FE 10 - 0 MG de 10  → `/mobaduel fe-mg 30 plain`
- BK 10 - 0 RF de 10  → `/mobaduel bk-rf 30 plain`
- BK 10 - 0 SUM de 10  → `/mobaduel bk-sum 30 plain`
- BK 10 - 0 DL de 10  → `/mobaduel bk-dl 30 plain`
- BK 10 - 0 MG de 10  → `/mobaduel bk-mg 30 plain`
- BK 0 - 10 FE de 10  → `/mobaduel bk-fe 30 plain`
- GM 10 - 0 RF de 10  → `/mobaduel gm-rf 30 plain`
- GM 10 - 0 SUM de 10  → `/mobaduel gm-sum 30 plain`
- GM 10 - 0 DL de 10  → `/mobaduel gm-dl 30 plain`
- GM 10 - 0 MG de 10  → `/mobaduel gm-mg 30 plain`
- GM 0 - 10 FE de 10  → `/mobaduel gm-fe 30 plain`
- GM 0 - 10 BK de 10  → `/mobaduel gm-bk 30 plain`
- SUM 9 - 0 RF (1 empates) de 10  → `/mobaduel sum-rf 30 plain`
- BK 9 - 1 GM de 10  → `/mobaduel bk-gm 30 plain`
- MG 8 - 1 RF (1 empates) de 10  → `/mobaduel mg-rf 30 plain`
- FE 7 - 3 SUM de 10  → `/mobaduel fe-sum 30 plain`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 38 %, básico 34 %, #2 23 %, #3 5 %, #5 0 % · daño medio por duelo 90.417
- **BK**: básico 30 %, #43 20 %, #232 18 %, #42 18 %, #21 8 %, #22 4 % · daño medio por duelo 90.416
- **FE**: #51 41 %, básico 32 %, #235 20 %, #52 6 %, #24 0 % · daño medio por duelo 125.789
- **MG**: básico 43 %, #2 18 %, #1 13 %, #41 12 %, #236 8 %, #237 6 % · daño medio por duelo 70.655
- **DL**: #65 65 %, básico 18 %, #61 10 %, #66 7 % · daño medio por duelo 73.243
- **SUM**: básico 78 %, #214 17 %, #215 5 % · daño medio por duelo 63.623
- **RF**: #263 51 %, #262 34 %, básico 10 %, #261 5 % · daño medio por duelo 84.073

## Con set completo T3 (set, arma, escudo y accesorios)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_t3_20261113_224246.json, duelos_t3_20261113_232956.json. Duelos descartados por daño ajeno (monstruo, minion, torre): 0.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 6-2-2e ⚠ | 10-0 ⚠ | 10-0 ⚠ | 1-7-2e ⚠ | 3-6-1e ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **BK** | 0-10 ⚠ | 2-4-4e | 1-9 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **FE** | 0-10 ⚠ | 9-1 ⚠ | 4-5-1e | 0-10 ⚠ | 1-9 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **MG** | 7-1-2e ⚠ | 10-0 ⚠ | 10-0 ⚠ | 6-3-1e ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **DL** | 6-3-1e ⚠ | 10-0 ⚠ | 9-1 ⚠ | 10-0 ⚠ | 6-4 | 0-10 ⚠ | 0-10 ⚠ |
| **SUM** | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 3-2-5e | 0-10 ⚠ |
| **RF** | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 3-6-1e ⚠ |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 60 | 42 % | 6 | 28 % |
| Blade Knight (BK) | 60 | 2 % | 3 | 9 % |
| High Elf (FE) | 60 | 17 % | 4 | 29 % |
| Magic Gladiator (MG) | 60 | 47 % | 5 | 21 % |
| Dark Lord (DL) | 60 | 59 % | 5 | 17 % |
| Summoner (SUM) | 60 | 83 % | 4 | 26 % |
| Rage Fighter (RF) | 60 | 100 % | 5 | 28 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 6-2-2e, BK 2-4-4e, FE 4-5-1e, MG 6-3-1e, DL 6-4, SUM 3-2-5e, RF 3-6-1e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 t3`)

- SUM 0 - 10 RF de 10  → `/mobaduel sum-rf 30 t3`
- DL 0 - 10 RF de 10  → `/mobaduel dl-rf 30 t3`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 t3`
- MG 0 - 10 RF de 10  → `/mobaduel mg-rf 30 t3`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 t3`
- MG 0 - 10 DL de 10  → `/mobaduel mg-dl 30 t3`
- FE 0 - 10 RF de 10  → `/mobaduel fe-rf 30 t3`
- FE 0 - 10 SUM de 10  → `/mobaduel fe-sum 30 t3`
- FE 0 - 10 MG de 10  → `/mobaduel fe-mg 30 t3`
- BK 0 - 10 RF de 10  → `/mobaduel bk-rf 30 t3`
- BK 0 - 10 SUM de 10  → `/mobaduel bk-sum 30 t3`
- BK 0 - 10 DL de 10  → `/mobaduel bk-dl 30 t3`
- BK 0 - 10 MG de 10  → `/mobaduel bk-mg 30 t3`
- GM 0 - 10 RF de 10  → `/mobaduel gm-rf 30 t3`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 t3`
- GM 10 - 0 FE de 10  → `/mobaduel gm-fe 30 t3`
- GM 10 - 0 BK de 10  → `/mobaduel gm-bk 30 t3`
- FE 1 - 9 DL de 10  → `/mobaduel fe-dl 30 t3`
- BK 1 - 9 FE de 10  → `/mobaduel bk-fe 30 t3`
- GM 1 - 7 MG (2 empates) de 10  → `/mobaduel gm-mg 30 t3`
- GM 3 - 6 DL (1 empates) de 10  → `/mobaduel gm-dl 30 t3`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: básico 90 %, #39 10 % · daño medio por duelo 28.823
- **BK**: básico 93 %, #19 7 % · daño medio por duelo 6.884
- **FE**: básico 82 %, #51 18 % · daño medio por duelo 35.857
- **MG**: básico 99 %, #1 1 % · daño medio por duelo 28.949
- **DL**: básico 91 %, #65 9 % · daño medio por duelo 30.275
- **SUM**: básico 90 %, #214 10 % · daño medio por duelo 28.392
- **RF**: básico 89 %, #263 11 % · daño medio por duelo 44.359

