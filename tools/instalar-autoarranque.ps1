<#
.SYNOPSIS
    Crea (o quita) la tarea programada que ejecuta tools\iniciar-todo.ps1 cada vez que inicias sesion en Windows.

.DESCRIPTION
    - Tarea "Eclipse Arena - iniciar todo", solo para tu usuario, SIN privilegios elevados (no pide UAC).
    - Se dispara al iniciar sesion, con 60 s de espera para dar tiempo a ZeroTier y PostgreSQL.
    - Si el script falla (p. ej. ZeroTier aun sin IP), Windows lo reintenta hasta 5 veces, una por minuto.
    - Lo que ya este corriendo no se reinicia, asi que ejecutarla de mas es inofensivo.
    - Quitarla: .\tools\instalar-autoarranque.ps1 -Quitar
#>
param([switch]$Quitar)

$nombre = 'Eclipse Arena - iniciar todo'
$script = Join-Path $PSScriptRoot 'iniciar-todo.ps1'

Unregister-ScheduledTask -TaskName $nombre -Confirm:$false -ErrorAction SilentlyContinue
if ($Quitar) { Write-Host "Tarea '$nombre' eliminada."; return }

$usuario = "$env:USERDOMAIN\$env:USERNAME"
$accion = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
$disparador = New-ScheduledTaskTrigger -AtLogOn -User $usuario
$disparador.Delay = 'PT60S'
$ajustes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$principal = New-ScheduledTaskPrincipal -UserId $usuario -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $nombre -Action $accion -Trigger $disparador -Settings $ajustes -Principal $principal `
    -Description 'Levanta el servidor de Eclipse Arena y el portal al iniciar sesion (tools\iniciar-todo.ps1).' | Out-Null
Write-Host "Tarea '$nombre' creada para $usuario (al iniciar sesion, +60 s)."
