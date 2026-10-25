# Portal web — Eclipse Arena (MU MOBA · ONLINE)

Sitio del servidor privado de MU Online «Eclipse Arena»: portada, registro de cuentas, ranking, perfil de jugador, estado del servidor y noticias.

## Arrancar

```
dotnet run --project portal
```

Escucha en `http://0.0.0.0:8080` (cambiar en `appsettings.json` → `Urls`). Debe correr en la misma máquina que PostgreSQL o tener acceso a él.

## Configuración (`appsettings.json` → `Portal`)

| Clave | Para qué |
|---|---|
| `Name`, `Subtitle`, `Tagline`, `Notice` | Marca. `Notice` debe decir que es un servidor privado de MU Online hecho por fans. |
| `Theme` | `eclipse` (principal), `gold` (Arena Dorada), `kalima` (Kalima Clash). También `?theme=` en la URL. |
| `ConnectionString` | Base de datos `openmu`. |
| `ServerHost`, `ServerPort` | Dirección que se muestra a los jugadores y que se sondea para el estado (en VPN: la IP de la VPN). |
| `RegistrationEnabled`, `RegistrationsPerHourPerIp` | Registro abierto y límite por IP. |
| `DownloadFile` | Zip del cliente servido en `/download` (si no existe, la web lo avisa). |
| `NewsFile` | Noticias (`news.json`: `date`, `tag`, `title`, `text`). |

## Ranking

Usa el esquema opcional `moba` de `tools/moba-ranking-schema.sql` (no se aplica solo). Mientras no exista, `/api/ranking` responde `ready: false`. Con el esquema aplicado, el servidor de juego (`MobaRankingSink`) registra cada partida terminada y el estado cada 15 s.

## Seguridad

- No exponer el portal a internet sin un proxy con HTTPS; el registro tiene límite por IP pero no CAPTCHA.
- La web nunca muestra el usuario de la cuenta, solo el nombre del personaje.
- Cambiar la contraseña de PostgreSQL del `ConnectionString` antes de abrir el portal a otros.
