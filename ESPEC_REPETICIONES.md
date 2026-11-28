# Especificación: repeticiones y cámara de grabación

**Estado: BORRADOR para tu aprobación (2026-11-28). No hay nada programado.** No tiene costo en dinero: todo es código propio y local.

## 1. Qué vas a poder hacer

1. **Grabar con OBS una pelea de bots** viéndolo todo, sin niebla, moviendo tú la cámara.
2. **Guardar partidas** y volver a verlas con pausa, cámara lenta y rápida, adelantar, retroceder y saltar a un minuto.
3. Mirar la repetición **desde otros ángulos** y **sin interfaz** para filmar.

No es un video: el servidor guarda los mensajes de la partida con su hora, y el juego los vuelve a representar.

## 2. De dónde partimos (probado en el juego el 2026-11-27/28)

| Qué | Resultado |
|---|---|
| Mirar lejos de tu campeón, con zoom hasta 1,8 | Se ven campeones, oleadas, monstruos, efectos y barras de vida de lo que ve tu equipo; la niebla se mantiene |
| Daño de peleas ajenas | **No se ve.** El servidor envía cada golpe solo al atacante (o su dueño) y a la víctima |
| `/mobafight 2` (solo mirar) | **No sirve para filmar:** sin barras de vida (ni la tuya), minimapa congelado con bots anteriores, y sigue la niebla del equipo azul |
| `/mobafight 6` | Te deja en el equipo azul (la ayuda dice «solo miras») |
| Un cliente + OBS (NVENC) | 97 s a 1364×768 y 60 FPS: el juego siguió en 60 FPS; OBS tuvo 3 cuadros atrasados al dibujar y 3 saltados al codificar, de 5.857; GPU de 30-33 % a 45-49 %; unos 68 MB por minuto, con audio |
| Avisos del MU normal | En la arena aparece «Chaos Castle entrance is open…»; saldría en tus videos |

Por qué falla `/mobafight 2`: los envíos de barras de vida, minimapa y fuentes se saltan a quien no tiene equipo (el marcador también). Como el cliente nunca recibe «ya no tienes equipo», conserva el anterior.

Del código: si el cliente no tiene equipo, no hay niebla, pero entonces se oye **toda** la arena sin límite de distancia. El daño ajeno no necesita cambios para dibujarse: el cliente lo dibuja sobre cualquier objetivo que reciba.

## 3. Etapa A: espectador para filmar en vivo

Es la parte pequeña, permite filmar ya con la cámara actual y es la base de la grabación.

- **Servidor:** un estado «espectador» explícito, solo para administradores. Recibe barras de vida, minimapa de **ambos** equipos, marcador, fuentes, objetivos y todos los golpes. Se le avisa que no tiene equipo.
- Los bots, torres, monstruos y oleadas lo ignoran; no cuenta para nada de la partida y los demás no lo ven.
- **Cliente:** sin niebla y **el sonido sigue a la cámara** (14 casillas).
- `/mobafight 2` usa este estado. `/mobafight 6` pasa a ser de solo mirar; `/mobafight 6 jungla` sigue igual.
- Mientras seas espectador no se muestran los avisos de eventos del MU normal.
- **Terminado cuando:** en `/mobafight 2` ves barras de ambos colores, minimapa en vivo, daño de las peleas, nada de niebla, y oyes solo lo que mira la cámara.

## 4. Etapa B: grabar y reproducir (pausa)

### Grabar (en el servidor)
- Un **grabador invisible** por partida que actúa como el espectador de la etapa A. En vez de enviar por red, escribe en un archivo cada mensaje con su hora.
- **Sin probar:** que se pueda reutilizar el código que ya arma los mensajes para un jugador, dándole una conexión falsa. Es lo primero que se prueba. Si no resulta, se cambia el diseño antes de seguir.
- Un fallo del grabador **nunca** debe afectar la partida: se corta la grabación y se anota en el registro.

### Guardar
- Carpeta `replays/` del servidor; un archivo comprimido por partida.
- Cabecera: versión del formato, versión mínima del cliente, fecha, tipo de partida, jugadores y campeones, duración y ganador. Después, la lista de mensajes con su hora en milisegundos.
- El tamaño por minuto **todavía no se conoce**; se mide en la primera prueba.
- No se guardan chats privados.
- Una repetición vieja puede dejar de verse bien tras una actualización. El juego avisa si la versión no coincide; no se promete compatibilidad.

### Reproducir (en el cliente, sin servidor)
- El cliente lee el archivo y mete cada mensaje, a su hora, en la misma cola por la que hoy entran los del servidor (`IncomingPacketQueue`).
- Modo repetición: no envía nada, no tienes campeón (cámara libre por el mapa) y se oculta lo que es de un jugador (tienda, inventario, habilidades).
- **El punto más incierto:** que el cliente entre al mapa sin pasar por el inicio de sesión. Se prueba al principio.

### Pausa
- Hoy un solo reloj (`WorldTime`) mueve el juego; aparece en 94 archivos y se asigna en un solo lugar. Se convierte en un **reloj de la repetición** que se puede detener. La cámara y los botones pasan a usar el reloj real, para moverte en pausa.
- Hay además otros 125 usos de relojes del sistema y puede haber animaciones que avanzan por cuadro. Hay que revisarlos probando: la pausa está bien cuando dos capturas separadas por 10 s, sin mover la cámara, no muestran personajes ni efectos en otro lugar.

