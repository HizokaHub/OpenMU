<#
.SYNOPSIS
    Levanta (o detiene) todo Eclipse Arena en este PC: comprueba ZeroTier, firewall y PostgreSQL, y arranca el servidor y el portal.

.DESCRIPTION
    Sin parametros: arranca todo.
      1. ZeroTier: servicio activo, red 3efa5cb78ac86a4b en estado OK y con la IP 172.27.71.8.
      2. Firewall: existe y esta activa la regla "Eclipse Arena (ZeroTier)" (se crea con tools\open-firewall-zerotier.ps1).
      3. PostgreSQL escuchando en 5432.
      4. Servidor del juego: MUnique.OpenMU.Startup.exe -autostart -resolveIp:172.27.71.8 (espera al puerto 44406).
      5. Portal web en el puerto 8080.
    Lo que ya este corriendo se deja como esta (no se reinicia).

    -AprobarAmigos <min>  Ademas lanza tools\zt-auto-approve.ps1 durante <min> minutos (ventana minimizada) para que los
              amigos que se unen a la red ZeroTier queden autorizados solos. Necesita el token en %USERPROFILE%\.zerotier-token.
              Termina sola al acabar la ventana; -Parar la corta antes.
    -Estado   Solo informa: no arranca ni detiene nada.
    -Parar    Detiene el portal y apaga el servidor con limpieza (Ctrl+C + confirmacion, como a mano). Se niega si hay
              jugadores conectados, salvo con -Forzar. Los procesos se identifican por su PUERTO (nunca por nombre).

    No necesita administrador. Los registros quedan en logs\ (el anterior se guarda como *_prev.log).

.EXAMPLE
    .\tools\iniciar-todo.ps1             # arrancar todo
    .\tools\iniciar-todo.ps1 -Estado     # ver como esta todo
    .\tools\iniciar-todo.ps1 -Parar      # parada limpia
    .\tools\iniciar-todo.ps1 -SinPortal  # solo el servidor del juego
