<#
.SYNOPSIS
    Publica una version del cliente de Eclipse Arena para el actualizador: manifiesto de hashes, copias por hash y version minima.

.DESCRIPTION
    Ejecutar con Windows PowerShell (powershell.exe), no con pwsh.
    1. Prepara el cliente: copia tools\cliente\Actualizar.ps1 y las DLL de Visual C++ a la carpeta del cliente y escribe eclipse-build.txt
       (el numero de version que el juego manda al servidor).
    2. Calcula el SHA-256 de cada archivo (con cache por tamano y fecha, asi que la segunda vez tarda segundos).
    3. Copia cada archivo nuevo a <Salida>\blobs\<sha256> (el actualizador baja solo los hashes que le faltan).
    4. Escribe <Salida>\manifest.json (ruta, tamano, hash) y <Salida>\version.json (version, version minima, novedades).
    5. Con -ExigirActualizacion (o -MinimoBuild N) escribe tambien moba-client.json junto al servidor: el servidor rechazara
       las partidas de clientes con una version menor. Sin esas opciones se conserva la minima anterior.
    La version es el numero AAMMDDHHmm de la publicacion (crece siempre). El portal sirve <Salida> en /updates.

.EXAMPLE
    .\tools\publicar-cliente.ps1 -Notas "Pings, anunciador y musica." -ExigirActualizacion
