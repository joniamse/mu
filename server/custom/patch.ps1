# Maneja el parche de nuestros cambios al nucleo de OpenMU (custom/patches/openmu-core.patch).
# El submodulo server/OpenMU apunta al OpenMU oficial; nuestros cambios viven en el parche, que el
# build de Docker (custom/Dockerfile) aplica. Uso: .\server\custom\patch.ps1 [apply | revert | update | status]
#   apply   aplica el parche al submodulo (para compilar o depurar en local)
#   revert  lo quita y deja el submodulo como el oficial
#   update  regenera el parche desde los cambios del submodulo (despues de editar el nucleo)
#   status  dice si esta aplicado
param([string]$Action = 'status')

# Los avisos de git van a stderr; con 'Stop' PowerShell 5 los toma como errores.
$ErrorActionPreference = 'Continue'
$openMu = Join-Path $PSScriptRoot '..\OpenMU'
$patch = Join-Path $PSScriptRoot 'patches\openmu-core.patch'

function Test-Applied {
    git -C $openMu apply --reverse --check $patch 2>$null
    return $LASTEXITCODE -eq 0
}

switch ($Action) {
    'apply' {
        if (Test-Applied) { Write-Host 'El parche ya esta aplicado.'; break }
        git -C $openMu apply --whitespace=nowarn $patch
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo aplicar el parche (el submodulo tiene otros cambios o cambio de version).' }
        Write-Host 'Parche aplicado.'
    }
    'revert' {
        if (-not (Test-Applied)) { Write-Host 'El parche no esta aplicado.'; break }
        git -C $openMu apply --reverse $patch
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo quitar el parche.' }
        Write-Host 'Parche quitado; el submodulo quedo como el oficial.'
    }
    'update' {
        # Los archivos nuevos entran al diff con "intent to add", sin tocar el indice de verdad.
        git -C $openMu add --intent-to-add -- src
        git -C $openMu diff --binary --output=$patch -- src
        git -C $openMu reset --quiet -- src
        Write-Host "Parche regenerado: $patch"
    }
    'status' {
        if (Test-Applied) { Write-Host 'Aplicado.' } else { Write-Host 'No aplicado.' }
    }
    default { Write-Error "Accion desconocida: $Action (apply | revert | update | status)" }
}
