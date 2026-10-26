<#
.SYNOPSIS
    Actualizador de Eclipse Arena: baja solo los archivos que cambiaron y abre el juego.

.DESCRIPTION
    1. Lee http://<ServerIP>:8080/updates/version.json (la ServerIP sale de config.ini) y, si hay una version nueva, el
       manifiesto /updates/manifest.json (ruta, tamano y SHA-256 de cada archivo).
    2. Compara con lo instalado y descarga solo lo que falta o cambio (en paralelo, comprobando el hash de cada archivo).
    3. Borra los archivos que ya no forman parte del juego y guarda lo instalado en actualizador\instalado.json.
    4. Abre Main.exe.
    Nunca toca config.ini, los registros ni nada que no este en el manifiesto. Si el servidor de actualizaciones no responde,
    abre el juego con la version instalada (el servidor del juego rechazara un cliente demasiado viejo).
    Archivo ASCII a proposito: Windows PowerShell 5.1 lee los .ps1 sin BOM con la pagina de codigos local.

.PARAMETER Url
    Direccion del portal (por defecto http://<ServerIP de config.ini>:8080).
.PARAMETER Carpeta
    Carpeta del juego (por defecto, la de este script).
.PARAMETER Verificar
    Comprueba el hash de TODOS los archivos (repara los danados); sin esto se fia del estado guardado.
.PARAMETER NoLanzar
    No abre el juego al terminar (pruebas).
.PARAMETER SinVentana
    Sin ventana: escribe el progreso en la consola (pruebas).
#>
param(
    [string]$Url,
    [string]$Carpeta = $PSScriptRoot,
    [switch]$Verificar,
    [switch]$NoLanzar,
    [switch]$SinVentana,
    [string]$Exe = 'Main.exe'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[System.Net.ServicePointManager]::DefaultConnectionLimit = 32
Add-Type -AssemblyName System.Web.Extensions
if (-not $SinVentana) { Add-Type -AssemblyName System.Windows.Forms, System.Drawing }

$PuertoPortal = 8080
$Paralelo = 6
$Reintentos = 3
$TimeoutMs = 8000

$Carpeta = (Resolve-Path -LiteralPath $Carpeta).Path.TrimEnd('\')
$estadoDir = Join-Path $Carpeta 'actualizador'
$estadoFile = Join-Path $estadoDir 'instalado.json'
$tmpDir = Join-Path $estadoDir 'tmp'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength = [int]::MaxValue
$serializer.RecursionLimit = 64

# ------------------------------------------------------------------ ventana

$script:form = $null
$script:etiqueta = $null
$script:barra = $null
$script:notas = $null
$script:omitir = $false

function Iniciar-Ventana {
    if ($SinVentana) { return }
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Eclipse Arena'
    $form.ClientSize = New-Object System.Drawing.Size(520, 250)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.BackColor = [System.Drawing.Color]::FromArgb(14, 10, 28)
    $form.ForeColor = [System.Drawing.Color]::FromArgb(235, 230, 255)

    $titulo = New-Object System.Windows.Forms.Label
    $titulo.Text = 'ECLIPSE ARENA'
    $titulo.Font = New-Object System.Drawing.Font('Bahnschrift', 18, [System.Drawing.FontStyle]::Bold)
    $titulo.Location = New-Object System.Drawing.Point(18, 12)
    $titulo.Size = New-Object System.Drawing.Size(480, 36)
    $titulo.ForeColor = [System.Drawing.Color]::FromArgb(155, 123, 255)
    $form.Controls.Add($titulo)

    $etiqueta = New-Object System.Windows.Forms.Label
    $etiqueta.Location = New-Object System.Drawing.Point(20, 56)
    $etiqueta.Size = New-Object System.Drawing.Size(480, 22)
    $form.Controls.Add($etiqueta)

    $barra = New-Object System.Windows.Forms.ProgressBar
    $barra.Location = New-Object System.Drawing.Point(20, 84)
    $barra.Size = New-Object System.Drawing.Size(480, 18)
    $barra.Minimum = 0
    $barra.Maximum = 1000
    $form.Controls.Add($barra)

    $notas = New-Object System.Windows.Forms.TextBox
    $notas.Multiline = $true
    $notas.ReadOnly = $true
    $notas.ScrollBars = 'Vertical'
    $notas.Location = New-Object System.Drawing.Point(20, 114)
    $notas.Size = New-Object System.Drawing.Size(480, 88)
    $notas.BackColor = [System.Drawing.Color]::FromArgb(24, 18, 44)
    $notas.ForeColor = [System.Drawing.Color]::FromArgb(200, 195, 225)
    $notas.BorderStyle = 'None'
    $form.Controls.Add($notas)

    $boton = New-Object System.Windows.Forms.Button
    $boton.Text = 'Omitir y jugar'
    $boton.Location = New-Object System.Drawing.Point(380, 212)
    $boton.Size = New-Object System.Drawing.Size(120, 28)
    $boton.FlatStyle = 'Flat'
    $boton.Add_Click({ $script:omitir = $true })
    $form.Controls.Add($boton)

    $form.Show()
    $script:form = $form
    $script:etiqueta = $etiqueta
    $script:barra = $barra
    $script:notas = $notas
}

function Estado([string]$texto, [double]$fraccion = -1) {
    if ($SinVentana) {
        Write-Host $texto
        return
    }
    if ($script:etiqueta) { $script:etiqueta.Text = $texto }
    if ($fraccion -ge 0 -and $script:barra) { $script:barra.Value = [int][math]::Min(1000, [math]::Max(0, $fraccion * 1000)) }
    [System.Windows.Forms.Application]::DoEvents()
}

function Mostrar-Notas([string]$texto) {
    if ($SinVentana) {
        if ($texto) { Write-Host "Novedades: $texto" }
        return
    }
    if ($script:notas) { $script:notas.Text = $texto }
    [System.Windows.Forms.Application]::DoEvents()
}

function Cerrar-Ventana {
    if ($script:form) { $script:form.Close(); $script:form.Dispose(); $script:form = $null }
}

function Aviso([string]$texto) {
    if ($SinVentana) {
        Write-Host $texto
        return
    }
    [void][System.Windows.Forms.MessageBox]::Show($texto, 'Eclipse Arena')
}

# ------------------------------------------------------------------ utilidades

function Get-UrlPortal {
    if ($Url) { return $Url.TrimEnd('/') }
    $config = Join-Path $Carpeta 'config.ini'
    if (Test-Path -LiteralPath $config) {
        $linea = Select-String -LiteralPath $config -Pattern '^\s*ServerIP\s*=\s*(\S+)' | Select-Object -First 1
        if ($linea) { return ('http://{0}:{1}' -f $linea.Matches[0].Groups[1].Value, $PuertoPortal) }
    }
    return $null
}

function Get-Texto([string]$direccion) {
    $req = [System.Net.HttpWebRequest]::Create($direccion)
    $req.Timeout = $TimeoutMs
    $req.ReadWriteTimeout = $TimeoutMs
    $req.UserAgent = 'EclipseArena-Updater'
    $req.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    $resp = $req.GetResponse()
    try {
        $lector = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
        return $lector.ReadToEnd()
    }
    finally { $resp.Close() }
}

function Get-Hash([string]$ruta) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $flujo = [System.IO.File]::OpenRead($ruta)
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($flujo))).Replace('-', '').ToLowerInvariant() }
    finally { $flujo.Dispose(); $sha.Dispose() }
}

