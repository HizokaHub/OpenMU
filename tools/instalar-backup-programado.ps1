<#
.SYNOPSIS
    Crea (o quita) la tarea programada que ejecuta tools\backup-db.ps1 todos los dias.

.DESCRIPTION
    - Tarea "Eclipse Arena - backup de la base de datos", solo para tu usuario, SIN privilegios elevados (no pide UAC).
    - Se ejecuta cada dia a las 04:30; si el PC estaba apagado, se ejecuta en cuanto se pueda (StartWhenAvailable).
    - Quitarla: .\tools\instalar-backup-programado.ps1 -Quitar
#>
param([switch]$Quitar, [string]$Hora = '04:30')

$nombre = 'Eclipse Arena - backup de la base de datos'
$script = Join-Path $PSScriptRoot 'backup-db.ps1'

Unregister-ScheduledTask -TaskName $nombre -Confirm:$false -ErrorAction SilentlyContinue
if ($Quitar) { Write-Host "Tarea '$nombre' eliminada."; return }

$usuario = "$env:USERDOMAIN\$env:USERNAME"
$accion = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
$disparador = New-ScheduledTaskTrigger -Daily -At $Hora
$ajustes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
$principal = New-ScheduledTaskPrincipal -UserId $usuario -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $nombre -Action $accion -Trigger $disparador -Settings $ajustes -Principal $principal `
    -Description 'Copia diaria de la base de datos del juego con rotacion (tools\backup-db.ps1).' | Out-Null
Write-Host "Tarea '$nombre' creada para $usuario (cada dia a las $Hora)."
