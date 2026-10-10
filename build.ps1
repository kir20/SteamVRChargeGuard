# Builds dist\SteamVRChargeGuard.exe (one-file, no console window).
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$venv = Join-Path $root '.venv-build'
$py = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path $py)) { python -m venv $venv }
& $py -m pip install --disable-pip-version-check -q -r (Join-Path $root 'requirements-build.txt')
& $py -I (Join-Path $root 'assets\make_icon.py')
& $py -m PyInstaller --noconfirm --clean --onefile --windowed `
    --name SteamVRChargeGuard `
    --icon (Join-Path $root 'assets\icon.ico') `
    --add-data "$(Join-Path $root 'assets\icon.ico');." `
    --version-file (Join-Path $root 'assets\version_info.txt') `
    --distpath (Join-Path $root 'dist') `
    --workpath (Join-Path $root 'build') `
    --specpath (Join-Path $root 'build') `
    (Join-Path $root 'src\main.py')
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root 'dist\SteamVRChargeGuard.exe') | Select-Object Name, Length, LastWriteTime
