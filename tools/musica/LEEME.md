# Música ambiental del MOBA

Cuatro pistas en bucle (síntesis propia, sin muestras de terceros), con un tema recurrente de cuatro compases («tema de Eclipse Arena», Re menor y Re mayor):

| Pista | Cuándo suena | Compás / duración | Carácter |
|---|---|---|---|
| `moba_prep` | cuenta atrás, antes de empezar | 60 BPM, 16 compases (64 s) | tensa: drone, latido de timbal, arpa escasa, coro que sube |
| `moba_early` | desde el inicio de la partida | 72 BPM, 32 compases (107 s) | serena y oscura: cuerdas largas, tema en el corno al fondo |
| `moba_mid` | 10 min después del inicio | 100 BPM, 40 compases (96 s) | batalla: ostinato de cuerdas, timbales, caja, tema en metales |
| `moba_golden` | desde el sobrevuelo de los dragones dorados | 112 BPM, 40 compases (86 s) | heroica en Re mayor: coro, metales, arpa rápida, redobles |

Al terminar la partida suenan `CW_win.wav` / `CW_lose.wav` de Crywolf (música original de MU, `Data\Sound\w35`) y silencio.

## Regenerar
Necesita Python con numpy, scipy y ffmpeg con `libvorbis` (todo local y gratis):
```
python -I tools\musica\componer.py <carpeta_wav> [prep early mid golden]
ffmpeg -i <carpeta_wav>\mid.wav -c:a libvorbis -q:a 4 moba_mid.ogg      (igual con las otras)
```
Copiar los OGG a `mu-main/src/bin/Data/Music/` (y a `out/build/windows-x64/src/Release/Data/Music/`). Son deterministas (semilla fija): el mismo script da los mismos WAV.
Cada pista se renderiza con unos segundos de cola que se pliegan sobre el principio, así el bucle es continuo; la mezcla pasa por un ecualizador automático por bandas (`igualar_bandas`) que reparte la energía (22 % sub, 38 % graves, 30 % medios, 8 % agudos, 2 % aire) para que no tape el bajo.

`analizar.py <wav> [imagen.png]` mide tempo, tonalidad probable, reparto de energía por bandas y nivel RMS: así se compararon `CW_win` y `CW_lose` (~140 BPM, 80 % de la energía bajo 400 Hz) con las pistas nuevas.

## Cómo la usa el cliente
`World/Moba/MobaMusic.cpp` (cliente): elige la pista según la fase (eventos del anunciador y el tiempo desde el inicio), la repite al terminar y desvanece 1,5 s entre pistas (`AudioPlayer::FadeOutMusic`). Se llama cada fotograma desde `ManageBackgroundMusic` (`SceneManager.cpp`).

## Cambiarla
Para otro estilo, editar las funciones `pista_*` de `componer.py` (progresión de acordes, patrones, ganancias) o reemplazar los OGG por música propia con los mismos nombres: el cliente no depende de la duración.
