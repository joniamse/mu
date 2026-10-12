# Compila el cliente MuMain (x64, Release, sin editor) con las Build Tools de VS 2022.
# Uso: .\client-src\build-client.ps1   ->  resultado en client-src\MuMain\out\build\windows-x64\src\Release
param([string]$Preset = 'windows-x64')

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'MuMain'

# Carga el entorno de MSVC (cl, cmake, ninja) en esta sesion
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$env:Path = "$vsInstaller;$env:Path"   # el link de .NET Native AOT invoca vswhere.exe por PATH
$vs = & "$vsInstaller\vswhere.exe" -latest -products * -property installationPath
Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null

# Despues del DevShell: este pisa VCPKG_ROOT con el vcpkg embebido de VS, que exige baseline
$env:VCPKG_ROOT = (Resolve-Path (Join-Path $PSScriptRoot '..\tools\vcpkg')).Path

git -C $src submodule update --init --recursive
Push-Location $src
try {
    cmake --preset $Preset
    if ($LASTEXITCODE) { throw "cmake configure fallo ($LASTEXITCODE)" }
    cmake --build --preset "$Preset-release"
    if ($LASTEXITCODE) { throw "cmake build fallo ($LASTEXITCODE)" }
}
finally { Pop-Location }
