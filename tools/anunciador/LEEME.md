# Sonidos del anunciador del MOBA

Los 22 WAV de `mu-main/src/bin/Data/Sound/MobaAnn/` (mono, 44,1 kHz, 16 bits) los genera este directorio. Voz neuronal local (Piper) más efectos de síntesis propia (sin muestras de terceros). Todo corre en local y es gratis.

## El set instalado
- **Voz:** Piper `es_MX-claude-high` (hombre, español de México), procesada: más grave, con cuerpo de pecho, doble voz, eco corto y sala.
- **Guion:** kills y rachas en inglés con **ortografía fonética** para que la voz española las diga bien («Dábol Kil», «Trípol Kil», «Ferst Blad», «Chatdáun», «Kílin Spri»…); objetivos y partida en español épico («Cae la noche. Empieza la guerra.», «Gloria eterna. Victoria.»…). Ver `piper_voces.py` (`FRASES` y `FONETICAS`).
- **Efectos por evento:** timbal, sub, crash, clang, gong, metales, coro, riser, rugido, alarma y destello (`producir.py`, funciones `r_*`). La voz baja el fondo mientras habla y todos los sonidos tienen el mismo nivel (-17 dBFS RMS).

## Regenerarlos
**Sin descargar nada:** las tomas crudas aprobadas están en `voces_crudas/` (22 normales + 10 fonéticas, 2 MB) y el procesado es determinista:
```
pwsh tools\anunciador\generar-set.ps1 -Salida <carpeta>
```
produce **exactamente** los WAV instalados (solo hace falta python con numpy y scipy). Sirve para cambiar efectos, niveles o sala sin tocar la voz.

**Con tomas nuevas (Piper):** Piper es estocástico, así que cada toma sale un poco distinta y el resultado no será idéntico. Descargas únicas (fuera del repo; guardar donde quieras):
- **Piper 2023.11.14-2 para Windows:** `https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip` (22.477.236 bytes, MIT). Descomprimir y usar `piper\piper.exe`.
- **Voz `es_MX-claude-high`:** `https://huggingface.co/rhasspy/piper-voices/resolve/main/es/es_MX/claude/high/es_MX-claude-high.onnx` (63.122.309 bytes, SHA-256 `3ef40a71ea63852cd8ab7e6fa7d2ecdcfa67a0b47c9c48e3f10e02ee02083ea0`, Apache-2.0) y su `.onnx.json` (4.963 bytes) en la misma carpeta.

Luego:
```
pwsh tools\anunciador\generar-set.ps1 -Piper <piper.exe> -Modelo <es_MX-claude-high.onnx> -Salida <carpeta>
```
En los dos casos, copiar los WAV a `mu-main/src/bin/Data/Sound/MobaAnn/` (y a `out/build/windows-x64/src/Release/Data/Sound/MobaAnn/`).

## Otros archivos
- `producir.py`: mezcla. Variables de entorno `VOZ_ESTILO` (`piper` o `helena`) y `VOZ_PERFIL` (`clasico` = el instalado; `claro` = menos eco y más presencia).
- `generar-voces.ps1`: primera versión con la voz Microsoft Helena de Windows (`pwsh`), ya sin uso.
- `piper_partes.py`: estilo experimental de **impacto** (primera palabra alargada + golpe + palabra final grave); se probó y no gustó (demasiado eco), se deja por si se retoma.
- `diagnostico.py`: espectrogramas para revisar a ojo; `comparar.py`: compara dos carpetas de WAV.

## Reemplazarlos por voces reales
Cualquier WAV mono de 44,1 kHz con el mismo nombre sirve: el cliente lee la duración del propio archivo. Claves y su evento (ver `World/Moba/MobaAnnouncer.cpp`):
`first_blood`, `double`, `triple`, `quadra`, `penta`, `ace` (nuestro equipo aniquila al rival), `ace_lost` (nos aniquilan), `shutdown`, `spree`, `dominating`, `unstoppable`, `legendary`, `turret_ally` (perdemos una torre), `turret_enemy`, `nexus_attack`, `match_start`, `victory`, `defeat`, `golden_flyover`, `golden_invaders`, `dragon_ally`, `dragon_enemy`.
