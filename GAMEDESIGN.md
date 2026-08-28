# GAMEDESIGN — Modo de juego custom sobre OpenMU (S6E3)

> Documento de diseño. Contexto trasladado desde una conversación previa para
> centralizar el trabajo en este repositorio. A partir de ahora el diseño y el
> desarrollo avanzan únicamente aquí.

---

## Visión general

Un modo de juego custom estilo **MOBA** (inspirado en League of Legends)
construido sobre el motor y los assets de **MU Online Season 6**, pensado para
eventualmente abrirse a un **servidor comunitario público**.

El desarrollo es **por fases**, empezando por un **ARAM simple** y escalando
hacia un **MOBA completo** si el resultado funciona bien.

---

## Restricciones técnicas confirmadas

- El servidor corre sobre **OpenMU** con protocolo **Season 6 Episodio 3
  (S6E3)**. No es posible usar clientes de seasons posteriores (S19, etc.)
  porque el protocolo de red es **incompatible**.
- Personajes / skills / ítems de seasons posteriores a S6 **se pueden recrear
  manualmente** (stats, lógica, mecánica) pero corriendo bajo cliente y
  protocolo S6 — **reutilizando assets visuales existentes del cliente S6** en
  vez de assets de esas seasons (por compatibilidad de formato y por copyright).
- El desarrollo se hace **100% local primero** (sin costos). Solo se contratará
  un **VPS** cuando el modo de juego esté listo para abrirse al público.

---

## Fase 1 — ARAM básico (prioridad actual)

- **Un solo mapa** reciclado de Season 6 (a definir cuál; forma
  alargada / angosta preferible), acordonado como arena.
- **Cooldowns y daño de skills editables** vía configuración / código para
  balancear el modo.
- Los personajes **empiezan en nivel 100** (temporal, dentro de la instancia),
  con **snapshot** del personaje real que se **restaura al salir**.
- **Experiencia acelerada** dentro de la partida para subir de nivel y escalar
  el daño de las skills.
- **Reducción de cooldown por nivel** dentro de la partida (regla custom, no
  nativa de MU).
- **Tienda de ítems**: moneda de partida **separada del Zen normal**, catálogo
  de ítems reescalados o custom, vendida vía **NPC vendedor** (reutilizando el
  sistema de NPC vendedor existente).

---

## Fase 2 — Oleadas de mobs (push de línea)

- **Mobs de oleada** con **ruta de waypoints fija** que avanzan por un carril,
  atacando solo si detectan enemigos en el camino (reutilizando la **IA de
  agresividad** existente de OpenMU).
- **Torretas**: NPCs estáticos (velocidad de movimiento 0) con **skill de
  ataque a rango**, agrediendo automáticamente a lo que entre en su radio según
  **facción / bando**.
- **Base enemiga**: reutilizar la lógica de **puertas de Castle Siege**
  (estructura con HP que dispara un evento de victoria / derrota al ser
  destruida). Confirmado que el plugin de **Castle Siege ya viene activo por
  defecto** en OpenMU.
- **Colisión / pathing custom** solo aplica a la **IA de los mobs de oleada**
  (para que sigan su carril). Los **jugadores se mueven libres** por todo el
  terreno caminable normal, **sin muros artificiales** — permite roams / ganks
  entre líneas como en un MOBA real.

---

## Fase 3 — Escalado a MOBA completo (visión a futuro, sin decidir aún)

- En vez de forzar **3 carriles en un solo mapa** (los mapas de MU no tienen ese
  diseño geométrico), usar **varios mapas de MU distintos, uno por carril / zona**
  (top / mid / bot / jungla), conectados por **portales tipo Lost Tower**
  (reutilizando el sistema de **warp existente entre pisos**).
- **Jungla** como mapa central propio, con **mobs estáticos que darían buffs al
  morir** (mecánica custom, no existe nativamente en MU).
- **Alternativa explorada y descartada por alta complejidad**: crear un mapa
  único con terreno editado a mano combinando zonas temáticas (requiere
  herramientas de edición 3D; esfuerzo mucho mayor que la opción de
  multi-mapa + portales).
- **Pendiente resolver**: sincronización de estado (oleadas, torretas caídas,
  temporizador de partida) entre **múltiples instancias de mapa simultáneas**.

---

## Decisión pendiente

Aún **no se ha cerrado el alcance exacto de la Fase 1**:

- Qué mapa de S6 reciclar.
- Valores exactos de nivel / daño / cooldown.
- Diseño de la tienda (moneda, catálogo, precios, cómo se gana la moneda).

Antes de empezar a programar, se cierran estas decisiones **una por una**.

### Registro de decisiones cerradas

_(Se irá completando a medida que se resuelvan las preguntas de la Fase 1.)_

| # | Decisión | Valor acordado | Fecha |
|---|----------|----------------|-------|
| — | — | — | — |