#>
param(
    [switch]$Parar,
    [switch]$Estado,
    [switch]$Forzar,
    [switch]$SinPortal,
    [int]$AprobarAmigos = 0,
    [string]$VpnIp = '172.27.71.8',
    [string]$NetworkId = '3efa5cb78ac86a4b'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$serverExe = Join-Path $root 'src\Startup\bin\Debug\MUnique.OpenMU.Startup.exe'
$portalDir = Join-Path $root 'portal'
$portalExe = Join-Path $portalDir 'bin\Debug\net10.0\MuMobaPortal.exe'
$logDir = Join-Path $root 'logs'
$gamePort = 44406
$portalPort = 8080
$firewallRule = 'Eclipse Arena (ZeroTier)'
$ztCli = 'C:\ProgramData\ZeroTier\One\zerotier-one_x64.exe'

function Ok($t)    { Write-Host "  [OK]    $t" -ForegroundColor Green }
function Aviso($t) { Write-Host "  [AVISO] $t" -ForegroundColor Yellow }
function Mal($t)   { Write-Host "  [FALLO] $t" -ForegroundColor Red }
function Paso($t)  { Write-Host "`n$t" -ForegroundColor Cyan }

# Proceso (PID) que escucha en un puerto TCP, o $null.
function Get-PidDelPuerto([int]$port) {
    $c = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($c) { return [int]$c.OwningProcess }
    return $null
}

function Esperar-Puerto([int]$port, [int]$segundos) {
    $fin = (Get-Date).AddSeconds($segundos)
    while ((Get-Date) -lt $fin) {
        if (Get-PidDelPuerto $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Rotar-Log([string]$nombre) {
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $actual = Join-Path $logDir "$nombre.log"
    if ((Test-Path $actual) -and (Get-Item $actual).Length -gt 0) {
        Move-Item $actual (Join-Path $logDir "${nombre}_prev.log") -Force
    }
    Remove-Item (Join-Path $logDir "$nombre.err.log") -Force -ErrorAction SilentlyContinue
}

# --- Comprobaciones ---------------------------------------------------------------------------

function Test-ZeroTier {
    $svc = Get-Service ZeroTierOneService -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne 'Running') {
        Mal 'El servicio de ZeroTier no esta activo (ZeroTierOneService). Iniciarlo exige administrador: abre ZeroTier desde el menu Inicio o ejecuta "Start-Service ZeroTierOneService" como administrador.'
        return $false
    }
    $linea = (& $ztCli -q listnetworks 2>&1 | Where-Object { $_ -match $NetworkId } | Select-Object -First 1)
    if (-not $linea) { Mal "Este PC no esta unido a la red $NetworkId. Ejecuta: zerotier-cli join $NetworkId"; return $false }
    if ($linea -notmatch '\sOK\s') { Mal "La red ZeroTier no esta en estado OK: $linea (revisa que este PC este autorizado en my.zerotier.com)"; return $false }
    if ($linea -notmatch [regex]::Escape($VpnIp)) { Mal "La red esta OK pero este PC no tiene la IP $VpnIp ($linea). Los amigos y el portal usan esa IP: no sigo."; return $false }
    Ok "ZeroTier: red $NetworkId OK, IP $VpnIp"
    return $true
}

function Test-Firewall {
    # netsh funciona sin administrador (Get-NetFirewallRule no ve las reglas sin permisos elevados).
    # El texto de salida depende del idioma de Windows, asi que se leen los campos por su posicion en vez de por etiqueta.
    $salida = @(& netsh advfirewall firewall show rule name="$firewallRule" 2>&1 | ForEach-Object { "$_" })
    $lineas = @($salida | Where-Object { $_ -match '^\S[^:]*:\s+\S' })
    if ($LASTEXITCODE -ne 0 -or $lineas.Count -lt 3) {
        Aviso "Falta la regla de firewall '$firewallRule': los amigos no podran conectar. Crearla (pide UAC): .\tools\open-firewall-zerotier.ps1"
        return $false
    }
    $habilitada = ($lineas[1] -split ':', 2)[1].Trim()
    if ($habilitada -notmatch '^(S|Y)') { Aviso "La regla '$firewallRule' existe pero esta desactivada."; return $false }
    $puertos = ($salida | Where-Object { $_ -match '\d{4,5}(-\d{4,5})?(,\d{4,5}(-\d{4,5})?)+' } | Select-Object -First 1)
    $puertos = if ($puertos) { ($puertos -split ':', 2)[1].Trim() } else { '?' }
    Ok "Firewall: regla '$firewallRule' activa (puertos: $puertos)"
    return $true
}

function Test-Postgres {
    if (Get-PidDelPuerto 5432) { Ok 'PostgreSQL escuchando en 5432'; return $true }
    Mal 'PostgreSQL no escucha en 5432 (servicio postgresql-x64-17). Iniciarlo exige administrador.'
    return $false
}

# La aprobacion automatica se identifica por el PID guardado, comprobando ademas que ese PID sea de zt-auto-approve.
$aprobarPidFile = Join-Path $logDir 'zt-auto-approve.pid'
function Get-PidAprobacion {
    if (-not (Test-Path $aprobarPidFile)) { return $null }
    $id = [int](Get-Content $aprobarPidFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
    if ($p -and $p.CommandLine -match 'zt-auto-approve') { return $id }
    return $null
}

function Get-EstadoPortal {
    try { return Invoke-RestMethod -Uri "http://127.0.0.1:$portalPort/api/status" -TimeoutSec 5 } catch { return $null }
}

function Show-Estado {
    $pidJuego = Get-PidDelPuerto $gamePort
    $pidPortal = Get-PidDelPuerto $portalPort
    if ($pidJuego) { Ok "Servidor del juego corriendo (PID $pidJuego, puerto $gamePort)" } else { Mal "Servidor del juego parado (nadie escucha en $gamePort)" }
    if ($pidPortal) { Ok "Portal corriendo (PID $pidPortal, puerto $portalPort)" } else { Mal "Portal parado (nadie escucha en $portalPort)" }
    $st = if ($pidPortal) { Get-EstadoPortal } else { $null }
    $pidAprob = Get-PidAprobacion
    if ($pidAprob) { Ok "Aprobacion automatica de amigos ACTIVA (PID $pidAprob)" } else { Write-Host '  [--]    Aprobacion automatica de amigos inactiva' }
    if ($st) { Write-Host ("  Jugadores: {0}   En partida: {1}   Servidor visible desde el portal: {2}" -f $st.players, $st.inMatch, $st.online) }
}

# --- Estado -----------------------------------------------------------------------------------

if ($Estado) {
    Paso 'Comprobaciones'
    [void](Test-ZeroTier); [void](Test-Firewall); [void](Test-Postgres)
    Paso 'Procesos'
    Show-Estado
    return
}

# --- Parar ------------------------------------------------------------------------------------

if ($Parar) {
    Paso 'Parada limpia'
    $pidJuego = Get-PidDelPuerto $gamePort
    $pidPortal = Get-PidDelPuerto $portalPort

    if ($pidJuego -and -not $Forzar) {
        $st = if ($pidPortal) { Get-EstadoPortal } else { $null }
        if ($st -and $st.players -gt 0) {
            Mal ("Hay {0} jugador(es) conectado(s) ({1} en partida). No apago nada. Avisales o usa -Forzar." -f $st.players, $st.inMatch)
            return
        }
        if (-not $st) { Aviso 'No pude consultar cuantos jugadores hay (portal parado o sin esquema moba); sigo con la parada.' }
    }

    $pidAprob = Get-PidAprobacion
    if ($pidAprob) { Stop-Process -Id $pidAprob -Force -ErrorAction SilentlyContinue; Ok "Aprobacion automatica detenida (PID $pidAprob)" }
    Remove-Item $aprobarPidFile -Force -ErrorAction SilentlyContinue

    if ($pidPortal) {
        Stop-Process -Id $pidPortal -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        if (Get-PidDelPuerto $portalPort) { Stop-Process -Id $pidPortal -Force -ErrorAction SilentlyContinue }
        Ok "Portal detenido (PID $pidPortal)"
    } else { Ok 'Portal ya estaba parado' }

    if ($pidJuego) {
        Write-Host "  Apagando el servidor (PID $pidJuego) con Ctrl+C + confirmacion..."
        $ctrlc = Join-Path $PSScriptRoot 'enviar-ctrlc.ps1'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ctrlc -ProcessId $pidJuego | Out-Null
        $fin = (Get-Date).AddSeconds(45)
        while ((Get-Process -Id $pidJuego -ErrorAction SilentlyContinue) -and (Get-Date) -lt $fin) { Start-Sleep -Milliseconds 500 }
        if (Get-Process -Id $pidJuego -ErrorAction SilentlyContinue) {
            Aviso 'El servidor no se cerro solo en 45 s: lo cierro a la fuerza.'
            Stop-Process -Id $pidJuego -Force
            Ok "Servidor detenido a la fuerza (PID $pidJuego)"
        } else { Ok "Servidor apagado con limpieza (PID $pidJuego)" }
    } else { Ok 'Servidor ya estaba parado' }
    return
}

# --- Arrancar ---------------------------------------------------------------------------------

Paso '1/3  Comprobaciones'
if (-not (Test-ZeroTier)) { exit 1 }
if (-not (Test-Postgres)) { exit 1 }
[void](Test-Firewall)
if (-not (Test-Path $serverExe)) { Mal "No existe $serverExe. Compila la solucion primero (dotnet build)."; exit 1 }
if (-not $SinPortal -and -not (Test-Path $portalExe)) { Mal "No existe $portalExe. Compila el portal primero (dotnet build portal)."; exit 1 }

Paso '2/3  Servidor del juego'
if (Get-PidDelPuerto $gamePort) {
    Ok "Ya estaba corriendo (PID $(Get-PidDelPuerto $gamePort)); no lo toco."
} else {
    Rotar-Log 'servidor'
    $p = Start-Process -FilePath $serverExe -ArgumentList '-autostart', "-resolveIp:$VpnIp" `
        -WorkingDirectory (Split-Path $serverExe) -WindowStyle Minimized `
        -RedirectStandardOutput (Join-Path $logDir 'servidor.log') -RedirectStandardError (Join-Path $logDir 'servidor.err.log') -PassThru
    Write-Host "  Arrancando (PID $($p.Id)); esperando al puerto $gamePort (hasta 120 s)..."
    if (Esperar-Puerto $gamePort 120) { Ok "Servidor listo (PID $(Get-PidDelPuerto $gamePort)). Registro: logs\servidor.log" }
    else { Mal "El servidor no abrio el puerto $gamePort en 120 s. Revisa logs\servidor.log y logs\servidor.err.log"; exit 1 }
}

Paso '3/3  Portal web'
if ($SinPortal) { Write-Host '  (omitido por -SinPortal)' }
elseif (Get-PidDelPuerto $portalPort) {
    Ok "Ya estaba corriendo (PID $(Get-PidDelPuerto $portalPort)); no lo toco."
} else {
    Rotar-Log 'portal'
    $p = Start-Process -FilePath $portalExe -WorkingDirectory $portalDir -WindowStyle Minimized `
        -RedirectStandardOutput (Join-Path $logDir 'portal.log') -RedirectStandardError (Join-Path $logDir 'portal.err.log') -PassThru
    if (Esperar-Puerto $portalPort 30) { Ok "Portal listo (PID $(Get-PidDelPuerto $portalPort)). Registro: logs\portal.log" }
    else { Mal "El portal no abrio el puerto $portalPort en 30 s. Revisa logs\portal.log y logs\portal.err.log"; exit 1 }
}

if ($AprobarAmigos -gt 0) {
    Paso 'Aprobacion automatica de amigos'
    $tokenFile = Join-Path $env:USERPROFILE '.zerotier-token'
    if (Get-PidAprobacion) { Ok "Ya estaba activa (PID $(Get-PidAprobacion)); no la toco." }
    elseif (-not ($env:ZEROTIER_TOKEN -or (Test-Path $tokenFile))) { Aviso "Falta el token de ZeroTier ($tokenFile): no activo la aprobacion automatica." }
    else {
        New-Item -ItemType Directory -Force $logDir | Out-Null
        $script = Join-Path $PSScriptRoot 'zt-auto-approve.ps1'
        $p = Start-Process powershell.exe -WindowStyle Minimized -PassThru `
            -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Minutes', $AprobarAmigos
        Set-Content $aprobarPidFile $p.Id
        Ok "Aprobacion automatica activa durante $AprobarAmigos min (PID $($p.Id)); termina sola."
    }
}

Paso 'Resumen'
Show-Estado
Write-Host "`n  Web para los amigos (solo dentro de la VPN): http://${VpnIp}:$portalPort" -ForegroundColor Green
Write-Host '  Amigos nuevos: .\tools\iniciar-todo.ps1 -AprobarAmigos 60   |   Parar todo: .\tools\iniciar-todo.ps1 -Parar'
