<#
.SYNOPSIS
    Envia Ctrl+C a la consola de un proceso (lo usa iniciar-todo.ps1 -Parar para apagar el servidor con limpieza).

.DESCRIPTION
    Se ejecuta en un PowerShell hijo para no tocar la consola del que lo llama.
    El servidor pide confirmar el apagado: la primera pulsacion pregunta y la segunda lo confirma,
    asi que el script envia -Veces pulsaciones separadas por -PausaMs.
#>
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [int]$Veces = 2,
    [int]$PausaMs = 1500
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CtrlC
{
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint group);
}
'@

[void][CtrlC]::FreeConsole()
if (-not [CtrlC]::AttachConsole([uint32]$ProcessId)) {
    Write-Error "No se pudo enlazar con la consola del proceso $ProcessId (error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    exit 2
}
[void][CtrlC]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)   # este proceso ignora el Ctrl+C que va a enviar
for ($i = 0; $i -lt $Veces; $i++) {
    [void][CtrlC]::GenerateConsoleCtrlEvent(0, 0)
    Start-Sleep -Milliseconds $PausaMs
}
[void][CtrlC]::FreeConsole()
