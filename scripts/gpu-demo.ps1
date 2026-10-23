param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1'); & (Join-Path $PSScriptRoot 'build-native.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
# This explicitly requested interactive launcher shows the controller; the source stays offscreen.
Start-Process -FilePath $hostExe -ArgumentList '--gpu-browser-demo' -WorkingDirectory $projectRoot -WindowStyle Normal
