# Maneja el servidor local. Uso: .\server\up.cmd [up | build | down | logs | pull]
#   up    levanta con la ultima imagen compilada
#   build recompila la imagen desde server/OpenMU/src y levanta
param([string]$Action = 'up')

$base = Join-Path $PSScriptRoot 'OpenMU\deploy\all-in-one\docker-compose.yml'
$local = Join-Path $PSScriptRoot 'docker-compose.local.yml'
$compose = @('compose', '-f', $base, '-f', $local)

switch ($Action) {
    'up'    { docker @compose up -d }
    'build' {
        docker @compose up -d --build
        # nginx resuelve la IP del contenedor al arrancar; si este se recreo, da 502 hasta reiniciarlo
        docker restart nginx-80 | Out-Null
    }
    'down'  { docker @compose down }
    'logs'  { docker @compose logs -f openmu-startup }
    'pull'  { docker @compose pull database nginx-80 }
    default { Write-Error "Accion desconocida: $Action (up | build | down | logs | pull)" }
}
