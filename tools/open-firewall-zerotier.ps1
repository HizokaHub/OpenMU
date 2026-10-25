<#
.SYNOPSIS
    Abre en el firewall de Windows SOLO los puertos del juego, y solo para la red ZeroTier (172.27.0.0/16).

.DESCRIPTION
    Puertos: 44405 y 44406 (connect server), 55901-55906 (game servers), 55980 (chat) y 8080 (web del portal).
    NO abre el 5000 (panel de administracion) ni PostgreSQL (5432).
    Se eleva solo (pide permiso de administrador). Para quitarlo: .\tools\open-firewall-zerotier.ps1 -Remove
#>
param([switch]$Remove)

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $MyInvocation.MyCommand.Path)
    if ($Remove) { $arguments += '-Remove' }
    Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait
    return
}

$name = 'Eclipse Arena (ZeroTier)'
Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($Remove) { Write-Host 'Regla eliminada.'; Start-Sleep 4; return }

New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP `
    -LocalPort 44405, 44406, 55901-55906, 55980, 8080 -RemoteAddress 172.27.0.0/16 -Profile Any | Out-Null
Write-Host 'Listo: puertos del juego abiertos solo para la red ZeroTier (172.27.0.0/16).'
Get-NetFirewallRule -DisplayName $name | Get-NetFirewallPortFilter | Format-Table Protocol, LocalPort
Start-Sleep 4
