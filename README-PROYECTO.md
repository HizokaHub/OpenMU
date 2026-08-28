# OpenMU – Notas de instalación local (aram)

Servidor de MU Online (Season 6 Ep. 3) basado en el proyecto oficial
[MUnique/OpenMU](https://github.com/MUnique/OpenMU). Este archivo documenta cómo
quedó montado el entorno de **desarrollo local en Windows** en esta máquina.

Fecha de instalación: 2026-08-28
Carpeta del proyecto: `C:\Users\aruiz\Proyectos\openmu-aram`
Commit del repo al clonar: `f0cc11999` (rama `master`)

### Repositorio / remotes
- `origin`   → `https://github.com/HizokaHub/OpenMU.git` (fork propio)
- `upstream` → `https://github.com/MUnique/OpenMU.git` (oficial, solo lectura)

El trabajo de setup local va en la rama **`aram/local-dev-setup`** (no en `master`,
que se mantiene sincronizada con `upstream` para poder hacer `git pull upstream master`).

---

## 1. Qué se instaló

| Componente | Versión | Cómo se instaló |
|---|---|---|
| .NET SDK | **10.0.400** (net10.0) | `winget install --id Microsoft.DotNet.SDK.10 --source winget` |
| PostgreSQL | **17.11-1** (instalador EnterpriseDB) | `winget install --id PostgreSQL.PostgreSQL.17 --source winget` |
| Git | 2.53.0.windows.2 | ya estaba instalado |
| SO | Windows 10 Home 19045 | — |

No se usa Docker. No se usa Node.js (solo haría falta para la web de
documentación `docs-website`, que no se compila aquí).

### Detalles de .NET
- Instalado en `C:\Program Files\dotnet\`
- Verificar: `dotnet --version` → `10.0.400`
- Todos los `.csproj` de la solución tienen `<TargetFramework>net10.0</TargetFramework>`.

### Detalles de PostgreSQL
- Instalado en `C:\Program Files\PostgreSQL\17\`
- Servicio de Windows: **`postgresql-x64-17`** (arranque automático)
- Escucha en `localhost:5432`
- Autenticación `scram-sha-256`
- Superusuario: `postgres` / contraseña **`postgres`** (default del instalador winget)
- `psql`: `C:\Program Files\PostgreSQL\17\bin\psql.exe`

### Cambio local aplicado al repo
`src/Persistence/EntityFramework/ConnectionSettings.xml` — las 3 cadenas de
conexión que usan el rol `postgres` se cambiaron de `Password=admin` a
`Password=postgres` para que coincidan con la contraseña real del superusuario.
Los demás roles (`config`, `account`, `friend`, `guild`) los crea el servidor
solo en el primer arranque.

> Este es un cambio versionado (el archivo está en git). Si se hace `git pull`
> de `master` puede aparecer como conflicto: mantener la versión con
> `Password=postgres`.

---

## 2. Comandos para recompilar

Desde `C:\Users\aruiz\Proyectos\openmu-aram`:

```powershell
dotnet restore src\MUnique.OpenMU.sln
dotnet build   src\MUnique.OpenMU.sln -c Debug
```

### ⚠️ Quirk conocido en el PRIMER build tras un `clone` o `git clean`
El primer `dotnet build` de la solución completa puede fallar con:

```
error MSB3073: El comando "dotnet run --project ../SourceGenerator/... --no-build" salió con el código 1
```

Es un problema de orden: un paso pre-build ejecuta el generador de código
(`MUnique.OpenMU.Persistence.SourceGenerator`) con `--no-build` antes de que ese
proyecto esté compilado. Solución: compilar el generador aparte una vez y luego
repetir el build:

```powershell
dotnet build src\Persistence\SourceGenerator\MUnique.OpenMU.Persistence.SourceGenerator.csproj -c Debug
dotnet build src\MUnique.OpenMU.sln -c Debug
```

A partir de ahí los builds siguientes funcionan directo. (231 advertencias de
StyleCop/analizadores y `NU1902/NU1903` de vulnerabilidades en paquetes de
telemetría son normales y no bloquean.)

---

## 3. Comandos para correr el servidor

El proyecto de arranque es **`MUnique.OpenMU.Startup`** (all-in-one: Connect
Server + Game Servers + Chat Server + Login + panel admin en un solo proceso).

El panel admin se sirve en **http://localhost/** (puerto **80**). Ese puerto
debe estar libre; en Windows puede requerir ejecutar la terminal como
Administrador.

Desde `C:\Users\aruiz\Proyectos\openmu-aram\src\Startup`:

### Primera vez / inicializar la base de datos
```powershell
dotnet run --project src\Startup\MUnique.OpenMU.Startup.csproj -c Debug -- -autostart -resolveIp:local
```
En el primer arranque, si la base `openmu` no tiene los esquemas, el proceso los
crea junto con los roles y permisos. Luego, en el panel admin, ir a la
**Setup page** para inicializar los datos de Season 6.

### Reinicializar la BD desde cero (borra y recrea)
```powershell
dotnet run --project src\Startup\MUnique.OpenMU.Startup.csproj -c Debug -- -autostart -resolveIp:local -reinit
```

### Modo demo (todo en memoria, sin PostgreSQL, NO guarda progreso)
```powershell
dotnet run --project src\Startup\MUnique.OpenMU.Startup.csproj -c Debug -- -autostart -resolveIp:local -demo
```

### Parámetros útiles
| Parámetro | Efecto |
|---|---|
| `-autostart` | Arranca los listeners (connect/game/chat) sin tener que iniciarlos a mano en el panel |
| `-resolveIp:local` | Resuelve la IP local para conexiones desde la misma máquina |
| `-reinit` | Reinicializa la base de datos |
| `-demo` | Repositorios en memoria, datos recreados en cada arranque |

Para detener el servidor: en la consola del proceso, seguir la instrucción de
salida (normalmente `Enter` / confirmar).

---

## 4. Puertos que usa

| Puerto | Uso |
|---|---|
| 80 | Panel de administración (http://localhost/) |
| 44405 | Connect server – cliente original |
| 44406 | Connect server – cliente open source |
| 55901–55906 | Game servers |
| 55980 | Chat server |

Para pruebas con cliente en la misma máquina, usar una IP `127.x.x.x`
**distinta de `127.0.0.1`** (el cliente la bloquea). Recomendado:
`127.127.127.127`.

---

## 5. Cliente de MU Online (Season 6)

- Cliente: **`MU Client 1.04d - Season 6E3`** (cliente retail S6, `main.exe` + `Mu.exe`
  + GameGuard). Descomprimido en:
  `C:\Users\aruiz\Proyectos\mu-client-s6\MU Client 1.04d - Season 6E3\`
  (fuera del repo a propósito, ~1,1 GB).
- **No tiene archivo de conexión editable** (`IGC.dll` / `.ini` / `.cfg`). `config.ini`
  solo trae la versión; `MuEng.ini` y `Data/Local/ServerList.bmd` están cifrados.
  La IP/puerto de destino de este cliente se define en el **registro de Windows**:
  `HKLM\SOFTWARE\WebZen\Mu\Connection` (vista de 32 bits).
- Para apuntarlo al servidor local se usa el **OpenMU ClientLauncher** (ya compilado
  en `src/ClientLauncher/bin/Debug/MUnique.OpenMU.ClientLauncher.exe`). El launcher
  escribe ese registro + pasa `connect /u<ip> /p<puerto>` y arranca `main.exe`.
  - Ejecutar el launcher **como Administrador** (escribe en HKLM).
  - IP: `127.127.127.127`  ·  Puerto: `44405` (cliente retail; `44406` es para el
    cliente open source MuMain).
  - main.exe: `C:\Users\aruiz\Proyectos\mu-client-s6\MU Client 1.04d - Season 6E3\main.exe`
  - Requiere el runtime de .NET 10 (ya cubierto por el SDK instalado).
  - El launcher lee/escribe `launcher.config` (XML) en su carpeta de trabajo; se dejó
    pre-cargado con los hosts `127.127.127.127:44405` y `:44406` y la ruta de `main.exe`.
  - **Probado OK 2026-08-28**: GameGuard no dio problema, login con `test0`/`test0`
    y entrada al juego correctas.
- Cuentas de prueba (contraseña = nombre de usuario): `test0`..`test9`, `test300`,
  `test400`, `testgm`, `testgm2`, `testunlock`, `quest1`..`quest3`, `ancient`, `socket`.
- Si hay desconexión justo al elegir servidor: el connect server responde con la IP
  que determina su *IP resolver*. Con `-resolveIp:local` responde la IP LAN
  (192.168.1.87). Para test en la misma máquina, poner el resolver en `Loopback`
  en **Configuration → System** del panel, o arrancar con `-resolveIp:loopback`.

---

## 6. Estado actual

- [x] .NET SDK 10 instalado
- [x] PostgreSQL 17 instalado y servicio corriendo
- [x] Repo clonado en `openmu-aram`
- [x] `ConnectionSettings.xml` ajustado a la contraseña real
- [x] `dotnet restore` OK
- [x] `dotnet build -c Debug` OK (0 errores)
- [x] Arranque en modo `-demo` (en memoria) validado: host arriba en ~11 s,
      panel admin respondiendo en `http://localhost/`, listeners de connect
      (44405/44406), game (55901–55906) y chat OK, 0 errores/warnings en runtime
- [x] Arranque real contra **PostgreSQL** validado: la primera ejecución creó la
      base `openmu`, los esquemas (`config`, `data`, `friend`, `guild`, todos
      propiedad de `postgres`), los roles con login (`config`, `account`, `friend`,
      `guild`) con grants de mínimo privilegio, y los datos de **Season 6 Episode 3
      English** + 20 cuentas de prueba. Host arriba en ~36 s, 0 errores en runtime.
      Setup page: `Up-to-date`.
- [x] Cliente S6 descomprimido; método de conexión identificado (ClientLauncher)
- [x] Cliente lanzado con el ClientLauncher y **login validado en el juego**
      (`test0`/`test0`, entrada correcta) — 2026-08-28

**Entorno de desarrollo local completo y funcionando de punta a punta.**

---

## 7. Documentación oficial de referencia

- Requisitos y puertos: `docs-website/docs/getting-started/requirements.md`
- Correr desde código: `docs-website/docs/getting-started/from-source.md`
- Correr con Docker: `docs-website/docs/getting-started/docker.md`
- Conectar un cliente: `docs-website/docs/getting-started/game-client.md`
- Cuentas de prueba: `docs-website/docs/getting-started/test-accounts.md`
- Panel admin: `docs-website/docs/admin-panel/overview.md`
