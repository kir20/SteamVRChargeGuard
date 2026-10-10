# Builds dist\SteamVRChargeGuard.exe: a small .NET Framework 4.x program, nothing to install for users.
# Needs the C# compiler from Visual Studio or Visual Studio Build Tools (".NET desktop build tools").
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = $null
if (Test-Path $vswhere) {
    $csc = & $vswhere -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' |
        Sort-Object { [version](Get-Item $_).VersionInfo.FileVersionRaw } -Descending | Select-Object -First 1
}
if (-not $csc) { throw 'C# compiler not found. Install Visual Studio Build Tools with ".NET desktop build tools".' }

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$out = Join-Path $dist 'SteamVRChargeGuard.exe'
$sources = (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /deterministic /langversion:latest /codepage:65001 `
    "/win32icon:$root\assets\icon.ico" "/win32manifest:$root\src\app.manifest" `
    "/reference:$fw\System.Windows.Forms.dll" "/reference:$fw\System.Drawing.dll" `
    "/out:$out" $sources
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Get-Item $out | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ('SHA256 ' + (Get-FileHash $out -Algorithm SHA256).Hash.ToLower())
