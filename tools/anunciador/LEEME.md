# Sonidos del anunciador del MOBA

Los 22 WAV de `mu-main/src/bin/Data/Sound/MobaAnn/` (mono, 44,1 kHz, 16 bits) los genera este directorio. Todo es síntesis propia (sin muestras de terceros) y corre en local, gratis.

## Regenerarlos
1. Voces crudas (Windows, voz «Microsoft Helena Desktop»; usar `pwsh`, que lee UTF-8):
   `pwsh -NoProfile -File generar-voces.ps1 -OutDir <carpeta_voz_cruda>`
2. Mezcla (voz con capas + percusión, metales, coro, gong, riser y sala):
   `python -I producir.py <carpeta_voz_cruda> <carpeta_salida> [clave ...]`
3. Revisión a ojo (espectrogramas): `python -I diagnostico.py <carpeta_salida> <imagen.png> <clave> [clave ...]`
4. Copiar los WAV a `mu-main/src/bin/Data/Sound/MobaAnn/` (y a `out/build/windows-x64/src/Release/Data/Sound/MobaAnn/`).

## Reemplazarlos por voces reales
Cualquier WAV mono de 44,1 kHz con el mismo nombre sirve: el cliente lee la duración del propio archivo. Claves y su evento (ver `World/Moba/MobaAnnouncer.cpp`):
`first_blood`, `double`, `triple`, `quadra`, `penta`, `ace` (nuestro equipo aniquila al rival), `ace_lost` (nos aniquilan), `shutdown`, `spree`, `dominating`, `unstoppable`, `legendary`, `turret_ally` (perdemos una torre), `turret_enemy`, `nexus_attack`, `match_start`, `victory`, `defeat`, `golden_flyover`, `golden_invaders`, `dragon_ally`, `dragon_enemy`.
