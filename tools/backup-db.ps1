<#
.SYNOPSIS
    Copia de seguridad de la base de datos del juego (PostgreSQL "openmu") con rotacion.

.DESCRIPTION
    - Ejecuta pg_dump (formato custom, comprimido) hacia backups\openmu_AAAAMMDD_HHmm.dump.
    - La contrasena se lee en el momento del archivo ConnectionSettings.xml del servidor y solo vive en el entorno de este
      proceso: no se imprime, no se guarda en el repo ni en el registro.
    - Comprueba cada copia con pg_restore --list (que el archivo se pueda leer) y borra las anteriores a -Dias dias,
      conservando siempre las ultimas -MinimoCopias.
    - Escribe backups\backup-db.log. Termina con codigo distinto de 0 si algo falla.
    - Restaurar: pg_restore -h localhost -U postgres -d openmu --clean --if-exists <archivo.dump> (con el servidor parado).
#>
param(
    [string]$Config = (Join-Path $PSScriptRoot '..\src\Startup\bin\Debug\ConnectionSettings.xml'),
    [string]$Destino = (Join-Path $PSScriptRoot '..\backups'),
    [int]$Dias = 14,
    [int]$MinimoCopias = 5
)

$ErrorActionPreference = 'Stop'
$pgBin = 'C:\Program Files\PostgreSQL\17\bin'
$registro = Join-Path $Destino 'backup-db.log'

function Escribir($texto) {
    $linea = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $texto
    Add-Content -Path $registro -Value $linea
    Write-Host $linea
}

New-Item -ItemType Directory -Force -Path $Destino | Out-Null
try {
    [xml]$xml = Get-Content -Path $Config -Raw
    $cadena = $xml.ConnectionSettings.Connections.Connection[0].ConnectionString
    $datos = @{}
    foreach ($par in ($cadena -split ';')) {
        if ($par -match '^\s*([^=]+)=(.*)$') { $datos[$Matches[1].Trim()] = $Matches[2].Trim() }
    }

    $archivo = Join-Path $Destino ('openmu_{0:yyyyMMdd_HHmm}.dump' -f (Get-Date))
    $env:PGPASSWORD = $datos['Password']
    & "$pgBin\pg_dump.exe" -h $datos['Server'] -p $datos['Port'] -U $datos['User Id'] -d $datos['Database'] -Fc -f $archivo
    if ($LASTEXITCODE -ne 0) { throw "pg_dump termino con codigo $LASTEXITCODE" }

    & "$pgBin\pg_restore.exe" --list $archivo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'la copia no se pudo leer con pg_restore --list' }
    Escribir ('OK  {0}  {1:N1} MB' -f (Split-Path $archivo -Leaf), ((Get-Item $archivo).Length / 1MB))

    $copias = Get-ChildItem -Path $Destino -Filter 'openmu_*.dump' | Sort-Object LastWriteTime -Descending
    $viejas = $copias | Select-Object -Skip $MinimoCopias | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$Dias) }
    foreach ($vieja in $viejas) {
        Remove-Item -Path $vieja.FullName -Force
        Escribir ('Borrada copia antigua {0}' -f $vieja.Name)
    }
}
catch {
    Escribir ('ERROR  {0}' -f $_.Exception.Message)
    exit 1
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
