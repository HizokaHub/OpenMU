# Bot de pruebas del MOBA (piloto)

Maneja el cliente `Main.exe` como una persona (lanzar, capturar, clic, teclas, chat) y lee el log del servidor. Todo local y gratis.
Sirve para repetir pruebas de la interfaz (tienda, HUD, menus) despues de cada cambio y para dejarle capturas a quien revisa.

## Antes de usarlo
- El servidor debe estar arriba (`tools\iniciar-todo.ps1 -Estado`).
- **No toques el raton ni el teclado mientras corre**: el bot los controla. Mover el raton a una esquina de la pantalla lo aborta.
- Solo un `Main.exe` a la vez; el bot nunca mata procesos (`cerrar` pide el cierre a la ventana).
- Cuenta de prueba: copia `cuenta.local.json.ejemplo` a `cuenta.local.json` (no se sube a git) con una cuenta GM de prueba.
- En Git Bash exporta `MSYS_NO_PATHCONV=1` antes de enviar comandos con `/` (si no, `/moba` se convierte en una ruta de Windows).

## Comandos (`python -I piloto.py ...`)
`lanzar`, `foto <nombre>`, `clic X Y [derecho]`, `mover X Y`, `escribir "texto"`, `tecla enter`, `chat "/moba"`, `login`, `log [N]`, `estado`, `cerrar`.
Las coordenadas son las de la captura (ventana de 1366x768). Las capturas quedan en `capturas/` (ignoradas por git).

## Recorrido conocido (ventana de 1366x768, cuenta GM `testgmDw`)
1. `lanzar` -> clic (503,301) "Valhalla" -> clic (680,301) "Valhalla-1" -> `login` -> clic OK (693,563).
2. Personaje: clic (515,520) y "Connect" (1262,697) -> a Lorencia.
3. `chat "/moba"` -> arena. Herrero: clic (625,262). Pestañas: PARA TI (84,78), CONSUM (214,78), ARMAS (349,78), SETS T1 (482,78), SETS T2 (615,78), SETS T3 (745,78), MASCOTAS (878,78).
4. `chat "/mobaoro 50000"`; seleccionar un item de la rejilla; COMPRAR (830,541), Vender (830,585), Deshacer (830,626).

## Limites
- Depende de coordenadas: si cambia el diseno del panel hay que reajustarlas.
- Las pulsaciones y los clics necesitan una pausa (el juego no ve las instantaneas); ya esta en `piloto.py`.
- No oye la musica ni juzga el sonido.
