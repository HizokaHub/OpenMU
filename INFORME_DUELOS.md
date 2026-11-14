# INFORME DE DUELOS 1v1

Generado por `tools/informe_duelos.py` a partir de `/mobaduel` (servidor, sin cliente, IA real de `MobaBotPlayer`, nivel 30, skills a rango 5).
Meta del balance: **4-6 victorias de 10** en cada cruce.

## Sin ítems (equipo inicial del clon)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_sin_items_20261113_163238.json, duelos_sin_items_20261113_223559.json, duelos_sin_items_20261113_225219.json.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 5-5 | 10-0 ⚠ | 0-10 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ |
| **BK** | 9-1 ⚠ | 3-4-3e | 0-10 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ |
| **FE** | 10-0 ⚠ | 10-0 ⚠ | 4-6 | 10-0 ⚠ | 10-0 ⚠ | 3-6-1e ⚠ | 10-0 ⚠ |
| **MG** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 3-5-2e | 10-0 ⚠ | 0-10 ⚠ | 8-0-2e ⚠ |
| **DL** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 2-3-5e | 0-10 ⚠ | 8-1-1e ⚠ |
| **SUM** | 0-10 ⚠ | 0-10 ⚠ | 6-3-1e ⚠ | 10-0 ⚠ | 10-0 ⚠ | 4-5-1e | 10-0 ⚠ |
| **RF** | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-8-2e ⚠ | 1-8-1e ⚠ | 0-10 ⚠ | 3-6-1e ⚠ |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 70 | 73 % | 11 | 52 % |
| Blade Knight (BK) | 70 | 70 % | 9 | 30 % |
| High Elf (FE) | 60 | 89 % | 13 | 64 % |
| Magic Gladiator (MG) | 60 | 32 % | 10 | 12 % |
| Dark Lord (DL) | 60 | 14 % | 9 | 7 % |
| Summoner (SUM) | 60 | 61 % | 8 | 27 % |
| Rage Fighter (RF) | 60 | 4 % | 9 | 5 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 5-5, BK 3-4-3e, FE 4-6, MG 3-5-2e, DL 2-3-5e, SUM 4-5-1e, RF 3-6-1e

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 plain`)

- SUM 10 - 0 RF de 10  → `/mobaduel sum-rf 30 plain`
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
- GM 10 - 0 BK de 10  → `/mobaduel gm-bk 30 plain`
- MG 8 - 0 RF (2 empates) de 10  → `/mobaduel mg-rf 30 plain`
- BK 9 - 1 GM de 10  → `/mobaduel bk-gm 30 plain`
- DL 8 - 1 RF (1 empates) de 10  → `/mobaduel dl-rf 30 plain`
- FE 3 - 6 SUM (1 empates) de 10  → `/mobaduel fe-sum 30 plain`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: #39 37 %, básico 35 %, #2 22 %, #3 5 %, #5 0 % · daño medio por duelo 81.342
- **BK**: básico 30 %, #43 19 %, #232 19 %, #42 17 %, #21 8 %, #22 4 % · daño medio por duelo 81.395
- **FE**: #51 41 %, básico 33 %, #235 20 %, #52 6 %, #24 0 % · daño medio por duelo 126.823
- **MG**: básico 44 %, #2 18 %, #1 13 %, #41 11 %, #236 8 %, #237 6 % · daño medio por duelo 70.997
- **DL**: #65 63 %, básico 19 %, #61 10 %, #66 7 % · daño medio por duelo 70.662
- **SUM**: básico 77 %, #214 17 %, #215 5 % · daño medio por duelo 64.964
- **RF**: #263 50 %, #262 33 %, básico 13 %, #261 4 % · daño medio por duelo 85.603

## Con set completo T3 (set, arma, escudo y accesorios)

Nivel 30, todas las skills a rango 5, tiempo máximo 120 s por duelo. Archivos: duelos_t3_20261113_224246.json.

Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.

| vs → | GM | BK | FE | MG | DL | SUM | RF |
|---|---|---|---|---|---|---|---|
| **GM** | 2-7-1e ⚠ | 8-2 ⚠ | 9-1 ⚠ | 2-8 ⚠ | 1-8-1e ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **BK** | 2-8 ⚠ | 1-5-4e ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **FE** | 1-9 ⚠ | 10-0 ⚠ | 3-5-2e | 1-7-2e ⚠ | 0-10 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **MG** | 8-2 ⚠ | 10-0 ⚠ | 7-1-2e ⚠ | 8-2 ⚠ | 2-8 ⚠ | 0-10 ⚠ | 0-10 ⚠ |
| **DL** | 8-1-1e ⚠ | 10-0 ⚠ | 10-0 ⚠ | 8-2 ⚠ | 5-5 | 0-10 ⚠ | 0-10 ⚠ |
| **SUM** | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 5-3-2e | 0-10 ⚠ |
| **RF** | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 10-0 ⚠ | 7-3 ⚠ |

### Totales por clase (sin contar espejos)

| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |
|---|---|---|---|---|
| Grand Master (GM) | 60 | 34 % | 5 | 29 % |
| Blade Knight (BK) | 60 | 3 % | 1 | 16 % |
| High Elf (FE) | 60 | 22 % | 4 | 34 % |
| Magic Gladiator (MG) | 60 | 47 % | 5 | 23 % |
| Dark Lord (DL) | 60 | 61 % | 5 | 20 % |
| Summoner (SUM) | 60 | 83 % | 4 | 27 % |
| Rage Fighter (RF) | 60 | 100 % | 4 | 30 % |

**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): GM 2-7-1e, BK 1-5-4e, FE 3-5-2e, MG 8-2, DL 5-5, SUM 5-3-2e, RF 7-3

### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 t3`)

