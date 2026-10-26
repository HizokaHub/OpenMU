<#
.SYNOPSIS
    Regenera los 22 WAV del anunciador (el set instalado: voz Piper, kills y rachas en ingles fonetico, el resto en español).

.DESCRIPTION
    Dos modos:
    - Por defecto usa las tomas crudas guardadas en voces_crudas\ (las que se aprobaron). Solo hace falta python con numpy y scipy.
      El procesado es determinista: se obtienen EXACTAMENTE los WAV instalados.
    - Con -Piper y -Modelo genera tomas nuevas con Piper 2023.11.14-2 y el modelo es_MX-claude-high.onnx (ver LEEME.md para la descarga).
      Piper es estocastico: cada toma sale un poco distinta, asi que el resultado no sera identico al instalado.

    En ambos modos: mezcla con producir.py (VOZ_ESTILO=piper, VOZ_PERFIL=clasico); las 10 frases en ingles salen de la version
    fonetica y el resto de la normal.
#>
param(
    [Parameter(Mandatory)][string]$Salida,
    [string]$Piper,
    [string]$Modelo
)

$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot
$trabajo = Join-Path $Salida '_trabajo'
$ingles = 'first_blood', 'double', 'triple', 'quadra', 'penta', 'shutdown', 'spree', 'dominating', 'unstoppable', 'legendary'

$env:VOZ_ESTILO = 'piper'
$env:VOZ_PERFIL = 'clasico'

if ($Piper -and $Modelo) {
    $normal = "$trabajo\voz_normal"
    $fonetica = "$trabajo\voz_fonetica"
    python -I "$aqui\piper_voces.py" $Piper $Modelo $normal
    python -I "$aqui\piper_voces.py" $Piper $Modelo $fonetica --fonetico
}
else {
    $normal = "$aqui\voces_crudas\normal"
    $fonetica = "$aqui\voces_crudas\fonetica"
}

python -I "$aqui\producir.py" $normal "$trabajo\mezcla_normal"
python -I "$aqui\producir.py" $fonetica "$trabajo\mezcla_fonetica" @ingles

New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Copy-Item "$trabajo\mezcla_normal\*.wav" $Salida -Force
foreach ($clave in $ingles) { Copy-Item "$trabajo\mezcla_fonetica\$clave.wav" $Salida -Force }
Remove-Item $trabajo -Recurse -Force
Write-Host "Listo: $((Get-ChildItem $Salida -Filter *.wav).Count) WAV en $Salida"
