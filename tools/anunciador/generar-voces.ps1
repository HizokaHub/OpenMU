param([string]$OutDir)

Add-Type -AssemblyName System.Speech
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# clave | texto (con \u para acentos) | palabra enfatizada (o vacio) | tono | ritmo
# tono: x-low low medium high ; ritmo: x-slow slow medium fast
$lineas = @(
    'first_blood|Primera sangre|sangre|low|slow'
    'double|Doble muerte|Doble|low|slow'
    'triple|Triple muerte|Triple|low|slow'
    'quadra|Cuádruple muerte|Cuádruple|low|slow'
    'penta|Penta muerte|Penta|x-low|x-slow'
    'ace|Masacre|Masacre|x-low|slow'
    'ace_lost|Equipo aniquilado|aniquilado|low|slow'
    'shutdown|Racha cortada|cortada|low|slow'
    'spree|Racha de muertes|muertes|low|slow'
    'dominating|Dominando|Dominando|x-low|slow'
    'unstoppable|Imparable|Imparable|x-low|slow'
    'legendary|Legendario|Legendario|x-low|x-slow'
    'turret_ally|Has perdido una torre|perdido|low|slow'
    'turret_enemy|Torre enemiga destruida|destruida|low|slow'
    'nexus_attack|Tu nexo está bajo ataque|ataque|medium|medium'
    'match_start|Que comience la batalla|batalla|low|slow'
    'victory|Victoria|Victoria|x-low|x-slow'
    'defeat|Derrota|Derrota|x-low|x-slow'
    'golden_flyover|Dragones dorados sobre el reino|dorados|low|slow'
    'golden_invaders|Invasión dorada|dorada|x-low|slow'
    'dragon_ally|Dragón dorado abatido|abatido|low|slow'
    'dragon_enemy|El enemigo abatió al dragón|enemigo|low|medium'
)

$voz = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voz.SelectVoice('Microsoft Helena Desktop')
$formato = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(44100, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)

foreach ($linea in $lineas) {
    $partes = $linea.Split('|')
    $clave = $partes[0]
    $texto = [regex]::Unescape($partes[1])
    $enfasis = [regex]::Unescape($partes[2])
    $tono = $partes[3]
    $ritmo = $partes[4]

    $marcado = $texto
    if ($enfasis) {
        $marcado = $texto.Replace($enfasis, '<emphasis level="strong">' + $enfasis + '</emphasis>')
    }
    $ssml = '<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="es-ES"><prosody pitch="' + $tono + '" rate="' + $ritmo + '" volume="x-loud">' + $marcado + '</prosody></speak>'

    $ruta = Join-Path $OutDir ($clave + '.wav')
    $voz.SetOutputToWaveFile($ruta, $formato)
    try {
        $voz.SpeakSsml($ssml)
        $modo = 'ssml'
    } catch {
        $voz.Speak($texto)
        $modo = 'plano'
    }
    $voz.SetOutputToNull()
    Write-Output ("{0} [{1}]" -f $clave, $modo)
}