#>
param(
    [string]$ClientDir = 'C:\Users\aruiz\Proyectos\mu-main\out\build\windows-x64\src\Release',
    [string]$Salida = (Join-Path $PSScriptRoot '..\portal\updates'),
    [long]$Build = [long](Get-Date -Format 'yyMMddHHmm'),
    [string]$Notas = '',
    [switch]$ExigirActualizacion,
    [long]$MinimoBuild = 0,
    [string]$ServidorDir = (Join-Path $PSScriptRoot '..\src\Startup\bin\Debug'),
    [string]$GuardarEstadoEn = '',
    [switch]$Limpiar,
    [switch]$SinPreparar
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength = [int]::MaxValue
$ClientDir = (Resolve-Path -LiteralPath $ClientDir).Path.TrimEnd('\')
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$Salida = (Resolve-Path -LiteralPath $Salida).Path
$blobs = Join-Path $Salida 'blobs'
New-Item -ItemType Directory -Force -Path $blobs | Out-Null

function Json-Texto([string]$texto) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($c in $texto.ToCharArray()) {
        $n = [int]$c
        if ($n -eq 34) { [void]$sb.Append('\"') }
        elseif ($n -eq 92) { [void]$sb.Append('\\') }
        elseif ($n -lt 32 -or $n -gt 126) { [void]$sb.AppendFormat('\u{0:x4}', $n) }
        else { [void]$sb.Append($c) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

function Json-Manifiesto([long]$build, [string]$generado, $archivos) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{"build":' + $build + ',"generated":' + (Json-Texto $generado) + ',"files":[')
    $primero = $true
    foreach ($f in $archivos) {
        if (-not $primero) { [void]$sb.Append(',') }
        $primero = $false
        [void]$sb.Append('[' + (Json-Texto ([string]$f[0])) + ',' + [long]$f[1] + ',' + (Json-Texto ([string]$f[2])) + ']')
    }
    [void]$sb.Append(']}')
    return $sb.ToString()
}

function Json-Estado([long]$build, $archivos) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{"build":' + $build + ',"files":{')
    $primero = $true
    foreach ($f in $archivos) {
        if (-not $primero) { [void]$sb.Append(',') }
        $primero = $false
        [void]$sb.Append((Json-Texto ([string]$f[0])) + ':[' + [long]$f[1] + ',' + (Json-Texto ([string]$f[2])) + ']')
    }
    [void]$sb.Append('}}')
    return $sb.ToString()
}

# Lo que NO forma parte del juego instalado (ajustes del usuario, registros, restos de la compilacion).
$excluir = @(
    '^config\.ini$', '^config\.ini\.template$', '^MuError\.log$', '^MuClient\.lib$', '^actualizador/', '\.pdb$', '\.ilk$', '\.exp$',
    '^logs?/', '^Screenshot', '(^|/)Thumbs\.db$'
)

# ------------------------------------------------------------------ 1. preparar el cliente

if (-not $SinPreparar) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'cliente\Actualizar.ps1') -Destination (Join-Path $ClientDir 'Actualizar.ps1') -Force
    $crt = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT' -Directory -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($crt) {
        foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') {
            $origen = Join-Path $crt.FullName $dll
            if (Test-Path $origen) { Copy-Item -LiteralPath $origen -Destination (Join-Path $ClientDir $dll) -Force }
        }
    }
    [System.IO.File]::WriteAllText((Join-Path $ClientDir 'eclipse-build.txt'), [string]$Build, (New-Object System.Text.ASCIIEncoding))
}

# ------------------------------------------------------------------ 2. hashes (con cache)

$cacheFile = Join-Path $Salida 'hash-cache.json'
$cache = @{}
if (Test-Path $cacheFile) {
    try { $cache = $serializer.DeserializeObject([System.IO.File]::ReadAllText($cacheFile)) } catch { $cache = @{} }
}

$sha = [System.Security.Cryptography.SHA256]::Create()
$archivos = New-Object System.Collections.ArrayList
$nuevoCache = @{}
$hasheados = 0
$lista = Get-ChildItem -LiteralPath $ClientDir -Recurse -File
$i = 0
foreach ($archivo in $lista) {
    $i++
    $relativa = $archivo.FullName.Substring($ClientDir.Length + 1).Replace('\', '/')
    $ignorar = $false
    foreach ($patron in $excluir) { if ($relativa -match $patron) { $ignorar = $true; break } }
    if ($ignorar) { continue }

    $ticks = $archivo.LastWriteTimeUtc.Ticks
    $hash = $null
    if ($cache.ContainsKey($relativa)) {
        $entrada = $cache[$relativa]
        if ([long]$entrada[0] -eq $archivo.Length -and [long]$entrada[1] -eq $ticks) { $hash = [string]$entrada[2] }
    }
    if (-not $hash) {
        $flujo = [System.IO.File]::OpenRead($archivo.FullName)
        try { $hash = ([System.BitConverter]::ToString($sha.ComputeHash($flujo))).Replace('-', '').ToLowerInvariant() }
        finally { $flujo.Dispose() }
        $hasheados++
    }
    $nuevoCache[$relativa] = @([long]$archivo.Length, [long]$ticks, $hash)
    [void]$archivos.Add(@($relativa, [long]$archivo.Length, $hash))
    if (($i % 1000) -eq 0) { Write-Host ("  {0} / {1} archivos..." -f $i, $lista.Count) }
}
[System.IO.File]::WriteAllText($cacheFile, $serializer.Serialize($nuevoCache), (New-Object System.Text.UTF8Encoding($false)))

# ------------------------------------------------------------------ 3. blobs

$bytes = [long]0
$blobsNuevos = 0
$bytesNuevos = [long]0
$vigentes = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($f in $archivos) {
    $bytes += [long]$f[1]
    $hash = [string]$f[2]
    [void]$vigentes.Add($hash)
    $destino = Join-Path $blobs $hash
    if (-not (Test-Path -LiteralPath $destino)) {
        Copy-Item -LiteralPath (Join-Path $ClientDir ($f[0].Replace('/', '\'))) -Destination $destino -Force
        $blobsNuevos++
        $bytesNuevos += [long]$f[1]
    }
}
$retirados = 0
if ($Limpiar) {
    foreach ($blob in Get-ChildItem -LiteralPath $blobs -File) {
        if (-not $vigentes.Contains($blob.Name)) { Remove-Item -LiteralPath $blob.FullName -Force; $retirados++ }
    }
}

# ------------------------------------------------------------------ 4. manifiesto y version

$utf8 = New-Object System.Text.UTF8Encoding($false)
$generado = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
[System.IO.File]::WriteAllText((Join-Path $Salida 'manifest.json'), (Json-Manifiesto $Build $generado $archivos), $utf8)

$versionFile = Join-Path $Salida 'version.json'
$minimoAnterior = [long]0
if (Test-Path $versionFile) {
    try { $minimoAnterior = [long]($serializer.DeserializeObject([System.IO.File]::ReadAllText($versionFile))['minimumBuild']) } catch { $minimoAnterior = 0 }
}
$minimo = $minimoAnterior
if ($ExigirActualizacion) { $minimo = $Build }
if ($MinimoBuild -gt 0) { $minimo = $MinimoBuild }
$versionJson = '{"build":' + $Build + ',"minimumBuild":' + $minimo + ',"notes":' + (Json-Texto $Notas) + ',"files":' + $archivos.Count + ',"bytes":' + $bytes + ',"generated":' + (Json-Texto $generado) + '}'
[System.IO.File]::WriteAllText($versionFile, $versionJson, $utf8)

# ------------------------------------------------------------------ 5. version minima para el servidor

if ($ServidorDir -and (Test-Path $ServidorDir)) {
    [System.IO.File]::WriteAllText((Join-Path $ServidorDir 'moba-client.json'), ('{"minimumBuild":' + $minimo + ',"latestBuild":' + $Build + '}'), $utf8)
}

# ------------------------------------------------------------------ estado inicial para el instalador

if ($GuardarEstadoEn) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $GuardarEstadoEn) | Out-Null
    [System.IO.File]::WriteAllText($GuardarEstadoEn, (Json-Estado $Build $archivos), $utf8)
}

Write-Host ("Version {0} publicada: {1} archivos, {2:N0} MB; {3} copias nuevas ({4:N0} MB), {5} hasheados, {6} retiradas." -f $Build, $archivos.Count, ($bytes / 1MB), $blobsNuevos, ($bytesNuevos / 1MB), $hasheados, $retirados)
Write-Host ("Version minima exigida: {0}" -f $minimo)
