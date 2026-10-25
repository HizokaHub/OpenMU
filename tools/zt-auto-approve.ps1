<#
.SYNOPSIS
    Aprueba automaticamente a los amigos que se unen a la red ZeroTier, solo durante una ventana de tiempo.

.DESCRIPTION
    Consulta la API de ZeroTier Central cada pocos segundos y autoriza a los miembros pendientes.
    - Solo actua mientras el script corre (por defecto 60 minutos) y luego termina solo.
    - Nunca desautoriza ni borra a nadie.
    - Se detiene sola al llegar al maximo de equipos autorizados (el plan gratis permite 10, tu PC incluido).
    - El token de la API vive solo en tu PC: archivo %USERPROFILE%\.zerotier-token (una linea, fuera del repositorio)
      o variable de entorno ZEROTIER_TOKEN. Se crea en my.zerotier.com -> Account -> API Access Tokens.

.EXAMPLE
    .\tools\zt-auto-approve.ps1                 # 60 minutos
    .\tools\zt-auto-approve.ps1 -Minutes 15     # ventana corta
    .\tools\zt-auto-approve.ps1 -OnlyNew        # ignora a quienes ya estaban pendientes al empezar
#>
param(
    [int]$Minutes = 60,
    [int]$IntervalSeconds = 30,
    [int]$MaxAuthorized = 10,
    [string]$NetworkId = '3efa5cb78ac86a4b',
    [string]$TokenFile = (Join-Path $env:USERPROFILE '.zerotier-token'),
    [switch]$OnlyNew
)

$ErrorActionPreference = 'Stop'
$api = 'https://api.zerotier.com/api/v1'

$token = $env:ZEROTIER_TOKEN
if (-not $token -and (Test-Path $TokenFile)) { $token = (Get-Content $TokenFile -Raw).Trim() }
if (-not $token) {
    Write-Host 'Falta el token de la API de ZeroTier.' -ForegroundColor Yellow
    Write-Host '  1. Entra a https://my.zerotier.com/account -> "API Access Tokens" -> New Token.'
    Write-Host "  2. Guarda el token en una sola linea en: $TokenFile"
    Write-Host '     (o define la variable de entorno ZEROTIER_TOKEN). No lo pegues en chats ni lo subas a git.'
    exit 1
}
$headers = @{ Authorization = "token $token" }

function Get-Members {
    Invoke-RestMethod -Uri "$api/network/$NetworkId/member" -Headers $headers -Method Get
}

function Approve-Member($nodeId) {
    $body = @{ config = @{ authorized = $true } } | ConvertTo-Json -Depth 4
    Invoke-RestMethod -Uri "$api/network/$NetworkId/member/$nodeId" -Headers $headers -Method Post -Body $body -ContentType 'application/json' | Out-Null
}

try { $members = @(Get-Members) }
catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -in 401, 403) { Write-Host 'ZeroTier rechazo el token (401/403). Crea uno nuevo y guardalo otra vez.' -ForegroundColor Red }
    else { Write-Host "No se pudo consultar ZeroTier: $($_.Exception.Message)" -ForegroundColor Red }
    exit 1
}

$skip = @{}
if ($OnlyNew) { foreach ($m in $members) { if (-not $m.config.authorized) { $skip[$m.nodeId] = $true } } }

$end = (Get-Date).AddMinutes($Minutes)
Write-Host ("Aprobacion automatica ACTIVA hasta las {0:HH:mm} ({1} min). Red {2}. Ctrl+C para detener." -f $end, $Minutes, $NetworkId) -ForegroundColor Green
$approved = 0

while ((Get-Date) -lt $end) {
    try {
        $members = @(Get-Members)
        $authorized = @($members | Where-Object { $_.config.authorized }).Count
        foreach ($m in ($members | Where-Object { -not $_.config.authorized -and -not $skip.ContainsKey($_.nodeId) })) {
            if ($authorized -ge $MaxAuthorized) {
                Write-Host "Limite de $MaxAuthorized equipos autorizados alcanzado: no apruebo a $($m.nodeId)." -ForegroundColor Yellow
                $skip[$m.nodeId] = $true
                continue
            }
            Approve-Member $m.nodeId
            $authorized++
            $approved++
            Write-Host ("{0:HH:mm:ss}  APROBADO {1}  (IP fisica: {2})" -f (Get-Date), $m.nodeId, $m.physicalAddress) -ForegroundColor Cyan
        }
    }
    catch { Write-Host ("{0:HH:mm:ss}  aviso: {1}" -f (Get-Date), $_.Exception.Message) -ForegroundColor DarkYellow }
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host "Ventana terminada. Aprobados en esta sesion: $approved. La aprobacion automatica ya NO esta activa." -ForegroundColor Green
