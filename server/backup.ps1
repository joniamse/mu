# Backup / restore de la DB de OpenMU (formato custom de pg_dump) en D:\Mu\backups.
# Uso: .\server\backup.cmd [etiqueta]          -> crea backups\openmu-AAAAMMDD-HHmm[-etiqueta].dump
#      .\server\backup.cmd restore <archivo>    -> restaura ese backup (pisa la DB actual)
param([string]$Action = '', [string]$File = '')

$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot '..\backups'
New-Item -ItemType Directory -Force $dir | Out-Null

if ($Action -eq 'restore') {
    if (-not (Test-Path $File)) { throw "No existe el backup: $File" }
    docker stop openmu-startup | Out-Null
    # cmd hace el redirect binario; PowerShell 5 corromperia el stream
    cmd /c "docker exec -i database pg_restore -U postgres -d openmu --clean --if-exists < `"$((Resolve-Path $File).Path)`""
    docker start openmu-startup | Out-Null
    Write-Host "Restaurado $File. El servidor se esta reiniciando."
    return
}

$suffix = if ($Action) { "-$Action" } else { '' }
$out = Join-Path (Resolve-Path $dir) ("openmu-{0:yyyyMMdd-HHmm}{1}.dump" -f (Get-Date), $suffix)
cmd /c "docker exec database pg_dump -U postgres -d openmu -Fc > `"$out`""
if ($LASTEXITCODE) { throw "pg_dump fallo ($LASTEXITCODE)" }
Write-Host ("Backup: {0} ({1:N1} MB)" -f $out, ((Get-Item $out).Length / 1MB))