function Ruta-Local([string]$relativa) {
    return (Join-Path $Carpeta ($relativa -replace '/', '\'))
}

function Lanzar-Juego {
    if ($NoLanzar) { return }
    $juego = Join-Path $Carpeta $Exe
    if (-not (Test-Path -LiteralPath $juego)) {
        Aviso "No encuentro $Exe en $Carpeta. Reinstala el juego."
        return
    }
    Start-Process -FilePath $juego -WorkingDirectory $Carpeta
}

function Leer-Estado {
    if (-not (Test-Path -LiteralPath $estadoFile)) { return $null }
    try { return $serializer.DeserializeObject([System.IO.File]::ReadAllText($estadoFile, [System.Text.Encoding]::UTF8)) }
    catch { return $null }
}

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

function Guardar-Estado([long]$build, $archivos) {
    New-Item -ItemType Directory -Force -Path $estadoDir | Out-Null
    $temporal = $estadoFile + '.nuevo'
    [System.IO.File]::WriteAllText($temporal, (Json-Estado $build $archivos), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporal -Destination $estadoFile -Force
}

# ------------------------------------------------------------------ plan: que hay que bajar

function Planificar($archivos, $instalado) {
    $faltan = New-Object System.Collections.ArrayList
    $total = $archivos.Count
    $revisados = 0
    foreach ($f in $archivos) {
        $relativa = [string]$f[0]
        $tamano = [long]$f[1]
        $hash = [string]$f[2]
        $ruta = Ruta-Local $relativa
        $revisados++
        if (($revisados % 200) -eq 0) {
            Estado ("Comprobando archivos ({0} de {1})..." -f $revisados, $total) (0.30 * $revisados / $total)
            if ($script:omitir) { return $null }
        }

        if (-not (Test-Path -LiteralPath $ruta -PathType Leaf)) { [void]$faltan.Add($f); continue }
        if ((Get-Item -LiteralPath $ruta).Length -ne $tamano) { [void]$faltan.Add($f); continue }
        if (-not $Verificar -and $instalado -and $instalado.ContainsKey($relativa) -and ([string]$instalado[$relativa][1]) -eq $hash) { continue }
        if ((Get-Hash $ruta) -ne $hash) { [void]$faltan.Add($f) }
    }
    return ,$faltan
}

# ------------------------------------------------------------------ descarga

function Descargar-Blobs([string]$portal, $faltan) {
    $porHash = @{}
    foreach ($f in $faltan) {
        $h = [string]$f[2]
        if (-not $porHash.ContainsKey($h)) { $porHash[$h] = New-Object System.Collections.ArrayList }
        [void]$porHash[$h].Add($f)
    }

    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    $pendientes = New-Object System.Collections.Queue
    $bytesTotal = [long]0
    foreach ($h in $porHash.Keys) {
        $tam = [long]$porHash[$h][0][1]
        $bytesTotal += $tam
        $pendientes.Enqueue(@{ Hash = $h; Tam = $tam; Intento = 1 })
    }

    $activos = New-Object System.Collections.ArrayList
    $fallos = New-Object System.Collections.ArrayList
    $bytesListos = [long]0
    $listos = 0
    $totalBlobs = $porHash.Count
    while (($pendientes.Count -gt 0 -or $activos.Count -gt 0) -and -not $script:omitir) {
        while ($activos.Count -lt $Paralelo -and $pendientes.Count -gt 0) {
            $item = $pendientes.Dequeue()
            $destino = Join-Path $tmpDir $item.Hash
            if (Test-Path -LiteralPath $destino) { Remove-Item -LiteralPath $destino -Force }
            $cliente = New-Object System.Net.WebClient
            $cliente.Headers['User-Agent'] = 'EclipseArena-Updater'
            $item.Cliente = $cliente
            $item.Destino = $destino
            $item.Tarea = $cliente.DownloadFileTaskAsync(($portal + '/updates/blobs/' + $item.Hash), $destino)
            [void]$activos.Add($item)
        }

        foreach ($item in @($activos)) {
            if (-not $item.Tarea.IsCompleted) { continue }
            [void]$activos.Remove($item)
            $bien = -not $item.Tarea.IsFaulted -and -not $item.Tarea.IsCanceled
            $item.Cliente.Dispose()
            if ($bien) { $bien = ((Get-Hash $item.Destino) -eq $item.Hash) }
            if ($bien) {
                $bytesListos += $item.Tam
                $listos++
            }
            elseif ($item.Intento -lt $Reintentos) {
                $item.Intento++
                $pendientes.Enqueue($item)
            }
            else {
                [void]$fallos.Add($item.Hash)
            }
        }

        $fraccion = if ($bytesTotal -gt 0) { [double]$bytesListos / [double]$bytesTotal } else { 1.0 }
        Estado ("Descargando ({0} de {1} archivos, {2:N1} de {3:N1} MB)..." -f $listos, $totalBlobs, ($bytesListos / 1MB), ($bytesTotal / 1MB)) (0.30 + 0.60 * $fraccion)
        Start-Sleep -Milliseconds 60
    }

    foreach ($item in $activos) { try { $item.Cliente.CancelAsync() } catch { } }
    return @{ PorHash = $porHash; Fallos = $fallos }
}

# ------------------------------------------------------------------ instalacion

function Instalar-Blobs($resultado) {
    $problemas = New-Object System.Collections.ArrayList
    $n = 0
    $total = $resultado.PorHash.Count
    foreach ($h in $resultado.PorHash.Keys) {
        $n++
        if ($resultado.Fallos.Contains($h)) {
            foreach ($f in $resultado.PorHash[$h]) { [void]$problemas.Add([string]$f[0]) }
            continue
        }

        $origen = Join-Path $tmpDir $h
        if (-not (Test-Path -LiteralPath $origen)) {
            foreach ($f in $resultado.PorHash[$h]) { [void]$problemas.Add([string]$f[0]) }
            continue
        }

        foreach ($f in $resultado.PorHash[$h]) {
            $destino = Ruta-Local ([string]$f[0])
            try {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destino) | Out-Null
                Copy-Item -LiteralPath $origen -Destination $destino -Force
            }
            catch { [void]$problemas.Add([string]$f[0]) }
        }
        Remove-Item -LiteralPath $origen -Force -ErrorAction SilentlyContinue
        if (($n % 25) -eq 0) { Estado ("Instalando ({0} de {1})..." -f $n, $total) (0.90 + 0.08 * $n / $total) }
    }
    return ,$problemas
}

function Borrar-Obsoletos($archivos, $instalado) {
    if (-not $instalado) { return 0 }
    $vigentes = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($f in $archivos) { [void]$vigentes.Add([string]$f[0]) }
    $borrados = 0
    foreach ($relativa in @($instalado.Keys)) {
        if ($vigentes.Contains($relativa)) { continue }
        $ruta = Ruta-Local $relativa
        if (Test-Path -LiteralPath $ruta -PathType Leaf) {
            try { Remove-Item -LiteralPath $ruta -Force; $borrados++ } catch { }
        }
    }
    return $borrados
}

# ------------------------------------------------------------------ principal

Iniciar-Ventana
try {
    Estado 'Buscando actualizaciones...' 0.02
    $portal = Get-UrlPortal
    $version = $null
    if ($portal) {
        try { $version = $serializer.DeserializeObject((Get-Texto ($portal + '/updates/version.json'))) }
        catch { $version = $null }
    }

    if (-not $version) {
        Estado 'Servidor de actualizaciones sin respuesta: se abre la version instalada.' 1
        Start-Sleep -Milliseconds 1200
        Lanzar-Juego
        return
    }

    $estado = Leer-Estado
    $instalado = if ($estado) { $estado['files'] } else { $null }
    $buildNuevo = [long]$version['build']
    $buildActual = if ($estado) { [long]$estado['build'] } else { 0 }
    Mostrar-Notas ([string]$version['notes'])
    if ($estado -and $buildActual -eq $buildNuevo -and -not $Verificar) {
        Estado 'El juego esta al dia.' 1
        Start-Sleep -Milliseconds 500
        Lanzar-Juego
        return
    }

    Estado 'Descargando la lista de archivos...' 0.05
    $manifiesto = $serializer.DeserializeObject((Get-Texto ($portal + '/updates/manifest.json')))
    $archivos = $manifiesto['files']
    $faltan = Planificar $archivos $instalado
    if ($null -eq $faltan) { Lanzar-Juego; return }

    $problemas = New-Object System.Collections.ArrayList
    if ($faltan.Count -gt 0) {
        $resultado = Descargar-Blobs $portal $faltan
        if ($script:omitir) { Lanzar-Juego; return }
        $problemas = Instalar-Blobs $resultado
    }

    $borrados = Borrar-Obsoletos $archivos $instalado
    if ($problemas.Count -gt 0) {
        Aviso ("No se pudieron actualizar {0} archivo(s) (por ejemplo {1}). Cierra el juego si esta abierto y abre Eclipse Arena otra vez." -f $problemas.Count, $problemas[0])
    }
    else {
        Guardar-Estado $buildNuevo $archivos
        if (Test-Path -LiteralPath $tmpDir) { Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Estado ("Listo: {0} archivo(s) actualizado(s), {1} retirado(s)." -f $faltan.Count, $borrados) 1
    Start-Sleep -Milliseconds 700
    if ($problemas.Count -eq 0 -or (Test-Path -LiteralPath (Join-Path $Carpeta $Exe))) { Lanzar-Juego }
}
catch {
    Aviso ("El actualizador fallo: {0}`nSe abre la version instalada." -f $_.Exception.Message)
    Lanzar-Juego
}
finally {
    Cerrar-Ventana
}
