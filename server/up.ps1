# Levanta (o actualiza) el servidor local. Uso: .\server\up.ps1  |  .\server\up.ps1 down
param([string]$Action = 'up')

$base = Join-Path $PSScriptRoot 'OpenMU\deploy\all-in-one\docker-compose.yml'
$local = Join-Path $PSScriptRoot 'docker-compose.local.yml'
$compose = @('compose', '-f', $base, '-f', $local)

switch ($Action) {
    'up'   { docker @compose up -d --no-build }
    'down' { docker @compose down }
    'logs' { docker @compose logs -f openmu-startup }
    'pull' { docker @compose pull }
    default { Write-Error "Accion desconocida: $Action (up | down | logs | pull)" }
}
