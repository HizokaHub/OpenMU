<#
.SYNOPSIS
    Arma el zip del cliente de Eclipse Arena para repartirlo a los amigos.

.DESCRIPTION
    Comprime la carpeta Release del cliente (MuMain) sin copiarla antes. Deja fuera MuError.log y MuClient.lib,
    reemplaza config.ini por uno limpio (sin credenciales guardadas, con la IP del servidor) y agrega LEEME.txt.

.EXAMPLE
    .\tools\build-client-package.ps1 -ServerIP 100.101.102.103
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerIP,
    [int]$ServerPort = 44406,
    [string]$ClientDir = 'C:\Users\aruiz\Proyectos\mu-main\out\build\windows-x64\src\Release',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Output) { $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\portal\downloads\EclipseArena-cliente.zip' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path (Join-Path $ClientDir 'Main.exe'))) { throw "No encuentro Main.exe en $ClientDir" }
if ($ServerIP -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw "ServerIP no parece una IP: $ServerIP" }

$Output = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
if (Test-Path $Output) { Remove-Item $Output -Force }

# config.ini limpio a partir del actual: sin usuario guardado y con la IP del servidor.
$cfg = Get-Content (Join-Path $ClientDir 'config.ini') -Raw
$cfg = $cfg -replace '(?m)^RememberMe=.*$', 'RememberMe=0'
$cfg = $cfg -replace '(?m)^EncryptedUsername=.*$', 'EncryptedUsername='
$cfg = $cfg -replace '(?m)^EncryptedPassword=.*$', 'EncryptedPassword='
$cfg = $cfg -replace '(?m)^SavePassword=.*$', 'SavePassword=0'
$cfg = $cfg -replace '(?m)^ServerIP=.*$', "ServerIP=$ServerIP"
$cfg = $cfg -replace '(?m)^ServerPort=.*$', "ServerPort=$ServerPort"

$readme = @"
ECLIPSE ARENA - MU MOBA ONLINE
Servidor privado de MU Online hecho por fans, sin fines de lucro y sin afiliacion con Webzen.

1. Instala la VPN que te indico el administrador y unete a su red (sin eso el juego no encuentra el servidor).
2. Descomprime esta carpeta donde quieras (ocupa ~1,1 GB). Solo Windows.
3. Crea tu cuenta en la web del servidor (te dara usuario y codigo de seguridad).
4. Abre Main.exe, entra con tu usuario y elige el servidor de la lista.
5. En Lorencia habla con el Maestro de la Arena para crear o unirte a una sala.

Servidor: ${ServerIP}:${ServerPort}
Si el juego no abre, instala "Visual C++ Redistributable 2015-2022 (x64)" de Microsoft.
"@

$exclude = @('MuError.log', 'MuClient.lib', 'config.ini', 'config.ini.template')
$root = (Resolve-Path $ClientDir).Path.TrimEnd('\')
$prefix = 'EclipseArena/'

function Add-Text($zip, $name, $text) {
    $entry = $zip.CreateEntry($prefix + $name, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $writer.Write($text)
    $writer.Dispose()
}

$zip = [System.IO.Compression.ZipFile]::Open($Output, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $count = 0
    Get-ChildItem $root -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1)
        if ($exclude -contains $rel) { return }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $prefix + ($rel -replace '\\', '/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $count++
    }
    Add-Text $zip 'config.ini' $cfg
    Add-Text $zip 'LEEME.txt' $readme
}
finally {
    $zip.Dispose()
}

$size = [math]::Round((Get-Item $Output).Length / 1MB)
"Listo: $Output ($size MB, $count archivos + config.ini + LEEME.txt)"