**Terminado cuando:** grabas una pelea de bots, la abres con el servidor apagado, la ves de principio a fin y puedes pausar y mover la cámara.

## 5. Etapa C: cámara de grabación

- **Cámara libre** en el `Main.exe` Release (hoy `FreeFly` solo está en la versión de editor): girar, inclinar, subir, bajar y acercar. Solo en repeticiones y para el espectador de la etapa A. Un jugador en partida nunca la tiene.
- **Ocultar interfaz** con una tecla (propuesta: `Ctrl+H`): quita HUD, minimapa, chat, avisos, contador y puntero. Las barras de vida y el daño se encienden o apagan aparte. No encontré una función que ya haga esto.
- **Seguir a un campeón** (teclas `1` a `0`) o moverte libre.
- **Riesgo sin comprobar:** el mapa se dibuja pensando en la vista desde arriba. Con la cámara baja podrían verse bordes vacíos u objetos que aparecen de golpe. Se prueba primero con una versión mínima; según el resultado se fija un límite de inclinación.
- **Terminado cuando:** filmas con OBS un giro alrededor de una pelea, sin interfaz y sin huecos visibles dentro de los límites fijados.

## 6. Etapa D: velocidad y adelantar

- Velocidades 0,25×, 0,5×, 1×, 2× y 4×.
- Las lentas salen del reloj de la repetición. Las rápidas necesitan calcular varios pasos por cada cuadro dibujado, porque el juego limita cuánto avanza una animación por cuadro.
- A 2× y 4× se pueden silenciar los efectos.
- Controles propuestos: barra inferior con tiempo y botones; `Espacio` pausa, `←` `→` saltan 10 s y `+` `-` cambian la velocidad.

## 7. Etapa E: saltar y retroceder

El cliente arma el mundo mensaje por mensaje, así que no sabe andar hacia atrás.

- Cada **30 s** el grabador guarda una **foto completa**: lo que recibiría un espectador que entrara en ese instante (objetos, posiciones, vida, estructuras, efectos activos, marcador).
- Saltar = limpiar el mundo, cargar la foto anterior más cercana y procesar rápido, sin dibujar, hasta el minuto pedido.
- Pérdida aceptada: los efectos que iban por el aire en el instante de la foto no aparecen.
- La barra marca muertes, torres y objetivos.
- **Terminado cuando:** saltas a cualquier minuto en pocos segundos y quedan igual las posiciones, la vida y las torres que viendo la partida completa.

## 8. Etapa F: lista de repeticiones

Pantalla en el juego con fecha, tipo, campeones, duración y ganador, con opciones para abrir, borrar y marcar «conservar». Antes de esto se abren con un comando o haciendo doble clic en un archivo.

## 9. Fuera de esta versión

- Ver la repetición con el HUD de un jugador (su tienda, oro y recargas).
- Compartir repeticiones con otras personas desde el juego o verlas desde una web.
- Convertir a video automáticamente (para eso está OBS).
- Ver en vivo la partida de otros con retraso.

## 10. Orden y control

| Orden | Etapa | Tamaño relativo | Riesgo |
|---|---|---|---|
| 1 | A. Espectador para filmar | Pequeña | Bajo |
| 2 | B. Grabar, reproducir y pausar | **La más grande** | **Alto** (entrar sin servidor y pausar) |
| 3 | C. Cámara de grabación | Mediana | Medio (aspecto visual) |
| 4 | D. Velocidad y adelantar | Mediana | Medio |
| 5 | E. Saltar y retroceder | Grande | Alto |
| 6 | F. Lista en el juego | Pequeña | Bajo |

No doy plazos: los de B y E dependen de las pruebas. B empieza con una **prueba de viabilidad** (grabar 1 minuto, entrar sin servidor y pausar). Si alguna de las tres falla, te traigo las alternativas antes de seguir.

**Efecto en el balance:** A, B y E cambian el servidor. No deben cambiar el resultado de los duelos, pero hay que comprobarlo antes de reanudar una medición a medias: **conviene terminar la confirmación de 100 duelos antes de empezar a programar A.**

## 11. Decisiones que necesito de ti

| N.º | Pregunta | Mi propuesta |
|---|---|---|
| 1 | ¿Qué se ve en una repetición? | Todo, sin niebla. Ver «como el equipo azul o rojo» queda para después |
| 2 | ¿Qué partidas se graban? | Automáticas si hay al menos un jugador humano; las de bots, con un comando |
| 3 | ¿Cuántas se conservan? | Las últimas 20, más las marcadas «conservar» (se ajusta al conocer el tamaño) |
| 4 | ¿Dónde se ven al principio? | Solo en tu PC |
| 5 | ¿Quién tiene la cámara libre? | Cualquiera dentro de una repetición; en vivo, solo el administrador como espectador |
| 6 | ¿Daño en repeticiones? | Encendido solo para golpes entre campeones, con un botón para ver todo o nada |
| 7 | ¿Se aprueba el orden A → F? | Sí. Con A ya puedes filmar peleas de bots |
| 8 | Jugando, ¿quieres ver el daño de las peleas de tus aliados? | No cambiarlo: hoy solo ves tu propio daño, estés cerca o lejos, igual que en el MU normal |
