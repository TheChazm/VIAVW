param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$hostExe = Join-Path $projectRoot 'src\LayerHost\bin\Release\net10.0-windows\LayerHost.exe'
$probeExe = Join-Path $projectRoot 'src\LayerProbe\bin\Release\net10.0-windows\LayerProbe.exe'
$artifactDir = Join-Path $projectRoot ('artifacts\native-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& $hostExe --self-test $probeExe $artifactDir
if ($LASTEXITCODE -ne 0) { throw "Verification failed; inspect $artifactDir\verification.json" }
Write-Output "Results: $artifactDir"
