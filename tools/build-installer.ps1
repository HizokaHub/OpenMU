<#
.SYNOPSIS
    Compila el instalador de Eclipse Arena (portal\downloads\EclipseArena-Setup.exe) con Inno Setup 6.

.DESCRIPTION
    Genera un config.ini limpio (sin credenciales guardadas, con la IP del servidor), localiza las DLL de
    Visual C++ que necesita Main.exe y llama a ISCC. Requiere Inno Setup 6 (winget install JRSoftware.InnoSetup).

.EXAMPLE
    .\tools\build-installer.ps1 -ServerIP 172.27.71.8
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerIP,
    [int]$ServerPort = 44406,
    [string]$NetworkId = '3efa5cb78ac86a4b',
    [string]$ClientDir = 'C:\Users\aruiz\Proyectos\mu-main\out\build\windows-x64\src\Release',
    [string]$Version = (Get-Date -Format 'yyyy.MM.dd')
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = [System.IO.Path]::GetFullPath((Join-Path $here '..\portal\downloads'))

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'No encuentro Inno Setup 6 (winget install JRSoftware.InnoSetup).' }
if (-not (Test-Path (Join-Path $ClientDir 'Main.exe'))) { throw "No encuentro Main.exe en $ClientDir" }
if ($ServerIP -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw "ServerIP no parece una IP: $ServerIP" }

# DLL de Visual C++ (x64): se instalan junto al juego para que funcione en un PC limpio.
$crt = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT' -Directory -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $crt) { throw 'No encuentro las DLL redistribuibles de Visual C++ (VC\Redist\MSVC\...\x64\Microsoft.VC*.CRT).' }

$work = Join-Path $env:TEMP 'EclipseArenaInstaller'
New-Item -ItemType Directory -Force $work | Out-Null
$cfg = Get-Content (Join-Path $ClientDir 'config.ini') -Raw
$cfg = $cfg -replace '(?m)^RememberMe=.*$', 'RememberMe=0'
$cfg = $cfg -replace '(?m)^EncryptedUsername=.*$', 'EncryptedUsername='
$cfg = $cfg -replace '(?m)^EncryptedPassword=.*$', 'EncryptedPassword='
$cfg = $cfg -replace '(?m)^SavePassword=.*$', 'SavePassword=0'
$cfg = $cfg -replace '(?m)^ServerIP=.*$', "ServerIP=$ServerIP"
$cfg = $cfg -replace '(?m)^ServerPort=.*$', "ServerPort=$ServerPort"
$configFile = Join-Path $work 'config.ini'
[System.IO.File]::WriteAllText($configFile, $cfg, (New-Object System.Text.UTF8Encoding($false)))

New-Item -ItemType Directory -Force $outDir | Out-Null
& $iscc "/DClientDir=$ClientDir" "/DConfigFile=$configFile" "/DCrtDir=$($crt.FullName)" "/DOutDir=$outDir" `
    "/DServerIP=$ServerIP" "/DNetworkId=$NetworkId" "/DAppVersion=$Version" (Join-Path $here 'installer\EclipseArena.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo con codigo $LASTEXITCODE" }

$exe = Join-Path $outDir 'EclipseArena-Setup.exe'
"Listo: $exe ($([math]::Round((Get-Item $exe).Length / 1MB)) MB)"
