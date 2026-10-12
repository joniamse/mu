# Abre el cliente MuMain compilado, conectado al servidor local (connect server para MuMain: 44406).
param([string]$Ip = '127.127.127.127', [int]$Port = 44406)

$dir = Join-Path $PSScriptRoot 'MuMain\out\build\windows-x64\src\Release'
Start-Process -FilePath (Join-Path $dir 'Main.exe') -WorkingDirectory $dir -ArgumentList 'connect', "/u$Ip", "/p$Port"