- SUM 0 - 10 RF de 10  → `/mobaduel sum-rf 30 t3`
- DL 0 - 10 RF de 10  → `/mobaduel dl-rf 30 t3`
- DL 0 - 10 SUM de 10  → `/mobaduel dl-sum 30 t3`
- MG 0 - 10 RF de 10  → `/mobaduel mg-rf 30 t3`
- MG 0 - 10 SUM de 10  → `/mobaduel mg-sum 30 t3`
- FE 0 - 10 RF de 10  → `/mobaduel fe-rf 30 t3`
- FE 0 - 10 SUM de 10  → `/mobaduel fe-sum 30 t3`
- FE 0 - 10 DL de 10  → `/mobaduel fe-dl 30 t3`
- BK 0 - 10 RF de 10  → `/mobaduel bk-rf 30 t3`
- BK 0 - 10 SUM de 10  → `/mobaduel bk-sum 30 t3`
- BK 0 - 10 DL de 10  → `/mobaduel bk-dl 30 t3`
- BK 0 - 10 MG de 10  → `/mobaduel bk-mg 30 t3`
- BK 0 - 10 FE de 10  → `/mobaduel bk-fe 30 t3`
- GM 0 - 10 RF de 10  → `/mobaduel gm-rf 30 t3`
- GM 0 - 10 SUM de 10  → `/mobaduel gm-sum 30 t3`
- GM 9 - 1 FE de 10  → `/mobaduel gm-fe 30 t3`
- GM 1 - 8 DL (1 empates) de 10  → `/mobaduel gm-dl 30 t3`
- GM 8 - 2 BK de 10  → `/mobaduel gm-bk 30 t3`
- MG 2 - 8 DL de 10  → `/mobaduel mg-dl 30 t3`
- FE 1 - 7 MG (2 empates) de 10  → `/mobaduel fe-mg 30 t3`
- GM 2 - 8 MG de 10  → `/mobaduel gm-mg 30 t3`

### De dónde sale el daño (promedio por duelo, todas las skills y el básico)

- **GM**: básico 90 %, #39 10 % · daño medio por duelo 29.136
- **BK**: básico 91 %, #19 9 % · daño medio por duelo 2.941
- **FE**: básico 82 %, #51 18 % · daño medio por duelo 34.516
- **MG**: básico 98 %, #1 2 % · daño medio por duelo 29.067
- **DL**: básico 91 %, #65 9 % · daño medio por duelo 30.345
- **SUM**: básico 92 %, #214 8 % · daño medio por duelo 29.387
- **RF**: básico 91 %, #263 9 % · daño medio por duelo 42.180

